# ADR 0192: Keep unified bytecode control-flow expansion compiler-owned

## Status

Accepted

## Context

Issue #2166 / PR #2173 expanded the unified bytecode prototype past linear
sync function bodies. A later bounded follow-up (issue #2182 / PR #2186) adds
one canonical condition-first loop back-edge IR topology. The accepted slice
compiles branch-shaped `ExecutionPlan`s plus that guarded single-head loop into
one `UnifiedBytecodeProgram`:

```text
BranchInstruction condition
  -> JumpIfFalse alternate_pc
  -> consequent bytecode
  -> Jump join_pc
  -> alternate bytecode
  -> joined return bytecode

BranchInstruction loop_condition
  -> JumpIfFalse loop_exit_pc
  -> supported loop body bytecode
  -> Jump loop_condition_pc
```

The implementation maps IR instruction indices to unified bytecode program
counters, patches forward `Jump` and `JumpIfFalse` operands after target blocks
are emitted, and executes the result with a PC-based VM loop. It also accepts
representative local assignment and compound-assignment joins plus comparison
operators needed by branch conditions.

The key boundary remains the same as earlier unified-bytecode ADRs: production
execution still uses the existing statement IR plus expression bytecode path,
and the unified VM may not hide unsupported coverage by calling back into
`ExpressionProgram` execution, AST evaluation, or `ExecutionPlanRunner`.

## Decision

Keep unified bytecode branch/control-flow expansion compiler-owned and
fallback-free, with loops still constrained to one proven condition-first
back-edge IR shape.

- Compile from the already-lowered `ExecutionPlan`; do not introduce a parallel
  AST-to-unified-bytecode control-flow compiler.
- Use an IR-instruction-index to bytecode-PC map as the owner of branch target
  identity. Patch forward `Jump` and `JumpIfFalse` operands after target blocks
  are emitted.
- Permit only one canonical loop back-edge shape: an already-active
  `BranchInstruction` condition head reached again by a supported body
  write/update `Next` edge. Source forms such as guarded `while` and
  condition-only `for` are eligible only when lowering produces this same
  condition-first topology.
- Emit branch conditions, local assignment joins, compound assignment joins,
  comparison operators, and returns only as owned unified bytecode operations
  for the currently proven expression-op subset.
- Reject unsupported branch payloads, awaited paths, async/generator functions,
  unsupported loop control flow (for example `break`/`continue` shaped jumps),
  invalid targets, and unproven expression operations at compile time.
- Keep `UnifiedBytecodeVirtualMachine` PC-based and limited to emitted unified
  opcodes; do not add a VM callback to the existing evaluators.

## Consequences

- Branch plans and one canonical condition-first loop back-edge shape can now
  prove non-linear control flow without changing production runtime routing.
- Loop support is still intentionally narrow. Future loop work needs explicit
  proof for each additional control-flow shape.
- Unsupported branch coverage remains visible as a compile-time decline rather
  than being masked by mixed execution.
- Future branch/control-flow slices should include opcode assertions, both-arm
  execution proofs, joined-local-update proofs, canonical-loop execution proofs,
  and unsupported-loop-control-flow decline tests.
- The comparison and arithmetic operators accepted here are the currently
  proven unified VM surface only. Broader JavaScript expression/operator
  coverage still requires explicit parity evidence.

## Related

- Issue #2166
- PR #2173
- Issue #2182
- PR #2186
- ADR 0181: `docs/adrs/0181-keep-unified-bytecode-prototype-ir-owned-and-all-or-nothing.md`
- ADR 0186: `docs/adrs/0186-keep-unified-bytecode-function-kind-eligibility-explicit.md`
- ADR 0189: `docs/adrs/0189-keep-unified-bytecode-linear-expression-packs-flattened.md`
- `.claude/rules/unified-bytecode-prototypes.md`
- `src/Asynkron.JsEngine/Execution/UnifiedBytecode/UnifiedBytecodeCompiler.cs`
- `src/Asynkron.JsEngine/Execution/UnifiedBytecode/UnifiedBytecodeProgram.cs`
- `src/Asynkron.JsEngine/Execution/UnifiedBytecode/UnifiedBytecodeVirtualMachine.cs`
- `tests/Asynkron.JsEngine.Tests/UnifiedBytecodePrototypeTests.cs`
