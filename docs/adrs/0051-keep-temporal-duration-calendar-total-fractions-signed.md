# ADR 0051: Keep Temporal Duration calendar total fractions signed

## Status

Accepted

## Context

Issue #835 / PR #1133 fixed the Test262
`Temporal_Duration_prototype_total` failure group for
`Temporal.Duration.prototype.total`. The main delivery repaired
`relativeTo` conversion order, representable range checks, DST-sensitive
ZonedDateTime arithmetic, and historical sub-minute offsets.

Review then found a narrower build-back bug in the new ZonedDateTime
calendar-unit total path. Negative partial totals for calendar units were using
the signed distance between the current whole-unit boundary and the previous
boundary as the fractional denominator. That made both the numerator and
denominator negative for cases such as
`new Temporal.Duration(0, 0, 0, -1).total({ unit: "week", relativeTo })`,
turning the expected negative fraction into a positive one.

The repair kept the signed remainder (`endEpochNs - thresholdNs`) but made the
calendar-unit denominator the positive span between adjacent boundaries for
weeks, months, and years. This preserves variable calendar/ZonedDateTime spans
while keeping the sign owned by the remainder.

## Decision

Keep `Temporal.Duration.prototype.total` calendar-unit fractions as signed
remainders divided by positive adjacent-boundary spans.

For future ZonedDateTime `total(..., { unit })` work:

1. compute the whole calendar-unit threshold relative to `relativeTo`;
2. compute the next boundary in the direction of the duration sign;
3. use the absolute span between those two boundaries as the fractional
   denominator;
4. keep the numerator signed as the difference between the actual end instant
   and the whole-unit threshold; and
5. prove negative partial totals for `week`, `month`, and `year` in the local
   suite before relying only on the focused
   `Name=Temporal_Duration_prototype_total` Test262 method group.

## Consequences

- Future calendar-unit total work should not infer the final sign from the
  denominator. Calendar and time-zone boundaries can move backward relative to
  the duration direction, but the unit span is still a positive measurement.
- Review should include both positive and negative partial totals whenever the
  code divides by a variable calendar or ZonedDateTime span.
- This ADR is caused by issue #835 / PR #1133 and complements ADR 0046 for
  `relativeTo` conversion order plus
  `.claude/rules/ecmascript-numeric-coercions.md` for exact duration math.
