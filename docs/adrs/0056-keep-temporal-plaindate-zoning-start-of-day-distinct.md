# ADR 0056: Keep Temporal PlainDate zoning start-of-day distinct

## Status

Accepted

## Context

Issue #841 / PR #1158 fixed the Test262
`Temporal_PlainDate_prototype_toZonedDateTime` failures for
`intl402/Temporal/PlainDate/prototype/toZonedDateTime/dst-skipped-cross-midnight.js`.

The failing fixture used `America/Toronto` on 1919-03-31, where the day starts
inside a historical midnight transition. `Temporal.PlainDate.prototype.toZonedDateTime`
does not always mean "combine the date with midnight." When `plainTime` is
omitted or explicitly `undefined`, the operation must use the time zone's true
start-of-day instant. When `plainTime` is explicitly supplied, including
`new Temporal.PlainTime()`, the existing PlainDateTime/midnight disambiguation
path remains observable and can intentionally differ from start-of-day.

The previous implementation collapsed both cases into midnight by substituting
a zero `PlainTime` whenever `plainTime` was absent. That passed ordinary days
but failed skipped-midnight days because start-of-day and explicit midnight are
not equivalent at Temporal's abstract-operation boundary.

## Decision

Keep `Temporal.PlainDate.prototype.toZonedDateTime` omitted/undefined
`plainTime` behavior separate from explicit `PlainTime` behavior.

For future Temporal PlainDate zoning work:

1. route omitted or explicitly `undefined` `plainTime` through
   `GetStartOfDayInstant`;
2. keep explicit `plainTime`, including explicit midnight, on the
   PlainDateTime/midnight disambiguation path;
3. do not replace absent Temporal fields with zero-valued Temporal objects
   unless the spec operation explicitly says the field is defaulted before the
   observable branch decision;
4. preserve fixed-offset time-zone behavior while keeping IANA skipped-midnight
   handling on the shared start-of-day helper; and
5. prove this class with the focused
   `Name=Temporal_PlainDate_prototype_toZonedDateTime` Test262 method group,
   including strict and sloppy variants when the issue came from that group.

## Consequences

- Future PlainDate zoning repairs should inspect whether the spec distinguishes
  absence from an explicit zero value before normalizing property bags.
- Review should check skipped-midnight IANA cases separately from ordinary
  midnight disambiguation cases, because both can produce valid but different
  instants.
- This ADR is caused by issue #841 / PR #1158 and complements the root
  `.claude/rules/ecmascript-abstract-operations.md` rule.
