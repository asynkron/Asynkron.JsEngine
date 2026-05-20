# ADR 0054: Keep Temporal Duration rounding ties shared

## Status

Accepted

## Context

Issue #834 / PR #1135 fixed the Test262
`Temporal_Duration_prototype_round` failure group for
`Temporal.Duration.prototype.round`. The delivery repaired several related
`relativeTo` and ZonedDateTime paths, then review found one build-back
correctness bug in the new month-rounding fast path.

The special ZonedDateTime `smallestUnit == "month"` midpoint branch initially
returned two months only for `halfExpand`, `ceil`, and `expand`, and returned
one month for every other rounding mode. That bypassed the engine's shared
`RoundToIncrement` tie semantics. Valid modes such as `halfCeil` and
odd-quotient `halfEven` must round the same way as the generic rounding path,
even when the calendar/ZonedDateTime path has a narrow fast branch.

The repair changed the branch to delegate the midpoint decision to
`RoundToIncrement`, preserving the existing shared rounding-mode semantics
instead of duplicating them locally.

## Decision

Keep Temporal Duration midpoint rounding decisions in shared rounding helpers,
including inside narrow calendar or ZonedDateTime fast paths.

For future `Temporal.Duration.prototype.round` work:

1. avoid hand-written rounding-mode lists for midpoint behavior;
2. route tie decisions through the same helper used by the generic rounding
   path;
3. include `halfCeil`, `halfFloor`, `halfTrunc`, and odd-quotient `halfEven`
   when reviewing midpoint behavior; and
4. prove the focused `Name=Temporal_Duration_prototype_round` Test262 method
   group before widening.

## Consequences

- Calendar-aware or ZonedDateTime-specific branches may still exist, but they
  must not fork the observable rounding-mode contract.
- Review should treat new fast paths around midpoint dates, times, or
  calendar-unit boundaries as abstract-operation reuse questions, not as local
  boolean-mode checks.
- This ADR is caused by issue #834 / PR #1135 and complements the root
  `.claude/rules/ecmascript-abstract-operations.md` rule.
