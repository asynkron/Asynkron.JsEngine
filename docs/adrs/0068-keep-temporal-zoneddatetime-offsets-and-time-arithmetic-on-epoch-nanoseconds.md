# 0068. Keep Temporal ZonedDateTime offsets and time-only arithmetic on epoch nanoseconds

## Status

Accepted.

## Context

`Temporal.ZonedDateTime` exposes several surfaces whose results depend on
selecting the correct UTC offset for an exact instant:

- `offsetNanoseconds` and `offset`
- the cached `LocalDateTime` projection
- `add` and `subtract` for time-only durations such as `PT1H`
- internal helpers that resolve the active offset for formatting

Prior to issue #860 / PR #1197 these surfaces routed through a mix of:

1. `Instant.ToDateTimeOffset()` — which truncates the BigInteger epoch
   nanoseconds to .NET ticks (100 ns) before asking
   `TimeZoneInfo.GetUtcOffset(...)` for the offset; and
2. `GetLocalPlainDateTime` followed by local-time disambiguation — which
   converts the wall clock back through `TimeZoneInfo` to find an instant.

Both routes interact badly with DST transitions.

The Test262 fixture `Temporal_ZonedDateTime_prototype_offsetNanoseconds`
`transition-minus-one-nanosecond` asks for the offset one nanosecond before a
forward DST transition. With the lossy `DateTimeOffset` truncation the instant
slid onto the post-transition side of the boundary, and the wrong offset was
returned. Symmetrically, the `offsetNanoseconds` getter parsed the formatted
offset string back into seconds, multiplied by `10^9`, and dropped sub-second
offsets entirely.

For time-only `ZonedDateTime.add`/`subtract`, routing through the local
PlainDateTime path can fail completely: at a forward transition the resulting
wall-clock time does not exist, and at a backward transition it is ambiguous.
Adding `PT1H` to an instant near the transition has a well-defined answer in
the exact-instant domain even when the local clock does not.

## Decision

Keep `Temporal.ZonedDateTime` offset lookups and time-only arithmetic on the
exact epoch-nanosecond instant representation:

1. **Offset lookup uses epoch nanoseconds with floor semantics.** Convert
   `BigInteger` epoch nanoseconds to ticks with floor division (`epochNs / 100`,
   rounding toward `-∞` for negative remainders), then ask
   `TimeZoneInfo.GetUtcOffset(...)` for the offset on that tick boundary. This
   keeps instants in the interval `[transition - 1 ns, transition)` on the
   pre-transition side of the boundary.
2. **`offsetNanoseconds` returns the model value directly.** Use the stored
   `OffsetNanoseconds` rather than re-parsing the formatted offset string.
3. **Time-only `add`/`subtract` short-circuits the local PlainDateTime path.**
   When the duration has zero `years`, `months`, `weeks`, and `days`, combine
   the hour/minute/second/millisecond/microsecond/nanosecond components into a
   `BigInteger` nanosecond delta, apply it to the instant directly, validate
   against the shared Temporal instant bounds, and build the result
   ZonedDateTime from that instant without revisiting the local clock.
4. **Larger durations stay on the spec's local-clock path.** Year/month/week/day
   arithmetic still needs the calendar-driven local PlainDateTime step
   (`AddZonedDateTime`); only purely time-component arithmetic is allowed to
   shortcut.

The shared offset-from-epoch helper lives on
`TemporalHistoricalTimeZoneOffsets` so every call site converts identically and
historical/synthetic zone overrides keep one entry point.

## Consequences

- Pro: `Temporal.ZonedDateTime` offset queries are exact at DST transition
  boundaries down to one nanosecond, matching ECMAScript Temporal semantics.
- Pro: Time-only ZonedDateTime arithmetic no longer fails or produces wrong
  results at skipped or ambiguous wall times.
- Pro: `offsetNanoseconds` no longer pays a string-parse round-trip and cannot
  silently drop sub-second offset precision.
- Con: Two arithmetic paths exist in `add`/`subtract`. The classifier (the
  zero-Y/M/W/D check) must stay correct; misclassifying a calendar duration as
  time-only would skip the calendar-aware local path.
- Floor-tick conversion can throw `OverflowException` if epoch nanoseconds are
  outside the `long`-tick range. Existing instant-range validation at
  construction time (ADR 0055-class checks for `JsTemporalInstant`) keeps that
  guarded for valid Temporal values.

## Proof

- Focused Test262: `Name=Temporal_ZonedDateTime_prototype_offsetNanoseconds`
  passed (8 tests) after the change. The `transition-minus-one-nanosecond`
  fixture exercised both the offset lookup and the model-value direct read.
- Implementation diff is bounded to:
  - `src/Asynkron.JsEngine/JsTypes/JsTemporalZonedDateTime.cs`
    (offset, offsetNanoseconds, LocalDateTime, internal helper)
  - `src/Asynkron.JsEngine/StdLib/Temporal/TemporalHelper.cs`
    (`offsetNanoseconds` getter, AddZonedDateTime time-only shortcut, removed
    obsolete offset-string parser)
  - `src/Asynkron.JsEngine/StdLib/Temporal/TemporalHistoricalTimeZoneOffsets.cs`
    (new `GetUtcOffset(string, TimeZoneInfo, BigInteger)` overload, floor-tick
    conversion helper)

## Related

- Issue #860, PR #1197.
- ADR 0046 — Temporal property-bag observability (broader Temporal rule family).
- ADR family 0055/0056/0067 — keep Temporal kind-specific representations on
  their owning field domain instead of host conversions.
