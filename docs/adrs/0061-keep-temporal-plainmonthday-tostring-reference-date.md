# ADR 0061: Keep Temporal PlainMonthDay toString reference date

## Status

Accepted

## Context

Issue #847 / PR #1172 fixed the focused Test262
`Temporal_PlainMonthDay_prototype_toString` failures for
`intl402/Temporal/PlainMonthDay/prototype/toString/calendarname-never.js`.

`Temporal.PlainMonthDay.prototype.toString({ calendarName: "never" })` does
not mean "use the ISO short month-day form for every calendar." The option
removes the calendar annotation. For ISO receivers, that leaves the short
`MM-DD` representation. For non-ISO receivers, the object still needs its
reference ISO date to keep the month-day unambiguous without a `[u-ca=...]`
annotation, such as `1972-05-02` for a Gregorian receiver.

The previous implementation routed every `calendarName: "never"` receiver to
`ToStringBasic()`, which collapsed non-ISO PlainMonthDay values to `MM-DD` and
lost the reference date. The delivery added a reference-date formatter and kept
the branch split: ISO uses `MM-DD`, non-ISO uses `YYYY-MM-DD` without calendar
annotation.

The build repair in the same delivery also reaffirmed the nearby PlainDate
storage invariant: non-ISO `Temporal.PlainDate.from(...)` should keep the
canonical ISO backing date returned by calendar conversion, while exposing
calendar-visible fields through calendar helpers.

## Decision

Keep `Temporal.PlainMonthDay.prototype.toString` calendar annotation handling
separate from PlainMonthDay reference-date selection.

For future PlainMonthDay string-format work:

1. treat `calendarName: "never"` as annotation removal, not as a request to
   force the ISO `MM-DD` shape for every calendar;
2. preserve ISO receivers as `MM-DD`;
3. preserve non-ISO receivers as the non-annotated reference ISO date
   `YYYY-MM-DD`;
4. keep `auto`, `always`, and `critical` on their existing annotation branches
   unless the spec path being implemented proves otherwise; and
5. prove this class with the focused
   `Name=Temporal_PlainMonthDay_prototype_toString` Test262 method group plus
   internal coverage for ISO and non-ISO `calendarName: "never"` receivers.

## Consequences

- PlainMonthDay review needs to distinguish three concerns: calendar-visible
  month/day fields, reference ISO date slots, and calendar annotation policy.
- Locale formatting remains covered by ADR 0060, but ordinary `toString`
  formatting has its own reference-date invariant when annotations are removed.
- Future non-ISO PlainDate repair work should keep checking whether it is
  operating on visible calendar fields or internal ISO backing fields before
  storing results.
- This ADR is caused by issue #847 / PR #1172 and complements ADR 0057, ADR
  0059, ADR 0060, and the root
  `.claude/rules/ecmascript-abstract-operations.md` rule for Temporal
  calendar-domain behavior.
