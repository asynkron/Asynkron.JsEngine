# ADR 0107: Keep self-referential assignment slot optimization slot-proven

## Status

Accepted

## Context

Issue `autrun-dir08v6q4vag-367d2e753a` optimized the `ir-arithmetic` profile.
The selected hot loop spelled accumulation as:

```javascript
total = total + ((i * 3 + 7) % 11);
```

The pre-change CPU profile showed the generic `AssignmentSlotInstruction` path
re-evaluating a full RHS expression program that loaded `total`, computed the
binary expression, and wrote the result back. The runtime already had a
`CompoundAssignmentSlotInstruction` path for simple compound assignments, so
the tempting optimization was to recognize `x = x <op> rhs` and emit the
compound-slot instruction directly.

Review of the delivery caught the important semantic boundary. A syntax match
alone is not enough for JavaScript assignment. In `with`, direct eval, global
object, or other no-cache/dynamic lookup contexts, the target reference and RHS
identifier lookup must preserve runtime binding resolution. Rewriting these
paths as a static slot update can skip observable dynamic lookup behavior.

## Decision

Treat self-referential assignment optimization as a slot-proven binding rewrite,
not as a pure AST-shape rewrite.

`ExpressionStatementEmitter` may emit the compound-slot fast path only when the
assignment node and left identifier already carry matching static slot metadata.
For ordinary script/function plan builds, `SlotAssignmentRewriter` performs the
conversion after scope analysis resolves the assignment target and the RHS
identifier to the same `(scopeId, slotIndex)` binding.

If the binding cannot be proven static, keep the generic
`AssignmentSlotInstruction` and its assignment-reference semantics. This is
especially important inside `with` and other dynamic lookup paths where the
same identifier spelling can resolve through runtime object/property lookup
rather than a precomputed flat slot.

## Consequences

- Future arithmetic or bitwise assignment optimizations must prove binding
  identity through slot metadata before replacing generic assignment handling.
- Tests should include a static positive case and a dynamic negative case such
  as `with (scope) { value = value + 2; }` to show the optimization does not
  cross the no-cache lookup boundary.
- Emitter-time optimizations should be conservative when analysis metadata has
  not yet been stamped. Prefer lowerer/rewriter-time normalization when the
  rewrite depends on scope resolution.
- The `ir-arithmetic` profile can still use `CompoundAssignmentSlotInstruction`
  for the proven hot path, while dynamic identifier assignments remain on the
  generic assignment instruction.
- This ADR is caused by issue `autrun-dir08v6q4vag-367d2e753a` / PR #1701 and
  complements ADR 0013's slotless assignment reference-capture ordering.
