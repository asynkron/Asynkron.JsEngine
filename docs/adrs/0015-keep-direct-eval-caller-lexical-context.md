# ADR 0015: Keep direct eval caller lexical context explicit

## Status

Accepted

## Context

Issue #773 / PR #951 fixed direct `eval` handling for caller-context
`super` and `new.target` semantics.

The delivery started as a direct-eval repair, but review exposed a narrower
trap: arrow functions do not own their own `super` or `new.target` binding.
Direct eval inside an arrow must still evaluate against the arrow's lexical
caller context. A categorical "arrow caller cannot use direct-eval super" rule
therefore rejected valid method-arrow and derived-constructor-arrow cases.

The first review-back repair fixed method and constructor arrows, but the
quality gate then exposed class-field initializer direct eval as the same class
of bug: class element evaluation can have a valid home object for `super` even
when it is not an ordinary method invocation frame.

## Decision

Direct eval must carry explicit caller-context eligibility for `super` and
`new.target`. Do not infer that eligibility from only the immediate function
kind.

For direct eval:

- `new.target` is valid when the caller context has a constructor
  `new.target`, including through lexical arrows.
- `super` property access is valid when the caller context has a home object or
  class-field initializer home object, including through lexical arrows.
- `super()` is valid only where the caller context represents a derived
  constructor call path that can actually perform the super-constructor call.
- Ordinary no-home-object functions and arrows without an inherited eligible
  context must still reject `super`.

Keep the validation in the eval/caller-context boundary and the environment
setup paths that feed it. Avoid fixing future failures by adding isolated
parser bans, runner fallbacks, or broad arrow-function exclusions.

## Consequences

- Future direct-eval work must inspect `EvalHostFunction`, function invocation
  environment setup, IR eval environment setup, and class-field initializer
  environment setup together.
- Focused regressions should include method-arrow `super` property access,
  derived-constructor-arrow `super()` and `new.target`, ordinary no-home-object
  negatives, and class-field initializer direct eval.
- Proof should include the focused `EvalFunctionTests.DirectEval` internal pack
  and the owning Test262 direct-eval group before widening.
- This ADR complements ADR 0012's direct-eval call-target split: ADR 0012 keeps
  direct eval classification syntactic and same-engine guarded, while this ADR
  keeps the direct eval execution context tied to the caller's lexical
  `super`/`new.target` eligibility.
