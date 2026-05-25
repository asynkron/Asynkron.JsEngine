# ADR 0125: Keep compound indexed assignment nullish order spec ordered

## Status

Accepted

## Context

Issue #1829 covered Test262 compound-assignment rows around property
evaluation. The repaired implementation was the existing expression bytecode
path for indexed compound assignments such as `base[key] *= rhs`.

The bug was an observable ordering error. The compiler evaluated the assignment
target and key, then converted the key with `ToPropertyKey` before checking
whether the base was object-coercible. For a nullish base, ECMAScript requires
the base check before property-key conversion in the compound assignment
reference path. Running `ToPropertyKey` first could call user code through
`toString` / `valueOf`, produce the wrong error, or hide the intended
JavaScript `TypeError`.

The runtime already had the expression-program stack model needed to represent
this ordering. The durable choice was to add a dedicated
`RequireObjectCoercibleExpressionOp` that checks an existing stack slot by
depth, then emit it before `ResolvePropertyKey` for compound indexed
assignment, rather than falling back to the legacy AST evaluator.

## Decision

Keep compound indexed assignment lowering as explicit expression bytecode
steps:

1. evaluate the assignment base;
2. evaluate the computed key expression;
3. require the base to be object-coercible while the unresolved key remains on
   the stack;
4. resolve the property key exactly once;
5. read the old value, evaluate the right-hand side, apply the compound
   operator, and write the result.

Use an expression-program operation for the nullish-base check when the value to
check is not the stack top. Do not route this class of ordering fix through a
mixed AST/bytecode fallback.

## Consequences

- Future compound member-assignment changes must preserve the separate
  observable boundaries for base evaluation, key-expression evaluation,
  nullish-base rejection, key conversion, old-value read, RHS evaluation, and
  final writeback.
- Regression proof should include nullish bases whose key conversion has an
  observable side effect or throw, so `ToPropertyKey` cannot run after the
  base has already failed `RequireObjectCoercible`.
- This ADR is caused by issue #1829 and complements ADR 0119's computed-member
  read ordering boundary and ADR 0018's super-reference/key-order boundary.
