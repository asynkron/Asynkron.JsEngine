# ADR 0057: Keep Temporal PlainDate calendar fields observable

## Status

Accepted

## Context

Issue #840 / PR #1160 fixed `Temporal.PlainDate.from` for non-ISO property
bags whose calendar does not use eras, including the focused
`BuiltInsTests.Temporal_PlainDate_from` proof group.

`Temporal.PlainDate.from` reads property-bag fields in observable spec order.
The previous implementation treated every non-ISO calendar as if `era` and
`eraYear` were relevant, so a Hebrew property bag could observe throwing
`era`/`eraYear` getters even when an explicit `year` was present. Neighboring
Temporal readers already used the resolved calendar's era capability to decide
whether those fields should be read.

The repair also preserved the calendar-visible `year`, `monthCode`, and `day`
after BCL calendar validation. The engine can still use a calendar-to-ISO
projection to prove the date is representable, but storing that projection back
into `JsTemporalPlainDate` would expose ISO fields where the property bag
requires source-calendar fields.

## Decision

Keep Temporal `PlainDate.from` property-bag conversion as a resolved-calendar
field operation with a separate ISO validation projection.

For future `Temporal.PlainDate.from` and adjacent non-ISO property-bag work:

1. resolve the calendar before deciding whether `era` and `eraYear` are
   observable fields;
2. read and coerce `era` and `eraYear` only for era-capable calendars, while
   still requiring `year` for calendars without era support;
3. use calendar-to-ISO conversion for validation and range checks, not as the
   stored visible field representation; and
4. prove this class with the focused `Name=Temporal_PlainDate_from` Test262
   method group plus local coverage for throwing era getters on a non-era
   calendar.

## Consequences

- `CalendarUsesEras` is part of property-bag field preparation, not only a later
  validation helper.
- Review should check observable getter reads separately from calendar-date
  validation.
- This ADR is caused by issue #840 / PR #1160 and complements ADR 0048 plus the
  root `.claude/rules/ecmascript-abstract-operations.md` rule for observable
  Temporal property-bag conversion.
