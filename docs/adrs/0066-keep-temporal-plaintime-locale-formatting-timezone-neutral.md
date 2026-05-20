# ADR 0066: Keep Temporal PlainTime locale formatting timezone-neutral

## Status

Accepted

## Context

Issue #849 / PR #1174 fixed the focused Test262
`Temporal_PlainTime_prototype_toLocaleString` failures for
`intl402/Temporal/PlainTime/prototype/toLocaleString/resolved-time-zone.js`.

The fixture formats `Temporal.PlainTime` values with locale `en`, resolved time
zone `Pacific/Apia`, explicit numeric time fields, and `hourCycle: "h23"`.
PlainTime is a wall-clock time without a date or instant, so the supplied time
zone must remain available for `Intl.DateTimeFormat` option resolution without
shifting the hour through that zone. The visible failure was that the formatting
path could treat a PlainTime as if it had an instant-backed synthetic date/time
and let the resolved time zone affect the displayed time.

The delivery kept the repair narrow: it added local PlainTime regression
coverage that proves the wall-clock hour is stable for offset-sensitive times
and removed the exact Test262 regression filter entries after the focused
method group passed.

## Decision

Keep `Temporal.PlainTime.prototype.toLocaleString` and adjacent
`Intl.DateTimeFormat` Temporal formatting timezone-neutral for output
components.

For future PlainTime locale-format work:

1. preserve the resolved `timeZone` option for `Intl.DateTimeFormat` option
   semantics and `resolvedOptions()` behavior;
2. do not convert PlainTime through the resolved time zone as an instant or
   date-bearing value;
3. keep output on PlainTime's own hour/minute/second/subsecond component
   domain;
4. route hour output through the shared `Intl.DateTimeFormat` hour-cycle
   formatting helpers instead of adding PlainTime-only string patches; and
5. prove this class with a focused internal regression plus the exact
   `Name=Temporal_PlainTime_prototype_toLocaleString` Test262 method group or
   narrower failing fixture filter.

## Consequences

- Future Temporal/Intl locale-formatting fixes should separate "resolved time
  zone is observable" from "PlainTime should be converted by that time zone".
- PlainTime, PlainDateTime, and Instant locale-formatting share the same
  DateTimeFormat boundary but have different time-zone semantics; do not
  collapse them into one host conversion path.
- This ADR is caused by issue #849 / PR #1174 and complements ADR 0047, ADR
  0049, ADR 0063, and the root
  `.claude/rules/ecmascript-abstract-operations.md` rule for Intl Temporal
  effective-slot behavior.
