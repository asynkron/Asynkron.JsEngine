# ADR 0065: Keep Temporal PlainMonthDay reference normalization shared

## Status

Accepted

## Context

Issue #845 / PR #1167 fixed the focused Test262
`Temporal_PlainMonthDay_prototype_equals` failures for
`intl402/Temporal/PlainMonthDay/prototype/equals/canonicalize-calendar.js`.

`Temporal.PlainMonthDay` equality is sensitive to more than calendar identifier
spelling. A non-ISO PlainMonthDay carries calendar-visible month/day fields plus
reference ISO slots used to keep the month-day unambiguous. The failing path
accepted canonical calendar aliases, but constructor and string conversion
created non-ISO receivers by converting an ISO reference date directly into
calendar fields and storing those intermediate values. That left equivalent
non-ISO month-days with different reference-slot shapes, so
`PlainMonthDay.prototype.equals` could still fail after calendar
canonicalization.

The repair introduced a shared ISO-reference creation helper for constructor and
string conversion paths. It converts the ISO reference date into calendar
month/day/monthCode and then normalizes through the existing non-ISO
month-day helper. The same delivery added fixed-calendar ISO-date conversion
coverage for Coptic, Ethiopic/Ethioaa, and Indian calendars, and BCL-backed
handling for accepted Islamic aliases.

## Decision

Keep non-ISO `Temporal.PlainMonthDay` construction from ISO reference dates on a
single shared normalization path before equality observes the object.

For future PlainMonthDay equality or conversion work:

1. use the shared ISO-reference helper for constructor and string paths instead
   of manually storing converted calendar fields;
2. normalize through the existing non-ISO month-day helper so reference ISO
   slots, `MonthCode`, and calendar-visible month/day stay mutually consistent;
3. treat calendar alias canonicalization as necessary but insufficient for
   equality when reference slots can differ;
4. add explicit conversion coverage before accepting non-BCL calendar IDs in
   constructor/string paths; and
5. prove this class with the focused
   `Name=Temporal_PlainMonthDay_prototype_equals` Test262 method group plus
   local coverage for accepted non-ISO constructor calendars and equal
   month-days created from different ISO reference dates.

## Consequences

- PlainMonthDay review needs to check constructor, string conversion, and
  property-bag conversion separately, because they enter the reference-slot
  model from different field domains.
- Calendar aliases and reference-date normalization must be reasoned together:
  equality can be wrong even when `CanonicalizeCalendarIdForComparison` is
  correct.
- Future calendar support additions should include ISO-date-to-calendar
  conversion coverage before exposing the calendar through PlainMonthDay
  construction.
- This ADR is caused by issue #845 / PR #1167 and complements ADR 0059, ADR
  0060, ADR 0061, ADR 0062, and the root
  `.claude/rules/ecmascript-abstract-operations.md` rule for Temporal
  calendar-domain behavior.
