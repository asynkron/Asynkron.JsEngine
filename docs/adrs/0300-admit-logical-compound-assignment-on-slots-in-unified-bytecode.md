# ADR 0300 — Admit Logical Compound Assignment on Slots in Unified Bytecode

## Status
Accepted

## Context

`&&=`, `||=`, and `??=` on slot-identifier targets (`x &&= y`, `x ||= y`, `x ??= y`)
are lowered to `LogicalCompoundAssignmentSlotInstruction`. These operators carry
a conditional short-circuit: `x &&= y` only evaluates and stores `y` when `x` is
truthy; `x ||= y` only evaluates and stores when `x` is falsy; `x ??= y` only when
`x` is `null` or `undefined`.

Production unified bytecode already owned three peek-semantics short-circuit jump
opcodes for expression-level `&&`/`||`/`??` (ADR 0293):
- `JumpIfShortCircuitFalse` (for `&&`)
- `JumpIfShortCircuitTrue` (for `||`)
- `JumpIfShortCircuitNotNullish` (for `??`)

The question was how to reuse those opcodes for the *statement-level* assignment
form, where the stack contract differs from the pure expression form.

## Decision

Admit `LogicalCompoundAssignmentSlotInstruction` to the production unified bytecode
compiler and eligibility checker. The compiler emits a statement-level pattern using
the existing peek-semantics jump opcodes.

### Emitted pattern

```
LoadSlot(slot)                   // push slot value as condition
JumpIfShortCircuitX(sc-pop)     // peek: if short-circuit, keep TOS and jump to sc-pop
Pop                              // proceeding path: discard slot value
[RHS expression program ops]    // evaluate RHS; result is now TOS
StoreSlot(slot)                  // write RHS result back to slot
Jump(end)                        // skip sc-pop
[sc-pop:] Pop                    // short-circuit path: discard slot value from TOS
[end:]
```

Both paths leave the operand stack balanced (empty net delta). The slot value is
used only as a condition; it is discarded regardless of which branch is taken.

### Distinction from expression-level `&&`/`||`/`??`

In expression-level short-circuit operators (`a && b`, `a || b`, `a ?? b`), the
LHS value **IS** the expression result when short-circuiting. The peek-semantics
opcodes leave TOS intact so the caller receives the LHS value.

In statement-level logical assignment (`x &&= y`), the LHS value is consumed as
a condition only. When short-circuiting, the slot already holds the correct value
— no assignment is needed, and the TOS copy of the slot value is discarded at
`[sc-pop:]`. When proceeding, the TOS copy is also discarded (by the `Pop` on the
proceeding path) before the RHS is evaluated and stored.

### Scope and retained declines

- **Admitted**: `&&=`, `||=`, `??=` on slot-identifier targets
  (`LogicalCompoundAssignmentSlotInstruction` with resolved flat slot, non-awaited,
  slot-provable target).
- **Retained decline**: `this.x &&= y` and other member logical assignments
  remain declined as `PropertyWriteDependency`. The conditional short-circuit
  semantics require a branch opcode that the compound get-for-set model does not
  provide (ADR 0238, rule 19 in `unified-bytecode-prototypes.md`).
- **Gate 1 side-effect test design**: To prove the short-circuit opcode never
  evaluates the RHS, a side-effecting RHS function call (`sideEffect()`) is used.
  A global identifier call target without `HasExplicitThis` declines the production
  fast path (`TryIsFirstBoundaryCallTargetPreparationCandidate` returns `false`).
  The test proves correctness of the short-circuit opcode on whichever execution
  path handles the function, but must **not** assert the
  `unified-bytecode-production-fast-path` log for this specific shape.

## Consequences

- Production unified bytecode now handles the complete set of slot-identifier
  assignment forms: simple assignment, arithmetic/bitwise compound assignment,
  and all three logical compound assignment operators.
- The `JumpIfShortCircuitFalse`, `JumpIfShortCircuitTrue`, and
  `JumpIfShortCircuitNotNullish` opcodes serve dual purpose: expression-level
  result-returning short-circuit (LHS stays on TOS as result) and statement-level
  condition-only short-circuit (both paths discard the slot TOS copy).
- Member logical assignments (`this.x &&= y`, `box.prop ||= y`) remain declined
  until a future slice owns conditional branch opcodes in the compound get-for-set
  model.
- Stack-balance regression coverage: a multi-call test
  (`LogicalAndAssignment_SlotBased_MultipleCallsDoNotCorruptStack_UsesProductionFastPath`)
  proves a stack misbalance does not accumulate across repeated invocations.

## Issue / PR

Issue `planitem-planmanual1780198120145433000-widen-unified-bytecode-production-conditio-dad47dee93`
/ PR #2810.
