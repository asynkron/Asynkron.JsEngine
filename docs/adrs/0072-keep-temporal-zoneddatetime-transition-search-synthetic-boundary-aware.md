# 0072. Keep Temporal ZonedDateTime transition search synthetic-boundary aware

## Status

Accepted.

## Context

`Temporal.ZonedDateTime.prototype.getTimeZoneTransition` returns the next or
previous instant at which the receiver's time zone changes UTC offset.
Asynkron.JsEngine implements this by binary-searching `TimeZoneInfo` for a
neighbouring offset transition around the receiver's epoch nanoseconds.

Several historical IANA zones cannot be modelled by .NET `TimeZoneInfo`
adjustment rules alone. The engine therefore overrides offsets for known
windows in `TemporalHistoricalTimeZoneOffsets`, such as Europe/London's
permanent British Standard Time experiment (1968-02-18T02:00Z through
1971-10-31T02:00Z) and America/Anchorage's pre-1969 GMT-10 standard offset.
Outside the broader Temporal runtime, the rest of ZonedDateTime offset reads
already route through this override path.

Before issue #859 / PR #1200, the transition search reached into
`TimeZoneInfo` directly with several silent assumptions:

1. The receiver's epoch nanoseconds were divided by `1_000_000` to a
   millisecond `long` before constructing a `DateTimeOffset`. That truncated
   sub-millisecond information and could place the search start on the wrong
   side of a transition that lives at a tick boundary.
2. The binary search bisected on `TotalSeconds > 1`, then snapped the result
   to a second boundary. Sub-second transition instants reported by Temporal
   could disagree with that snap by up to one second.
3. `tz.GetUtcOffset(...)` was called instead of the
   `TemporalHistoricalTimeZoneOffsets` helper, so the search did not see
   override windows that the rest of the engine already observed.
4. Zones with `GetAdjustmentRules().Length == 0` returned `null` early, even
   when an override zone such as Anchorage had observable Temporal transitions
   defined only by `TemporalHistoricalTimeZoneOffsets`.
5. The expansion phase doubled its step (1 day → 30 days → 180 days) and
   compared each scan point's offset to the start offset. For a London
   reference instant inside the 1968–1971 override window (offset `+01:00`
   via override), a 180-day backward step could land in summer 1967, where
   the native DST table also reports `+01:00`. The expansion saw "same
   offset" on both sides and snapped its `hi` boundary past the synthetic
   1968-02-18T02:00Z transition entirely, then converged on a 1967 native
   BST transition instead.

The result was that Test262
`Temporal.ZonedDateTime.prototype.getTimeZoneTransition`
`rule-change-without-offset-transition.js` returned `-88034400000000000n`
(1967-03-19 native BST transition) for `London 1968-02-18T02:00Z`, instead of
the expected `-59004000000000000n`.

## Decision

Keep the ZonedDateTime transition search synthetic-boundary aware and
nanosecond-precise:

1. **Nanosecond-precise search start.** Convert the receiver's epoch
   nanoseconds through a shared `ToTransitionSearchInstant` /
   `TryToDateTimeOffset` helper that floors to `DateTimeOffset` ticks
   without intermediate millisecond truncation, and explicitly handles the
   `DateTimeOffset` host range.
2. **Tick-precision bisection.** Binary search terminates when
   `hi.UtcTicks - lo.UtcTicks <= 1` and returns the `hi` instant directly;
   do not snap the result to a second boundary.
3. **Route offset reads through the historical override layer.** Pass
   `requestedTimeZoneId` into `FindTransitionBinarySearch` and read offsets
   via `TemporalHistoricalTimeZoneOffsets.GetUtcOffset(...)`, so override
   windows (Europe/London, America/Anchorage, Europe/Paris, Pacific/Niue,
   Africa/Monrovia) are visible to the search the same way they are visible
   to ZonedDateTime offset/wall-clock helpers.
4. **Do not gate on adjustment-rule count.** Only treat `TimeZoneInfo.Utc`
   as "no transitions"; other zones may have an empty native rule table and
   still expose observable transitions through the override layer.
5. **Detect synthetic-window boundaries during expansion.** Maintain a small
   per-zone list of synthetic override-window UTC boundaries via
   `TemporalHistoricalTimeZoneOffsets.GetSyntheticBoundaries(timeZoneId)`.
   On each backward expansion step where `scanOffset == startOffset`, check
   whether a known boundary lies in `(scanPoint, hi)`. If the tick just
   before that boundary reports a different offset, snap `lo`/`hi` to
   bracket the boundary so the binary search converges on the synthetic
   transition instead of a coincidentally same-offset native rule on the
   far side.
6. **Filter spurious same-offset candidates.** Use
   `IsObservableOffsetTransition` and a `StepPastTransition` helper inside
   the retry loop: when a candidate transition's neighbours share the same
   offset (a transition reported by `TimeZoneInfo` that the override layer
   smooths over), step past it and continue searching instead of returning
   a non-observable point.

## Consequences

- Pro: `Temporal.ZonedDateTime.prototype.getTimeZoneTransition` now reports
  the 1968-02-18T02:00Z London BST-start synthetic transition and the
  1945-09-30T11:00Z Anchorage standard-offset boundary, matching Test262
  expectations.
- Pro: Offset transition results agree with `OffsetNanoseconds`,
  `withPlainTime`, and the rest of the Temporal/Intl surface, all of which
  already routed through `TemporalHistoricalTimeZoneOffsets`.
- Pro: Sub-second instants no longer round to second boundaries in the
  reported transition.
- Con: The synthetic-boundary list is per-zone hardcoded data. New override
  windows must be added to both the offset switch and
  `GetSyntheticBoundaries(...)` for the expansion phase to see them; the
  list is intentionally small and zone-scoped to keep the binary search
  cheap.
- Con: The expansion loop now performs a bounded boundary check per step.
  This is `O(boundaries)` per loop iteration, and `boundaries` is a small
  constant per known override zone.

## Proof

- Narrow Test262 method group
  `Name=Temporal_ZonedDateTime_prototype_getTimeZoneTransition` passes
  40/40, including
  `rule-change-without-offset-transition.js` (strict and non-strict) for
  Europe/London 1968-02-18T02:00Z and America/Anchorage 1945-09-30T11:00Z.
- Implementation diff is bounded to
  `src/Asynkron.JsEngine/StdLib/Temporal/TemporalHelper.cs`
  (transition search helpers) and
  `src/Asynkron.JsEngine/StdLib/Temporal/TemporalHistoricalTimeZoneOffsets.cs`
  (synthetic-boundary list, Paris pre-cutover entry, BigInteger overload).
- Canonical local quality gate passed for issue #859 / PR #1200.

## Related

- Issue #859, PR #1200.
- ADR 0068 — Keep Temporal ZonedDateTime offsets and time-only arithmetic on
  epoch nanoseconds. This ADR extends the same nanosecond-precision rule
  into the transition search.
- ADR 0071 — Keep Temporal ZonedDateTime wall-clock projection DST-aware.
  Same family: ZonedDateTime surfaces must observe override windows through
  `TemporalHistoricalTimeZoneOffsets`, not raw `TimeZoneInfo`.
