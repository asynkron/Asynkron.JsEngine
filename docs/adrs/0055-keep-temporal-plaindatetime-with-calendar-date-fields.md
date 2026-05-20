# ADR 0055: Keep Temporal PlainDateTime.with calendar date fields

## Status

Accepted

## Context

Issue #839 / PR #1159 fixed the Test262
`Temporal_PlainDateTime_prototype_with` failure for
`non-iso-calendar-fields.js`.

`Temporal.PlainDateTime.prototype.with` applies partial date/time overrides to
the receiver. For non-ISO calendars, the receiver's observable `year`, `month`,
`day`, and `monthCode` are calendar fields, while the engine stores the
underlying date as ISO fields. The previous implementation merged overrides
against the internal ISO date. That meant a Hebrew `PlainDateTime` could expose
the correct calendar after construction, but a later `.with(...)` call defaulted
or resolved date fields through ISO values.

The repair made `with` read receiver defaults through calendar-aware
PlainDateTime fields, resolve `monthCode` in the receiver calendar for the
target calendar year, and convert the resulting calendar date back to the
internal ISO date only at the storage boundary. Time fields remain plain numeric
fields and continue to use the existing overflow behavior.

## Decision

Keep `Temporal.PlainDateTime.prototype.with` as a calendar-date merge operation
before internal ISO storage conversion.

For future `PlainDateTime.prototype.with` and adjacent non-ISO calendar work:

1. default partial date overrides from the receiver's observable calendar
   fields, not from the internal ISO storage fields;
2. preserve the receiver's default `monthCode` across year changes when no
   explicit month or monthCode is supplied;
3. resolve supplied `monthCode` through the receiver calendar and target
   calendar year before checking agreement with a supplied numeric `month`;
4. apply ISO date overflow helpers only on the ISO calendar path; and
5. convert non-ISO calendar dates to the internal ISO representation only after
   calendar-field merge and overflow handling are complete.

## Consequences

- Review should check both sides of a `PlainDateTime` change: observable
  calendar fields used by Temporal operations and internal ISO fields used for
  storage/range checks.
- Future non-ISO `with` fixes should not reuse ISO month/monthCode helpers for
  BCL-backed calendars unless the receiver calendar has first been ruled out.
- The focused proof for this class is the
  `Name=Temporal_PlainDateTime_prototype_with` Test262 method group, with the
  non-ISO calendar-fields fixture as the narrow first check.
- This ADR is caused by issue #839 / PR #1159 and complements ADR 0048 plus the
  root `.claude/rules/ecmascript-abstract-operations.md` rule for observable
  Temporal calendar-field conversion.
