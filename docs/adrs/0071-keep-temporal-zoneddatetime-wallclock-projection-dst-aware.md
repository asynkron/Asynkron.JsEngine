# 0071. Keep Temporal ZonedDateTime wall-clock projection DST-aware and host-bridge overflow-safe

## Status

Accepted.

## Context

`Temporal.ZonedDateTime` projects an exact epoch-nanosecond instant onto a
wall-clock local date/time through several surfaces, including
`GetLocalDate`, `GetLocalDateTime`, `startOfDay`, `year`, `month`, `day`, and
the cached `LocalDateTime`. Two distinct bugs combined to break the Test262
fixture
`Temporal_ZonedDateTime_prototype_startOfDay/same-date-starts-twice.js`
for `America/St_Johns` near the November DST fall-back.

1. The local-date/local-datetime helpers were combining
   `Instant.EpochNanoseconds` with `zdt.TimeZone.BaseUtcOffset`, which is the
   zone's **standard** offset. For an instant during DST (e.g.
   `2010-11-06T00:00:00-02:30` in St_Johns, where standard offset is `-03:30`),
   the wrong offset shifted the local wall clock one full day earlier.
2. `GetPossibleUtcOffsets` returned a single offset for IANA ambiguous local
   times — the offset `TimeZoneInfo.GetUtcOffset` chose for that wall clock.
   At an ambiguous fall-back midnight (`-02:30` and `-03:30` both valid),
   parsing a ZonedDateTime string with an explicit `-02:30` rejected the value
   with an offset-mismatch `RangeError` because the helper had already chosen
   the other side.

