# ADR 0253: Keep unified bytecode loop-control targets compiler-owned

## Status

Accepted

## Context

Faktorial issue
`planitem-planmanual1779943568009120000-batch-1-shared-bytecode-surface-and-parall-9d6cd3060b`
and PR #2489 widened production unified bytecode beyond the ADR 0210
condition-first loop boundary.

The issue asked for simple unlabeled loop control to use the production unified
bytecode route: forward loop exits, direct `break`, direct `continue`,
for-style update continue targets, and simple do-while loop shapes. The prior
production selector explicitly declined all `BreakInstruction` and
`ContinueInstruction` cases. That made the boundary safe but left common loop
control on the legacy route even though compact statement diagnostics and the
unified VM already had the owned `Jump` and `JumpIfFalse` opcodes needed for
the admitted shapes.

The main risk was treating break/continue as a source-syntax permission.
JavaScript loop control is target-sensitive: a forward break target, a
continue target that runs a for-loop update, and a do-while consequent backedge
are different IR topologies. Labeled control flow also remains unproven for
production routing, so numeric target resolution alone cannot make labels safe.

Delivery also exposed a maintenance friction point during rebase. Local
`origin/main` had added explicit for-in and destructuring production declines,
while this branch removed the old blanket break/continue decline. The conflict
resolution preserved both boundaries, and `make quality` then caught prototype
tests that still asserted old loop-control decline behavior for for-loop
post-update shapes.

## Decision

Keep production loop-control support compiler-owned and target-explicit.

- Do not blanket-decline every unlabeled `BreakInstruction` or
  `ContinueInstruction` during production eligibility.
- Compile `JumpInstruction`, `BreakInstruction`, and `ContinueInstruction` to
  owned `Jump` bytecode through one resolved-target path that validates target
  indices, recursively emits forward targets, and patches bytecode program
  counters from the IR-instruction map.
- Allow only proven loop-control backedges. A continue backedge must resolve
  through an unlabeled `BreakableEnterInstruction` whose continue and break
  targets match the loop. A loop body/update backedge must still match the
  compiler-owned linear topology checks before VM execution.
- Allow the simple do-while consequent backedge only when the branch consequent
  reaches the branch linearly and the same unlabeled loop continue/break target
  metadata proves the loop boundary.
- Keep labeled breakable control flow declined explicitly as
  `LabelControlFlow`. Do not infer label safety from resolved numeric targets.
- Keep the VM fallback-free. Unsupported complex loop/control-flow shapes must
  decline before `UnifiedBytecodeVirtualMachine` starts instead of calling back
  into `ExecutionPlanRunner`, `ExpressionProgram`, or AST evaluation.
- Keep proof paired: production selector acceptance, owned opcode assertions,
  public invocation route-log proof for the accepted loop-control shapes, nearby
  labeled/no-route proof, the AST-eval seam scan, and forloop memory-profile
  stability evidence.

## Consequences

- ADR 0210 remains useful history for the first branch/join/canonical-loop
  production boundary, but this ADR owns the current loop-control widening.
- `BreakOrContinueControlFlow` no longer describes a blanket production
  pre-scan for every break/continue instruction. Future agents should treat it
  as taxonomy inventory or use it only for a deliberately reintroduced
  unsupported neighbor, not as the default control-flow policy.
- Future loop-control widening must be phrased in terms of IR target topology
  and compiler-owned proof, not JavaScript source syntax names.
- Prototype tests that used to assert decline for old loop-control gaps are
  part of the production boundary. When the compiler boundary moves, stale
  prototype decline expectations must be updated in the same delivery so the
  canonical `make quality` gate stays meaningful.

## Evidence

- PR #2489 merged commit `7010258f83902f8d710d074bbfd2125083201ae2`.
- Build-stage proof recorded:
  `rtk dotnet test tests/Asynkron.JsEngine.Tests --filter "FullyQualifiedName~UnifiedBytecodeProductionEligibilityTests|FullyQualifiedName~UnifiedBytecodeProductionInvocationTests" -- xUnit.MaxParallelThreads=1 -timeout 20000`
  with 172 tests passing.
- Build-stage AST-eval seam scan:
  `rtk rg "EvaluateExpression\\(|ProfileEvaluateExpression\\(" src/Asynkron.JsEngine/Ast/TypedAstEvaluator.ExecutionPlanRunner*`
  returned no matches.
- Build-stage memory profile:
  `rtk ./tools/profile forloop --memory` reported total allocated 6.75 MB.
- Conflict-resolution proof recorded focused production tests passing with 174
  tests, prototype tests passing with 45 tests, and final `rtk make quality`
  passing after stale prototype expectations were repaired.

## Related

- ADR 0201: `docs/adrs/0201-keep-unified-bytecode-production-routing-decline-first.md`
- ADR 0204: `docs/adrs/0204-keep-unified-bytecode-sync-production-routing-slot-bridged.md`
- ADR 0210: `docs/adrs/0210-keep-unified-bytecode-control-flow-production-routing-operator-and-shape-owned.md`
- ADR 0246: `docs/adrs/0246-keep-unified-bytecode-expansion-contract-source-of-truth-and-drift-guarded.md`
- ADR 0251: `docs/adrs/0251-keep-unified-bytecode-iterator-and-destructuring-drivers-model-first.md`
- `docs/unified-bytecode-expansion-contract.md`
- `.claude/rules/unified-bytecode-prototypes.md`
- `src/Asynkron.JsEngine/Execution/UnifiedBytecode/UnifiedBytecodeCompiler.cs`
- `src/Asynkron.JsEngine/Execution/UnifiedBytecode/UnifiedBytecodeProductionEligibility.cs`
- `tests/Asynkron.JsEngine.Tests/UnifiedBytecodeProductionEligibilityTests.cs`
- `tests/Asynkron.JsEngine.Tests/UnifiedBytecodeProductionInvocationTests.cs`
- `tests/Asynkron.JsEngine.Tests/UnifiedBytecodePrototypeTests.cs`
