# ADR 0049: Keep Temporal PlainDateTime locale hours on shared hour-cycle formatting

## Status

Accepted

## Context

Issue #838 / PR #1146 fixed the Test262
`Temporal_PlainDateTime_prototype_toLocaleString` failures for
`intl402/Temporal/PlainDateTime/prototype/toLocaleString/resolved-time-zone.js`.

The fixture formats `Temporal.PlainDateTime` values with locale `en`, resolved
time zone `Pacific/Apia`, explicit numeric date/time fields, and
`hourCycle: "h23"`. PlainDateTime is a wall-clock value, so the supplied time
zone must remain visible to `Intl.DateTimeFormat` resolution without converting
the date/time as if it were an instant. The implementation already preserved
that wall-clock behavior, but the Temporal component-formatting path formatted
numeric `h23` midnight as `0` while the shared DateTimeFormat hour path expects
`00`.

The delivery kept the fix in `IntlDateTimeFormatPrototype` by zero-padding
numeric `h23` and `h24` output through the shared `FormatHour` helpers. The
Temporal PlainDateTime path stayed on component formatting and did not start
converting through the supplied time zone.

## Decision

For `Temporal.PlainDateTime.prototype.toLocaleString`, keep wall-clock
PlainDateTime component formatting separate from instant/time-zone conversion,
but keep component output aligned with the shared `Intl.DateTimeFormat`
hour-cycle helpers.

Specifically:

1. do not convert PlainDateTime through the resolved `timeZone` option as an
   instant;
2. still preserve the resolved time-zone option for `Intl.DateTimeFormat`
   option semantics and `resolvedOptions()` behavior;
3. route Temporal component hours through the same hour-cycle formatting policy
   as epoch-based and proleptic DateTimeFormat paths;
4. zero-pad numeric `h23` and `h24` hours where the shared formatter requires
   it, instead of adding a Temporal-only output patch; and
5. prove this class with a focused internal Temporal DateTimeFormat regression
   plus the exact
   `Name=Temporal_PlainDateTime_prototype_toLocaleString` Test262 method group
   or narrower failing fixture filter.

## Consequences

- Future Temporal/Intl locale-formatting fixes should separate "resolved time
  zone is observable" from "PlainDateTime should be converted by that time
  zone".
- Component-formatting helpers must not silently drift between Temporal,
  epoch-based, and proleptic DateTimeFormat paths.
- Hour-cycle output changes can affect broad Intl formatting, so fixes should
  land in the shared hour helper and be proven with focused Test262 coverage
  before widening.
- This ADR is caused by issue #838 / PR #1146 and complements ADR 0008, ADR
  0047, ADR 0048, and the root
  `.claude/rules/ecmascript-abstract-operations.md` rule.
