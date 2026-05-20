# ADR 0059: Keep Temporal PlainMonthDay field order calendar-dependent

## Status

Accepted

## Context

Issue #844 / PR #1162 fixed the Test262
`BuiltInsTests.Temporal_PlainMonthDay_from` failures for
`built-ins/Temporal/PlainMonthDay/from/order-of-operations.js`.

`Temporal.PlainMonthDay.from` observes property-bag reads. The previous
implementation read `era` and `eraYear` immediately after `day`, even for ISO
property bags. That moved observable `era` getter work ahead of `month`,
`monthCode`, `year`, and options, while the Test262 fixture expects ISO bags to
read `calendar`, `day`, `month`, `monthCode`, `year`, then options. The same
helper later re-read `era` and `eraYear` for non-ISO validation, so the bug was
both an ISO ordering issue and duplicated non-ISO field access.

The repair made ISO `PlainMonthDay.from` skip `era` and `eraYear` during field
preparation. Non-ISO calendars still read those fields once and reuse the
observed presence for validation. Clone and string inputs stayed on the
existing path that reads only the options overflow sequence.

## Decision

Keep Temporal `PlainMonthDay.from` property-bag conversion as an observable
field-read sequence whose era reads are calendar-dependent.

For future `Temporal.PlainMonthDay.from` and adjacent Temporal property-bag
work:

1. do not read `era` or `eraYear` for ISO PlainMonthDay property bags;
2. preserve the ISO observable order as `calendar`, `day`, `month`,
   `monthCode`, `year`, then options;
3. for non-ISO calendars, read `era` and `eraYear` at most once and reuse their
   observed presence for validation instead of re-reading them later; and
4. prove this class with the focused `Name=Temporal_PlainMonthDay_from`
   Test262 method group before widening.

## Consequences

- Future Temporal field readers should avoid sharing one eager `era` field list
  across ISO and non-ISO calendar paths.
- Review should check property-bag field order, options read order, and clone
  or string input paths separately.
- This ADR is caused by issue #844 / PR #1162 and complements ADR 0046, ADR
  0048, ADR 0057, and the root
  `.claude/rules/ecmascript-abstract-operations.md` rule for observable
  Temporal property-bag conversion.
