# ADR 0313: Admit nested driver labeled abrupt cleanup in unified bytecode

## Status

Accepted

## Context

Faktorial issue
`planitem-planmanual1780240661926543000-burn-down-unified-bytecode-production-decl-9e9f5025f7`
and PR #2914 widened production unified bytecode for synchronous nested
iterator/for-in driver loops. The delivery follows ADR 0285, which admitted
labeled control flow but deliberately kept labeled `break`/`continue` that
crossed several active driver loops as a pre-VM decline. At that time the VM
could close only the driver whose `BreakTarget` exactly matched the abrupt
target, and a program-counter ordering heuristic had already been rejected
because lazy target compilation could close the wrong driver set.

The remaining production gap was not label resolution itself. Labels were
already compiler-owned numeric targets. The missing model was topology: for a
control target, the VM had to know which active driver descriptors are being
exited, which driver target is a continue backedge that should remain open, and
which cleanup chains (`PopEnvironment`, `LeaveWith`) stand between a source
target and the effective destination.

## Decision

Admit synchronous labeled abrupt control that crosses nested iterator/for-in
drivers only when cleanup is descriptor-topology-backed and VM-owned.

- Extend `UnifiedBytecodeDriverDescriptor` with `MoveNextTarget`, alongside the
  existing state slot, value slot, next target, and break target metadata. The
  descriptor now carries the loop's own continue/backedge target explicitly.
- Replace exact-break-target cleanup with control-target cleanup. The VM resolves
  leading cleanup-chain opcodes from both the abrupt target and each descriptor
  break target, then closes every active driver whose descriptor lifetime is
  exited by that target.
- Keep cleanup order inner-to-outer by sorting active driver states by
  `ActiveDriverOrdinal` descending before calling the existing driver cleanup
  path.
- Treat a target that resolves to the descriptor's `MoveNextTarget` as staying
  inside that driver loop, so labeled `continue` to an outer driver closes inner
  drivers but keeps the outer driver open.
- Keep the VM fallback-free. Do not repair missed nested-driver cleanup by
  calling `ExecutionPlanRunner`, `ExpressionProgram`, or AST evaluation.
- Widen compiler backedge recognition only for proven cleanup/control shapes:
  direct jumps, completion-value pass-through, `LeaveTry`, `EndFinally`, and
  continue-targeted `PopEnvironment` cleanup into an active driver `MoveNext`
  target.

## Consequences

- ADR 0285's driver-crossing labeled-abrupt decline boundary is superseded for
  synchronous nested iterator/for-in driver loops with descriptor-backed
  topology. The earlier warning against PC-ordering heuristics remains active.
- Future driver-loop widening must keep selector eligibility, compiler target
  descriptors, VM cleanup topology, and route/no-route proof in the same slice.
- Async iterator drivers, awaited iterator/for-in sources, and any driver shape
  whose cleanup cannot be described by the current descriptors remain pre-VM
  declines.
- Proof must cover both behavior and route selection: for-of labeled continue
  closes only the inner iterator, for-of labeled break closes exited iterators
  inner-to-outer, for-in labeled continue routes through production, and
  unsupported async/awaited driver shapes remain declined.

## Evidence

- PR #2914 merged as squash commit
  `707f9128c79607a022a81d339c2179ff5a5d8b92`.
- Build-stage commits:
  - `835727a30` ("Support nested unified driver cleanup")
  - `960bfcd5a` ("Route nested for-of labeled continue in unified bytecode")
- Focused proof from the build comments:
  - `rtk dotnet test tests/Asynkron.JsEngine.Tests --filter "FullyQualifiedName~UnifiedBytecodeProductionEligibilityTests.Evaluate_LabeledContinueCrossingDriverLoop_Accepts|FullyQualifiedName~UnifiedBytecodeProductionInvocationTests.LabeledContinueAcrossNestedForOf_ClosesInnerIteratorOnly|FullyQualifiedName~UnifiedBytecodeProductionInvocationTests.LabeledBreakAcrossNestedForOf_ClosesExitedIteratorsInnerToOuter|FullyQualifiedName~UnifiedBytecodeProductionInvocationTests.LabeledContinueAcrossNestedForIn_UsesProductionFastPath"` passed 4 tests.
  - `rtk dotnet test tests/Asynkron.JsEngine.Tests --filter "FullyQualifiedName~UnifiedBytecodeProduction"` passed 619 tests.
  - `rtk git diff --check` passed.
  - AST-eval seam scan returned no matches.
- The orchestrator quality gate for the delivery worktree passed `make quality`:
  5006 tests passed, 2 skipped, 0 failed, with existing nullable warnings in
  unrelated test files.

## Related

- ADR 0251: `docs/adrs/0251-keep-unified-bytecode-iterator-and-destructuring-drivers-model-first.md`
- ADR 0253: `docs/adrs/0253-keep-unified-bytecode-loop-control-targets-compiler-owned.md`
- ADR 0271: `docs/adrs/0271-keep-unified-bytecode-exception-regions-vm-owned-and-driver-cleanup-topology-guarded.md`
- ADR 0285: `docs/adrs/0285-admit-labeled-control-flow-in-unified-bytecode-and-decline-driver-crossing.md`
- `docs/rules/unified-bytecode-prototypes.md`
- `src/Asynkron.JsEngine/Execution/UnifiedBytecode/UnifiedBytecodeCompiler.cs`
- `src/Asynkron.JsEngine/Execution/UnifiedBytecode/UnifiedBytecodeProgram.cs`
- `src/Asynkron.JsEngine/Execution/UnifiedBytecode/UnifiedBytecodeVirtualMachine.cs`
- `tests/Asynkron.JsEngine.Tests/UnifiedBytecodeProductionEligibilityTests.cs`
- `tests/Asynkron.JsEngine.Tests/UnifiedBytecodeProductionInvocationTests.cs`
- `tests/Asynkron.JsEngine.Tests/UnifiedBytecodePrototypeTests.cs`
