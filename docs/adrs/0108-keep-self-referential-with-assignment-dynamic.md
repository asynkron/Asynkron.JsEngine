# ADR 0108: Keep self-referential with assignment dynamic

## Status

Accepted

## Context

Issue #gh1707 followed up on the self-referential arithmetic assignment
optimization from PR #1701. That optimization can rewrite static-slot shapes
such as `x = x + 1` into a compound-slot instruction when scope analysis proves
both identifiers name the same binding.

The risky adjacent shape was dynamic object-environment lookup:

```js
with (proxy) {
    p = p + 2;
}
```

For this source form, the left side assignment target and the right side read
are distinct observable operations. A proxy can observe the `has`,
`Symbol.unscopables`, `get`, and final `set` sequence. Treating the syntax as a
compound assignment shortcut would hide the separate RHS read and could skip
the object-environment reference semantics that `with` requires.

PR #1710 did not need a runtime patch. The current implementation already kept
the shape on generic assignment-reference semantics once guarded by a focused
regression. The learned decision is therefore a preservation rule: future
assignment-lowering work must not use syntactic self-reference alone as proof
for compound-slot lowering.

## Decision

Keep self-referential assignment optimization limited to proven static-slot
bindings.

The durable policy is:

1. rewrite `x = x <op> rhs` to a compound-slot instruction only after scope
   metadata proves both `x` references resolve to the same static slot;
2. keep `with`, proxy-observable object-environment lookup, and other
   no-cache/dynamic identifier paths on ordinary assignment-reference
   semantics;
3. when guarding this class, prove both runtime trap/order behavior and
   instruction shape, because either check alone can miss the regression; and
4. prefer keeping plain dynamic assignment as `AssignmentSlotInstruction` or the
   generic reference path rather than normalizing it into
   `CompoundAssignmentSlotInstruction`.

## Consequences

- The `ir-arithmetic` self-assignment fast path remains available for static
  slot-owned code.
- Dynamic `with` and proxy cases continue to expose the ECMAScript read before
  write sequence.
- Future performance work in assignment lowering must carry a binding proof,
  not just a syntax proof, before choosing a compound-slot instruction.

## Related

- Issue #gh1707 / PR #1710
- Issue `autrun-dir08v6q4vag-367d2e753a` / PR #1701
- `.claude/rules/expression-bytecode-assignment.md`
