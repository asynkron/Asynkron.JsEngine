# ADR 0295 — Admit property-read short-circuit expressions with simple-RHS constraint

## Status

Accepted

## Context

ADR 0293 / PR #2761 admitted `&&`, `||`, and `??` operators as production-eligible
unified bytecode shapes for slot-to-slot or slot-to-literal operand pairs, using
three peek-semantics opcodes (`JumpIfShortCircuitFalse`, `JumpIfShortCircuitTrue`,
`JumpIfShortCircuitNotNullish`).

PR #2766 added a boundary helper (`TryIsNamedPropertyReadAtLogicalShortCircuitBoundary`)
to allow property-read chains to appear on the LHS when recognizing candidates for other
expression shapes, and added `GetNamedProperty` to the compiler's general expression-op
loop. However, the full `this.prop &&/||/?? rhs` expression as a *standalone return
value* — where the entire expression program is the short-circuit form — still lacked a
dedicated eligibility and compiler path. Nine test cases for `ThisPropertyLeft` short-
circuit variants were left failing as forward-looking coverage.

Issue `autrun-diwb8ex5sizk-189ab69a31` / PR #2773 fixed those 9 failures by implementing
the complete `this.prop &&/||/?? rhs` admission.

## Decision

Accept the expression program shape:

```
[activation-resolved base, GetNamedProperty+, JumpIfX, Pop, simple-rhs]
```

as a production-eligible standalone return expression using two dedicated helpers:

- **`TryIsFirstBoundaryPropertyReadShortCircuitExpressionCandidate`** (eligibility)
- **`TryAppendFirstBoundaryPropertyReadShortCircuitExpression`** (compiler)

These helpers mirror the structure of the existing `TryIsFirstBoundaryPropertyReadBinaryExpressionCandidate` /
`TryAppendFirstBoundaryPropertyReadBinaryExpression` pair, but differ in two load-bearing ways:

### 1. RHS is restricted to a single simple operand

The binary-expression helper accepts multi-op RHS spans (array, object, template literals)
because the `Binary` opcode emitted last is a known-fixed endpoint. The short-circuit
helper restricts RHS to a single simple operand (`LoadLiteral`, `LoadIdentifier` resolving
to an activation slot, `LoadThis`, or `LoadNewTarget`). This avoids variable-length
backpatch complexity: with a single-op RHS the jump placeholder can be patched to
`unified.Count` immediately after the RHS instruction is emitted.

### 2. Jump target validation guards against malformed programs

The eligibility check requires `jumpOp.Target == expressionProgram.OperationCount` — the
short-circuit jump must target *exactly* the op count (one past the last instruction),
meaning it exits at the end of the expression. Any program where the jump target is in the
middle is rejected rather than speculatively compiled.

### Backpatch pattern

The compiler emits a placeholder `JumpIfShortCircuit*` operand (0), emits `Pop` and the
RHS load, then overwrites the placeholder with `unified.Count` — the exact PC following
the RHS. This is safe because the RHS is always exactly one emitted instruction.

### Optional chains remain declined

`JumpIfShortCircuited` (optional-chain `?.`) continues to decline as
`OptionalChainDependency`; the short-circuit jump opcode restriction
(`JumpIfFalse or JumpIfTrue or JumpIfNotNullish` only) enforces this at both
eligibility and compile time.

## Consequences

- `this.prop && b`, `this.prop || b`, `this.prop ?? b` (and the activation-slot LHS
  variants `x.prop && b` etc.) execute through the production unified bytecode fast path.
- Multi-op RHS spans (array/object/template literals) on the short-circuit form decline,
  not because they are semantically wrong, but because the current helper does not own
  the variable-length span+backpatch pattern for jump operands. A future slice can widen
  this by measuring the RHS span first, then emitting the jump placeholder, then emitting
  the span, then backpatching.
- The `TryIsNamedPropertyReadAtLogicalShortCircuitBoundary` helper (from PR #2766) remains
  in use for recognizing property-read LHS within boundary-candidate probes that are
  *not* standalone short-circuit return expressions; this ADR does not replace it.

## Related

- ADR 0293 — admit `&&`, `||`, `??` with peek-jump semantics (slot operands)
- Rule `unified-bytecode-prototypes.md` rule #41 — short-circuit expression production boundary
- PR #2773 — delivery that fixed 9 pre-existing `ThisPropertyLeft` test failures
