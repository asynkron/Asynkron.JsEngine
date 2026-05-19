# ADR 0008: Keep Intl DateTimeFormat proleptic Gregorian host-range safe

## Status

Accepted

## Context

Issue #766 fixed the Test262 `DateTimeFormat_prototype_format` failures for
`intl402/DateTimeFormat/prototype/format/proleptic-gregorian-calendar.js`.
The failing cases formatted dates outside the .NET `DateTimeOffset` year range.
The previous `Intl.DateTimeFormat` path converted epoch milliseconds into
`DateTimeOffset` and clamped values to the host-supported range, so a
proleptic Gregorian BC date formatted as the .NET boundary year instead of the
ECMAScript calendar year.

The delivery in PR #942 kept ordinary in-range formatting on the existing
`DateTimeOffset` path, but added a proleptic Gregorian representation derived
from ECMAScript date math for out-of-range component formatting. The repair
also extended the same representation through `formatToParts`, `formatRange`,
and `formatRangeToParts`, because those surfaces otherwise reintroduced the
same clamping through helper-specific DTO conversions.

## Decision

Do not use `DateTimeOffset` as the source of truth for `Intl.DateTimeFormat`
component formatting when the epoch is outside the host date range and the
resolved calendar is Gregorian.

For out-of-range Gregorian component formatting:

1. derive year, month, day, weekday, and time fields from ECMAScript date math;
2. keep the existing `DateTimeOffset` path for ordinary in-range dates and for
   style-based formatting until a broader style-safe implementation is proven;
3. route string formatting and parts/range formatting through the same
   proleptic representation so helper surfaces do not drift;
4. preserve locale ordering, numbering-system digit translation, and explicit
   time-zone offset handling at the formatter boundary; and
5. prove changes with the focused `Name=DateTimeFormat_prototype_format`
   Test262 method group plus local regressions for parts and range helpers.

## Consequences

- Future Intl date work must treat host date/time types as implementation
  helpers with limited range, not as ECMAScript calendar semantics.
- New DateTimeFormat helpers that accept epoch milliseconds should either reuse
  the proleptic-safe path or explicitly prove that the helper cannot observe
  out-of-range Gregorian dates.
- Broadening proleptic support to style-based formatting, non-Gregorian
  calendars, or IANA time-zone historical rules needs separate proof because
  host globalization APIs may encode behavior outside ECMAScript's date math.
- This ADR is caused by issue #766 / PR #942 and complements the root
  `.claude/rules/ecmascript-abstract-operations.md` rule for future Intl
  implementation work.
