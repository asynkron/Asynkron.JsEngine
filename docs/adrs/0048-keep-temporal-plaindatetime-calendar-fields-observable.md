# ADR 0048: Keep Temporal PlainDateTime calendar fields observable

## Status

Accepted

## Context

Issue #837 / PR #1137 fixed the Test262
`BuiltInsTests.Temporal_PlainDateTime_from` failures for non-ISO calendar
property bags, including the Intl402
`Temporal/PlainDateTime/from/calendar-not-supporting-eras.js` fixture.

`Temporal.PlainDateTime.from` accepts property-bag date fields in the resolved
calendar. For supported non-ISO calendars, the previous implementation converted
the calendar date to an ISO date and then stored that converted ISO year, month,
and day in the resulting `PlainDateTime`. That made range validation use a date
shape the engine could validate, but it also changed the calendar-visible slots.
The Hebrew fixture demonstrated the bug: `year: 5780` must remain observable as
`5780` on the resulting `PlainDateTime`, even though the corresponding ISO date
is in 2019.

The fix split the two roles. Non-ISO date fields are normalized or constrained
in the source calendar, converted to an ISO date only for range validation, and
then stored back as the calendar-visible year, month, and day on the
`PlainDateTime`. For calendars that do not use eras, `era` and `eraYear` remain
ignored when an explicit `year` is present; they must not replace the calendar
year or satisfy a missing required year.

## Decision

Keep Temporal `PlainDateTime.from` property-bag conversion as a calendar-field
operation with a separate ISO validation projection.

For future Temporal `PlainDateTime.from` and adjacent non-ISO property-bag work:

1. preserve the resolved calendar's visible `year`, `month`, and `day` fields
   on the Temporal object after overflow handling;
2. use calendar-to-ISO conversion only for ISO range checks or other operations
   that explicitly need the ISO projection;
3. do not overwrite visible non-ISO fields with the converted ISO fields merely
   because the validation helper returns an ISO date;
4. keep `era` and `eraYear` handling calendar-dependent: calendars without era
   support must ignore them when an explicit `year` is present and must still
   require `year` when it is absent; and
5. prove this class with the focused `Name=Temporal_PlainDateTime_from`
   Test262 method group and a local Temporal PlainDateTime pack when touched.

## Consequences

- Future Temporal property-bag code should name helpers by role: source
  calendar fields, overflow-normalized calendar fields, and ISO validation
  projection are not interchangeable.
- Review should check observable object slots separately from the intermediate
  ISO date used for validation.
- This ADR is caused by issue #837 / PR #1137 and complements ADR 0046 plus the
  root `.claude/rules/ecmascript-abstract-operations.md` rule for observable
  Temporal property-bag conversion.
