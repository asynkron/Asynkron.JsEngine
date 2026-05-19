# ADR 0018: Keep delete-super reference check before property key

## Status

Accepted

## Context

Issue #778 fixed the Test262 `Expressions_delete` failures for `delete super`
inside derived constructors.

The expression bytecode compiler lowered `delete super[expr]` by evaluating the
computed property expression before throwing the required delete-super
`ReferenceError`. That ordering was wrong before `super()` initialized `this`:
ECMAScript must reject the `super` reference first, so computed property key
side effects must not run until the derived constructor's `this` binding is
initialized.

The runtime already had a dedicated `EnsureSuperReference` bytecode operation
that enforces the derived-constructor `this` initialization check. The bug was
not missing runtime capability; the compiler had placed the check after the
observable computed-key boundary.

## Decision

For expression bytecode lowering of `delete super.property` and
`delete super[expr]`, emit `EnsureSuperReference` before any computed property
key evaluation and before the final delete-super `ReferenceError`.

Keep initialized and uninitialized cases distinct through ordering, not through
an AST fallback. When `this` is uninitialized, the `super` reference check
throws before a computed key can run. After `super()` has initialized `this`, a
computed key may run for side effects and the delete-super operation still
throws `ReferenceError` because super-property references are not deletable.

## Consequences

- Future `super` property bytecode work must treat "validate the super
  reference" and "evaluate the property key" as separate observable steps.
- Do not repair delete-super ordering by disabling expression bytecode lowering
  or by moving the throw to a runner-time AST seam.
- Regression proof should include both structural lowering order
  (`EnsureSuperReference` before computed-key bytecode before
  `ThrowReferenceError`) and runtime behavior: pre-`super()` computed keys do
  not run, while post-`super()` computed keys do run before the delete-super
  `ReferenceError`.
- This ADR is caused by issue #778 / PR #970 and complements ADR 0012's
  expression-bytecode reference-target split and ADR 0013's spec-ordered
  reference capture.