A follow-up review then exposed a third, latent bridge bug. ADR 0068 already
moves offset queries onto an epoch-nanosecond floor-tick conversion and notes
that "`Floor-tick conversion can throw OverflowException if epoch nanoseconds
are outside the long-tick range`." That observation was correct, but the
guard was incomplete: `JsTemporalInstant.ToDateTimeOffset()` casts
`EpochNanoseconds / 100` from `BigInteger` to `long`, and the .NET
`DateTimeOffset(long ticks, …)` constructor itself throws
`ArgumentOutOfRangeException` for out-of-range ticks. Past
`long.MaxValue / TicksPerSecond` (≈ year 33,658), the cast throws
`OverflowException` before any .NET range check runs. Temporal's representable
instant maximum is ~year 275,760, so valid instants well within Temporal range
could throw `OverflowException` and bypass `GetIanaOffset`'s
`ArgumentOutOfRangeException` fallback.

## Decision

Keep `Temporal.ZonedDateTime` wall-clock projection on the DST-aware offset
and treat every `BigInteger → DateTimeOffset` bridge as overflow-safe:

1. **Wall-clock projection uses the DST-aware offset.** `GetLocalDate` and
   `GetLocalDateTime` combine `EpochNanoseconds` with the offset returned by a
   shared `GetIanaOffset(zdt)` helper, not `zdt.TimeZone.BaseUtcOffset`. The
   helper goes through `TemporalHistoricalTimeZoneOffsets.GetUtcOffset` so
   historical/synthetic zone overrides see the same call path as DateTime
   formatting.
2. **`BaseUtcOffset` is the explicit out-of-host-range fallback only.**
   `GetIanaOffset` catches both `ArgumentOutOfRangeException` and
   `OverflowException` and returns `BaseUtcOffset` only for instants outside
   the .NET `DateTimeOffset` window (years 1–9999 by .NET range, and now also
   the long-tick overflow window). Valid in-range instants always see the
   DST-aware offset.
3. **Possible-UTC-offsets returns both ambiguous offsets.**
   `TemporalHistoricalTimeZoneOffsets.GetPossibleUtcOffsets` checks
   `TimeZoneInfo.IsAmbiguousTime` before the single-offset fallback and
   returns `GetAmbiguousTimeOffsets(localDateTime)` so the explicit-offset
   match accepts either side of an ambiguous local time.
4. **Catch the union of host bridge exceptions.** Any helper that bridges a
   Temporal `BigInteger` epoch through `Instant.ToDateTimeOffset()` (or any
   equivalent `long`-tick cast) catches both
   `ArgumentOutOfRangeException` and `OverflowException`. .NET's range check
   does not subsume the cast's overflow.
5. **Local field extraction stays on epoch arithmetic.** Prototype calendar
   fields such as `year`, `month`, `day`, `monthCode`, `era`, and `eraYear`,
   plus shared helpers such as `GetLocalPlainDateTime`, derive the local
   components from epoch nanoseconds plus the selected offset with
   `BigInteger` date decomposition. They must not require a successful
   `DateTimeOffset` conversion for named time zones outside .NET's supported
   year range.

## Consequences

- Pro: ZonedDateTime wall-clock outputs (date/time getters, `startOfDay`,
  cached `LocalDateTime`) are correct across DST transitions for named IANA
  zones, including `America/St_Johns` half-hour DST.
- Pro: ZonedDateTime parsing accepts both ambiguous offsets at IANA fall-back
  transitions instead of rejecting one with a misleading offset-mismatch error.
- Pro: Extreme valid Temporal instants (years > ~33,658, up to ~275,760) no
  longer crash named-timezone wall-clock projection with a host
  `OverflowException`; the `BaseUtcOffset` approximation is acceptable far
  outside the IANA DST data window.
- Pro: Extended-year named-timezone local fields remain available even when the
  offset lookup must fall back to `BaseUtcOffset`; issue #1375 / PR #1387 fixed
  a regression where `GetLocalPlainDateTime` still depended on
  DateTimeOffset-only conversion after the offset bridge had been made
  overflow-safe.
- Con: `GetIanaOffset` adds one host call per local projection. Fixed-offset
  ZonedDateTimes still skip the helper through the existing `FixedOffset.HasValue`
  branch.
- Con: Two distinct host bridge exceptions must stay paired at every Temporal
  `Instant → DateTimeOffset` site. Reviewers must keep both in the catch list.

## Proof

- Focused internal regressions:
  - `Temporal_ZonedDateTime_StartOfDay_AmbiguousMidnight` — `America/St_Johns`
    DST fall-back midnight returns the in-range start instant and exposes
    `-02:30` through the wall clock.
  - `Temporal_ZonedDateTime_OutOfDotNetRange_NamedTimezone_DoesNotThrow` —
    constructs a ZonedDateTime with `1000000000000000000000n` epoch ns
    (~10²¹, within Temporal range) in `America/New_York` and asserts `.year`
    returns a number without throwing.
- Issue #1375 / PR #1387 re-proved
  `Temporal_ZonedDateTime_OutOfDotNetRange_NamedTimezone_DoesNotThrow` after
  changing local field extraction to use `EpochNanosToComponents`, then reran
  the focused Temporal Test262 cases removed from the regression filters.
- Test262 filter removal: `built-ins/Temporal/ZonedDateTime/prototype/startOfDay/same-date-starts-twice.js`
  removed from `tests/Asynkron.JsEngine.Tests.Test262/current-regressions.filter.txt`.
- Implementation diff is bounded to:
  - `src/Asynkron.JsEngine/StdLib/Temporal/TemporalHelper.cs`
    (`GetIanaOffset`, `GetLocalDate`, `GetLocalDateTime`)
  - `src/Asynkron.JsEngine/StdLib/Temporal/TemporalHistoricalTimeZoneOffsets.cs`
    (`GetPossibleUtcOffsets` ambiguous branch)

## Related

- Issue #862, PR #1203.
- ADR 0068 — keeps `ZonedDateTime` offset and time-only arithmetic on epoch
  nanoseconds. This ADR extends 0068 by pairing every host bridge with both
  exception types and by requiring DST-aware projection on the local-date/time
  surfaces 0068 did not cover.
- ADR 0070 — keeps ZonedDateTime difference step order explicit (sibling
  ZonedDateTime correctness rule).
