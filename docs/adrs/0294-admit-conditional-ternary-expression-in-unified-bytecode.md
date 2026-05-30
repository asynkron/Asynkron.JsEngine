# ADR 0294: Admit conditional (ternary) expressions in unified bytecode

## Status

Accepted

## Context

Faktorial issue gh2770. `&&`, `||`, and `??` short-circuit expressions were
admitted in PR #2761 via peek-semantics jump opcodes (ADR 0293). The ternary
`?:` (`ConditionalExpression`) is the natural next short-circuit expression form
that remained outside the unified bytecode production path.

The expression-program compiler (`ExpressionProgramCompiler.cs`) already emits
a complete expression program for `cond ? a : b`:

```
[test ops]
JumpIfFalse(alternateStart)   ← ExpressionOpKind.JumpIfFalse
Pop                            ← discard condition on the truthy path
[consequent ops]
Jump(endTarget)                ← ExpressionOpKind.Jump  ← previously unhandled
Pop                            ← discard condition on the falsy path (at alternateStart)
[alternate ops]
[endTarget]
```

`JumpIfFalse` already mapped to `JumpIfShortCircuitFalse` in the compiler
(peek semantics: condition is left on TOS when the branch is taken so the
`Pop` at `alternateStart` consumes it). The `Jump` (unconditional forward
jump) was the only missing case — the compiler's `TryAppendExpressionProgramOps`
fell through to its `default:` arm and returned an `UnsupportedExpressionOp`
failure, causing `TryCompile` to fail and the eligibility checker to decline
with `UnsupportedPlanShape`.

No new unified-bytecode opcodes are required: the existing `Jump`,
`JumpIfShortCircuitFalse`, and `Pop` opcodes cover the ternary execution model
completely.

## Decision

Admit `ConditionalExpression` (`cond ? a : b`) to the production unified-bytecode
VM by wiring the four-surface coupling checklist (rule 40):

1. **Eligibility** (`UnifiedBytecodeProductionEligibility.cs` `TryFindExpressionDecline`):
   Add `case ExpressionOpKind.Jump:` to the explicit allowed-op set alongside
   `JumpIfFalse`, `JumpIfTrue`, and `JumpIfNotNullish`. This documents intent and
   guards against a future `default:` arm accidentally declining the ternary `Jump`.

2. **Compiler** (`UnifiedBytecodeCompiler.cs` `TryAppendExpressionProgramOps`):
   Add `case ExpressionOpKind.Jump:` that emits a placeholder
   `UnifiedBytecodeOpCode.Jump(0)`, records the patch entry in the `patches` list,
   and backpatches the operand via `exprPcToUnifiedPc[]` after the full op sequence
   is emitted — the same pattern used by `JumpIfFalse/True/NotNullish`.

3. **VM** (`UnifiedBytecodeVirtualMachine.cs`): No new cases. `Jump`,
   `JumpIfShortCircuitFalse`, and `Pop` are already dispatched.

4. **Expansion contract** (`docs/unified-bytecode-expansion-contract.md`):
   Updated opcode inventory note to document `ConditionalExpression` admission.
   `Jump` was already in the inventory; no new opcode is added.

Resumable path: functions containing ternary expressions that are async/generator-kind
are declined before the resumable VM path by the existing `TryFindUnsupportedResumableOpcode`
allowlist — `Jump` is already in the resumable-allowed set, so no change is needed
there either.

Optional-call trailing structure: callee-optional call expression programs
(`box.read?.()`) also contain `ExpressionOpKind.Jump` in their trailing
`..., Call, Jump, SwapTopTwo, Pop` structure, but those programs are attached to
`CallInvocationBoundaryInstruction`, which is not handled by
`TryGetExpressionProgram`. The `TryFindExpressionDecline` op scan is therefore
never called for call-target preparation programs; the explicit
`case ExpressionOpKind.Jump:` in eligibility does not accidentally admit malformed
optional-call shapes through the general path.

## Consequences

- `cond ? a : b` expressions execute in production unified bytecode for ordinary
  sync functions without fallback to `ExpressionProgram`, `ExecutionPlanRunner`,
  or AST evaluation.
- No new opcodes, no new VM dispatch cases, and no resumable-path changes.
- The `ExpressionOpKind.Jump` backpatch pattern in `TryAppendExpressionProgramOps`
  generalizes ADR 0293's `exprPcToUnifiedPc[]` approach to unconditional forward
  jumps within expression programs.

## Evidence

- Delivery PR for gh2770.
- Build-stage baseline signal: `TryAppendExpressionProgramOps` has no
  `ExpressionOpKind.Jump` case (ternary expressions declined at compile step).
- Build-stage final signal: `case ExpressionOpKind.Jump:` added; ternary tests
  assert `unified-bytecode-production-fast-path` log on both truthy and falsy
  condition branches.
- `dotnet build -c Release` — 0 errors, 0 warnings.
- Focused test pack passed: eligibility (truthy/falsy/slot-condition shapes) and
  invocation (truthy branch, falsy branch) tests in
  `UnifiedBytecodeProductionEligibilityTests` and
  `UnifiedBytecodeProductionInvocationTests`.
