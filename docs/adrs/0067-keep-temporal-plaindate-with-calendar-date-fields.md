# ADR 0067: Keep Temporal PlainDate.with calendar date fields

## Status

Accepted

## Context

Issue #843 / PR #1169 fixed the Test262
`Temporal_PlainDate_prototype_with` failures for Gregorian era fields and
Hebrew non-ISO calendar fields.

`Temporal.PlainDate.prototype.with` applies partial overrides to the receiver.
For non-ISO calendars, the receiver exposes calendar-visible `year`, `month`,
`day`, and `monthCode`, while the engine stores the underlying date as ISO
fields. The failing Hebrew fixture showed that merging a year override against
raw ISO fields produced the wrong observable calendar fields. The delivery also
exposed a subtler leap-month trap during review: if an omitted Hebrew leap
`monthCode` is resolved too early against the target year, default
`overflow: "constrain"` can throw before options are read, instead of falling
back through the numeric month path and constraining to a valid month.

## Decision

Keep `Temporal.PlainDate.prototype.with` as a receiver-calendar field merge
operation before internal ISO storage conversion.

For future `PlainDate.prototype.with` and adjacent non-ISO calendar work:

1. default partial date overrides from the receiver's observable calendar
   fields, not from the internal ISO storage fields;
2. read and merge `era`/`eraYear` only for era-capable calendars, and require
   them as a pair when used;
3. when `month` and `monthCode` are both omitted, preserve the receiver's
   visible default `monthCode` in the target year only when that code is valid;
4. for invalid default leap `monthCode` with `overflow: "constrain"`, fall back
   through the calendar numeric month path so overflow can constrain rather
   than throw before options ordering is complete; and
5. convert the merged non-ISO calendar date to the internal ISO representation
   only after calendar-field merge and overflow handling are complete.

## Consequences

- Review should check both the observable receiver fields and the internal ISO
  storage conversion for PlainDate updates.
- Future non-ISO `with` fixes should not reuse ISO month/monthCode helpers for
  BCL-backed calendars unless the receiver calendar has first been ruled out.
- The focused proof for this class is the
  `Name=Temporal_PlainDate_prototype_with` Test262 method group, with Hebrew
  leap-month constrain behavior as the review-bounce case to pin locally.
- This ADR is caused by issue #843 / PR #1169 and complements ADR 0057, ADR
  0055, and the root `.claude/rules/ecmascript-abstract-operations.md` rule for
  observable Temporal calendar-field conversion.
