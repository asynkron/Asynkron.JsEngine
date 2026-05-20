# ADR 0060: Keep Temporal PlainMonthDay locale formatting on reference slots

## Status

Accepted

## Context

Issue #846 / PR #1171 fixed the focused Test262
`Temporal_PlainMonthDay_prototype_toLocaleString` failures for Islamic
calendar date-style formatting.

`Temporal.PlainMonthDay` carries two related representations. Its observable
`Month`, `Day`, and `MonthCode` belong to the receiver's calendar. Its internal
reference ISO date slots provide a valid date that can be passed to the host
date formatter. The previous Intl path built `DateTimeOffset` values from the
calendar-visible month and day together with the reference year. That mixed
calendar-space fields with an ISO date constructor and produced the wrong
formatted date for non-ISO calendars.

The delivery also exposed a nearby invariant through the quality gate:
`Temporal.PlainDate.from` must keep ISO-backed internal storage for non-ISO
PlainDate values. Calendar-visible fields are exposed through calendar-part
getters, while storage and host validation remain ISO-projected.

## Decision

Keep `Temporal.PlainMonthDay.prototype.toLocaleString` and adjacent
`Intl.DateTimeFormat` Temporal formatting on the object's reference ISO slots
when constructing host date values.

For future PlainMonthDay locale-format work:

1. use `ReferenceYear`, `ReferenceMonth`, and `ReferenceDay` when building a
   `DateTimeOffset` or epoch value for a `PlainMonthDay`;
2. use the receiver's `Calendar` only to select calendar formatting semantics
   and validate calendar compatibility;
3. do not combine calendar-visible `Month` or `Day` with the reference year as
   if those fields were ISO date components;
4. lower `dateStyle` for PlainMonthDay to the month/day fields that are valid
   for that Temporal kind instead of leaking a year field through shared
   DateTimeFormat style expansion; and
5. keep PlainDate non-ISO storage ISO-backed, exposing calendar fields through
   calendar-part helpers rather than preserving source-calendar fields in the
   internal slots.

## Consequences

- PlainMonthDay review needs to distinguish calendar-visible fields from
  reference ISO slots before any host `DateTimeOffset` or epoch conversion.
- Intl Temporal formatting fixes should recheck all helper paths that construct
  formatter targets or epoch values, because `format`, `formatToParts`, and
  date-style helpers can each accidentally mix field domains.
- Future work should prove this class with the focused
  `Name=Temporal_PlainMonthDay_prototype_toLocaleString` Test262 method group,
  plus internal coverage for non-ISO PlainDate storage invariants when the fix
  touches shared Temporal calendar conversion.
- This ADR is caused by issue #846 / PR #1171 and complements ADR 0009, ADR
  0057, ADR 0059, and the root
  `.claude/rules/ecmascript-abstract-operations.md` rule for Intl Temporal
  effective-slot and calendar-domain behavior.
