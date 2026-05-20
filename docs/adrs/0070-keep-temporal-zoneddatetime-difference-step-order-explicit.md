# 0070. Keep Temporal ZonedDateTime since/until spec step order explicit

## Status

Accepted.

## Context

`DifferenceTemporalZonedDateTime` is the shared abstract operation behind
`Temporal.ZonedDateTime.prototype.since` and `.until`. The ECMAScript Temporal
spec orders its checks so that:

1. Argument coercion and the `DifferenceSettings` resolution happen first.
2. If `LargestUnit` is hour or smaller (a "time-only" largest unit), the
   operation returns the difference computed from epoch nanoseconds directly,
   *without* requiring the two operands to share a time zone.
3. Only for calendar-unit largest units (day or larger) does the spec require
   that both operands resolve to the same time zone; otherwise it throws
   `RangeError`.

Prior to issue #861 / PR #1198, the implementation reversed two parts of this
contract:

- A `TimeZoneEquals` rejection ran ahead of the hour-largest fast path. That
  made cross-zone hour-largest `since`/`until` (e.g. comparing an Instant in
  `America/New_York` to an Instant in `Europe/Paris` with
  `{ largestUnit: 'hour' }`) throw `RangeError` even though the spec returns a
  valid epoch-nanosecond difference.
- A `!FixedOffset.HasValue` bypass skipped the time-zone equality check
  whenever either operand was a fixed-offset zone. That made non-equivalent
  fixed-offset pairs such as `+01:00` vs `+02:00`, and fixed-offset vs named
  pairs, silently accept calendar-unit differences that the spec requires to
  throw.

Both bugs came from treating "time zone equality" as a single coarse gate
instead of as a step inside `DifferenceTemporalZonedDateTime` with explicit
neighbours.

## Decision

Keep the spec step order between the time-only fast path and the time-zone
equality check explicit in the implementation:

1. **Hour-largest returns first.** When `UnitRank(settings.LargestUnit)` is
   `Hour` or smaller, return immediately from the epoch-nanosecond difference
   path. Do not check time-zone equality before this branch.
2. **Calendar-unit differences canonicalize then compare.** For day-or-larger
   largest units, compare the operands' canonical time-zone identifiers via
   `CanonicalizeTimeZoneIdForComparison(...)`. This single helper normalizes
   fixed-offset spellings (`+01`, `+0100`, `+01:00` all canonicalize the same
   way, and `+00:00`/`UTC` collapse), and named IANA zones canonicalize to
   their resolved primary name. A single ordinal string comparison covers
   fixed-offset, named, and mixed pairs uniformly.
3. **No FixedOffset bypass.** Do not branch on `FixedOffset.HasValue`. The
   canonical-identifier comparison is the single source of truth for whether
   two ZonedDateTime operands share a time zone for calendar-unit
   `since`/`until`.
4. **Throw site stays on the date-unit path.** The `RangeError` for a
   time-zone mismatch is emitted on the path that actually requires a shared
   zone; it must not regress to a pre-fast-path gate or move into the
   hour-largest branch.

## Consequences

- Pro: Cross-zone `since`/`until` with `largestUnit: 'hour'` (or smaller) now
  computes the spec-correct epoch-nanosecond difference instead of throwing.
- Pro: Non-equivalent fixed-offset pairs (`+01:00` vs `+02:00`) and mixed
  fixed-offset/named pairs correctly throw `RangeError` for calendar-unit
  differences.
- Pro: Equivalent fixed-offset spellings (`+01:00` vs `+0100`) still succeed
  for calendar-unit differences because canonicalization treats them as the
  same zone.
- Con: The implementation has two distinct branches whose order is observable
  via Test262. The classifier (the `UnitRank(LargestUnit) <= Hour` check) must
  remain correct: misclassifying a calendar-unit operation as time-only would
  skip the required mismatch throw.

## Proof

- Six focused regressions added in `tests/Asynkron.JsEngine.Tests/TemporalTests.cs`:
  - `Temporal_ZonedDateTime_Until_HourLargestUnit_AcrossDifferentNamedZones`
  - `Temporal_ZonedDateTime_Since_HourLargestUnit_AcrossFixedAndNamedZones`
  - `Temporal_ZonedDateTime_Until_DayLargestUnit_ThrowsOnNamedTimeZoneMismatch`
  - `Temporal_ZonedDateTime_Until_DayLargestUnit_ThrowsOnFixedOffsetMismatch`
  - `Temporal_ZonedDateTime_Since_DayLargestUnit_ThrowsOnFixedOffsetVsNamedMismatch`
  - `Temporal_ZonedDateTime_Until_DayLargestUnit_AllowsEquivalentFixedOffsets`
- Canonical local quality gate (build + internal tests) passed for issue
  #861 / PR #1198.
- Implementation diff is bounded to
  `src/Asynkron.JsEngine/StdLib/Temporal/TemporalHelper.cs`
  (`DifferenceTemporalZonedDateTime` step order and the
  `ValidateZonedDateTimeDateRoundingBound` / `DateDurationTargetsInvalidLocalTime`
  helpers).

## Related

- Issue #861, PR #1198.
- ADR 0068 — Keep Temporal ZonedDateTime offsets and time-only arithmetic on
  epoch nanoseconds. Same general lesson family: time-only ZonedDateTime
  semantics stay in the exact-instant domain; calendar-unit semantics use the
  local/identifier domain.
- ADR 0046 — Temporal property-bag observability (Temporal abstract operation
  ordering rule family).
