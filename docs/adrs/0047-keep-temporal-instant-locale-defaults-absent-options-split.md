# ADR 0047: Keep Temporal Instant locale defaults absent-options split

## Status

Accepted

## Context

Issue #836 fixed the Test262
`Temporal_Instant_prototype_toLocaleString` failures for undefined locales,
undefined options, undefined `dateStyle`/`timeStyle`, and lone option cases.

The failing behavior came from treating all `Temporal.Instant.prototype.toLocaleString`
calls as if they should receive injected date, time, and time-zone-name
components before constructing `Intl.DateTimeFormat`. That drifted from the
observable ECMA-402 contract in two directions:

1. absent or undefined options should keep the `Intl.DateTimeFormat` default
   path so `instant.toLocaleString(...)` matches
   `new Intl.DateTimeFormat(...).format(instant)`; and
2. defined non-format options, such as `timeZone`, still need Temporal
   date/time defaults so the constructor has concrete fields to format.

The delivery in PR #1134 kept the fix in the Intl/Temporal formatting boundary:
`TemporalHelper.BuildTemporalDateTimeFormatOptions` now distinguishes absent
options from defined non-format options, and
`IntlDateTimeFormatPrototype.GetDefaultComponentsForTemporal` no longer gives
`Temporal.Instant` a default `timeZoneName`. `Temporal.ZonedDateTime` keeps its
separate time-zone-name default path.

## Decision

For `Temporal.Instant.prototype.toLocaleString`, preserve a deliberate split
between absent options and defined option bags.

Specifically:

1. when locales/options or `dateStyle`/`timeStyle` are absent or `undefined`,
   let `Intl.DateTimeFormat` compute its own defaults and then format the
   Instant through the Temporal-aware DateTimeFormat path;
2. when a defined non-format option such as `timeZone` is present, inject the
   date and time component defaults needed for Instant formatting;
3. do not inject a default `timeZoneName` for Instant defaults;
4. when `timeZoneName` is the lone explicit formatting option for Instant, keep
   date/time defaults plus the requested zone name so behavior matches the
   adjacent Date toLocaleString expectation; and
5. keep `Temporal.ZonedDateTime` time-zone defaults separate from
   `Temporal.Instant`.

## Consequences

- Future Temporal/Intl fixes must treat default injection as observable
  ECMA-402 option semantics, not as a generic Temporal convenience.
- Instant and ZonedDateTime should not share one default-component branch just
  because both can format date and time fields.
- Focused proof for this class should include the exact
  `Name=Temporal_Instant_prototype_toLocaleString` Test262 method group and
  should cover undefined options, defined non-format options, and lone
  `timeZoneName`.
- This ADR is caused by issue #836 / PR #1134 and complements the root
  `.claude/rules/ecmascript-abstract-operations.md` rule.
