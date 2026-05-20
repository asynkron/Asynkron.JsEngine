# ADR 0076: Keep Temporal ZonedDateTime rounding bound validation wall-clock preserving

## Status

Accepted.

## Context

`Temporal.ZonedDateTime.prototype.since` and `.until` use
`DifferenceTemporalZonedDateTime` for both ordinary difference calculation and
rounding-bound validation. Date-unit rounding with a non-1 rounding increment
needs a speculative rounded date to prove the result stays inside Temporal's
representable `PlainDateTime` range.

Before issue #864 / PR #1245, `ValidateZonedDateTimeDateRoundingBound`
validated that rounded ISO date at midnight. That kept one Test262
out-of-range fixture throwing, but it over-rejected valid negative-boundary
values whose receiver wall-clock time was just after midnight. At the lower
Temporal date boundary, `-271821-04-20T00:00:00.000000001` is representable as
a date-time while the same date at `00:00:00.000000000` is not.

The fix also needed to preserve the original out-of-range behavior. The
candidate date alone is not enough: the normalized time remainder from the
difference calculation can roll the validation target across a day boundary.

## Decision

Keep ZonedDateTime date-rounding bound validation on the full
receiver-relative date-time, not on a synthetic midnight date:

1. Build the validation time from the receiver wall-clock time plus the
   normalized time remainder produced by the difference calculation.
2. Normalize that nanosecond time-of-day with floor-style day carry so negative
   remainders borrow from the date and positive remainders advance it.
3. Validate the resulting `(date, time)` tuple through the shared
   `RejectISODateTimeRange` helper.
4. Do not replace this with a date-only or midnight check. Midnight is a
   separate instant at Temporal's lower boundary and can be invalid while the
   receiver's actual wall-clock time is valid.

## Consequences

- Pro: Valid negative-boundary ZonedDateTime `.until` calculations with
  day-rounding increments are no longer rejected just because midnight on the
  rounded date is out of range.
- Pro: The original Test262 rounding-increment out-of-range case still throws,
  because the normalized time remainder is applied before range validation.
- Pro: The helper now matches the semantic unit it validates: a rounded
  `PlainDateTime`, not only a rounded ISO date.
- Con: `ValidateZonedDateTimeDateRoundingBound` must carry a `BigInteger`
  normalized time remainder and split it into date/time components before
  calling the range helper.

## Proof

- Focused internal regression:
  `Temporal_ZonedDateTime_Until_DayRoundingIncrement_PreservesNegativeBoundaryTime`.
- Focused Test262 fixture:
  `Temporal_ZonedDateTime_prototype_until` with
  `roundingincrement-addition-out-of-range`.
- Focused method groups from the delivery:
  `Name=Temporal_ZonedDateTime_prototype_until` and
  `FullyQualifiedName~Temporal_ZonedDateTime`.
- `git diff --check HEAD~1..HEAD` passed for delivery commit `62629fd7`.

## Related

- Issue #864, PR #1245.
- ADR 0070 - Keep Temporal ZonedDateTime since/until spec step order explicit.
- ADR 0068 - Keep Temporal ZonedDateTime offsets and time-only arithmetic on
  epoch nanoseconds.
- Root rule: `.claude/rules/ecmascript-abstract-operations.md`.
