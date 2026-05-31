# ADR 0314: Split unified bytecode driver break and continue cleanup targets

## Status

Accepted

## Context

Faktorial issue
`planitem-planmanual1780240661926543000-burn-down-unified-bytecode-production-decl-1daee0b11e`
and PR #2915 followed ADR 0313's nested-driver cleanup work. ADR 0313 made
synchronous nested iterator/for-in labeled abrupt control descriptor-topology
backed, but the first model still treated the driver backedge as the only
continue-like target and used one cleanup classification path for both `break`
and `continue`.

That was too coarse. A labeled `continue` that jumps to an outer loop must close
inner active drivers, but it must not close the target outer driver. A labeled
`break` that exits driver loops must close every exited active driver in
innermost-first order. Those two operations need different target ordinals and
different "stays inside this driver" checks.

The build-stage proof exposed the distinction directly. The focused
`LabeledContinueCrossingForOf_ClosesInnerDriverOnProductionFastPath` test first
returned `102:102` instead of `2:2`, showing that the wrong driver lifetime was
kept open or closed for the continue path. The final delivery introduced the
separate driver continue target metadata and a cleanup classifier that receives
the abrupt kind.

## Decision

Keep production unified bytecode driver cleanup target-kind aware.

- `UnifiedBytecodeDriverDescriptor` carries an explicit `ContinueTarget` in
  addition to break/cleanup and next/move-next metadata.
- VM cleanup callers pass whether the pending abrupt control is a `break` or a
  `continue`; cleanup selection is not inferred only from a numeric target.
- For `break`, cleanup starts at the matched exited driver ordinal and closes
  active drivers at or inside that target in descending active-driver order.
- For `continue`, cleanup keeps the target driver open and closes only active
  drivers deeper than the matched continue target, or drivers whose body no
  longer contains the effective cleanup-chain-resolved target.
- Target comparison resolves cleanup-chain opcodes (`PopEnvironment`,
  `LeaveWith`) before classifying whether a target is the same break/continue
  destination or remains inside a driver body.
- Backedge widening remains compiler-owned. The compiler may accept a continue
  target that points at a branch, iterator `MoveNext`, or for-in `MoveNext`
  only when `HasLoopContinueTarget` proves the associated break/exit target.
- Keep the route fallback-free. Do not repair a missed cleanup case with
  `ExecutionPlanRunner`, `ExpressionProgram`, AST evaluation, or source-syntax
  exceptions.

## Consequences

- ADR 0313 remains the nested-driver topology base, but its "MoveNext target"
  shorthand is no longer sufficient for all continue classification. Future
  work must preserve the explicit `ContinueTarget` and abrupt-kind split.
- New driver-control-flow widening must prove both sides: breaks close all
  exited active drivers, while continues close crossed inner drivers and keep
  the target driver alive.
- Eligibility tests should assert the topology is present, not only that a
  previous decline reason disappeared.
- Async iterator drivers, awaited driver sources, and driver shapes without
  explicit descriptor topology remain pre-VM declines until their cleanup model
  is proven in the same selector/compiler/VM/proof slice.

## Evidence

- PR #2915 merged as squash commit
  `d50a4fa91e179a8e94f94718c31bb39b6a1ab5f2`.
- Build-stage commit:
  - `a10e3c260` ("Handle crossed driver cleanup in unified bytecode")
- Focused proof from the build comments:
  - `rtk dotnet test tests/Asynkron.JsEngine.Tests --filter "FullyQualifiedName~UnifiedBytecodeProductionEligibilityTests.Evaluate_LabeledContinueCrossingDriverLoop_DoesNotDeclineWithLabelControlFlow|FullyQualifiedName~UnifiedBytecodeProductionEligibilityTests.Evaluate_LabeledBreakCrossingDriverLoop_AcceptsWithDriverCleanupTopology|FullyQualifiedName~UnifiedBytecodeProductionInvocationTests.LabeledBreakCrossingForOf_ClosesInnerAndOuterDriversOnProductionFastPath|FullyQualifiedName~UnifiedBytecodeProductionEligibilityTests.Evaluate_LabeledBreakOutOfForOf_Accepts|FullyQualifiedName~UnifiedBytecodeProductionEligibilityTests.Evaluate_LabeledContinueInLoop_Accepts|FullyQualifiedName~UnifiedBytecodeProductionInvocationTests.LabeledBreakOutOfForOf_ClosesDriverOnProductionFastPath"` passed 6 tests.
  - `rtk git diff --check` passed.
- The orchestrator quality gate for the delivery worktree passed `make
  quality`: 5001 tests passed, 2 skipped, 0 failed, with existing nullable
  warnings in unrelated test files.

## Related

- ADR 0251: `docs/adrs/0251-keep-unified-bytecode-iterator-and-destructuring-drivers-model-first.md`
- ADR 0253: `docs/adrs/0253-keep-unified-bytecode-loop-control-targets-compiler-owned.md`
- ADR 0271: `docs/adrs/0271-keep-unified-bytecode-exception-regions-vm-owned-and-driver-cleanup-topology-guarded.md`
- ADR 0285: `docs/adrs/0285-admit-labeled-control-flow-in-unified-bytecode-and-decline-driver-crossing.md`
- ADR 0313: `docs/adrs/0313-admit-nested-driver-labeled-abrupt-cleanup-in-unified-bytecode.md`
- `docs/rules/unified-bytecode-prototypes.md`
- `src/Asynkron.JsEngine/Execution/UnifiedBytecode/UnifiedBytecodeCompiler.cs`
- `src/Asynkron.JsEngine/Execution/UnifiedBytecode/UnifiedBytecodeProgram.cs`
- `src/Asynkron.JsEngine/Execution/UnifiedBytecode/UnifiedBytecodeVirtualMachine.cs`
- `tests/Asynkron.JsEngine.Tests/UnifiedBytecodeProductionEligibilityTests.cs`
