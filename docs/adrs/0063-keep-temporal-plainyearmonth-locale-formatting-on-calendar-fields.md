# ADR 0063: Keep Temporal PlainYearMonth locale formatting on calendar fields

## Status

Accepted

## Context

Issue #852 / PR #1180 fixed the focused Test262
`Temporal_PlainYearMonth_prototype_toLocaleString` failures for date-style
formatting.

`Temporal.PlainYearMonth` carries calendar-visible year/month fields plus a
reference day. The reference day exists to make a complete internal date where
one is needed, but it is not an observable formatting field for
`PlainYearMonth`. The previous `Intl.DateTimeFormat` Temporal path reused the
generic `DateTimeOffset` component formatter. That flattened year/month output
through Gregorian host date components and let style expansion behave as if a
full date were being formatted.

The visible failure was non-ISO calendar output: Islamic `dateStyle: "long"`
for `islamic-tbla` did not reach the calendar month name `Ramadan`. The
delivery added a `PlainYearMonth`-specific formatter path that formats only the
effective year/month fields, derives non-ISO year/month display through the
matching BCL-backed calendar helper, and keeps the reference day out of
date-style output.

## Decision

Keep `Temporal.PlainYearMonth.prototype.toLocaleString` and adjacent
`Intl.DateTimeFormat` Temporal formatting on a PlainYearMonth-specific
component path.

For future PlainYearMonth locale-format work:

1. lower `dateStyle` to the fields valid for `PlainYearMonth`, namely year and
   month, instead of reusing a full-date style shape that can leak day output;
2. format non-ISO year/month values in the receiver calendar's domain, not by
   asking a Gregorian `DateTimeOffset` for month names;
3. treat the reference day as an internal completion field, not as an output
   field for `PlainYearMonth` formatting;
4. keep the fallback/default path equivalent to omitted options when
   `dateStyle` is explicitly `undefined`; and
5. prove this class with the focused
   `Name=Temporal_PlainYearMonth_prototype_toLocaleString` Test262 method group
   plus local coverage for Gregorian and non-ISO month names.

## Consequences

- PlainYearMonth review needs to distinguish calendar-visible year/month
  fields from the reference day before any host date formatting path is reused.
- Intl Temporal formatting fixes should check whether style expansion is valid
  for the Temporal kind being formatted, not only whether a complete host date
  can be constructed.
- This ADR is caused by issue #852 / PR #1180 and complements ADR 0009, ADR
  0057, ADR 0060, ADR 0061, and the root
  `.claude/rules/ecmascript-abstract-operations.md` rule for Intl Temporal
  effective-slot and calendar-domain behavior.
