# ADR 0345: Keep standalone ExpressionProgram evaluation on unified bytecode

## Status

Accepted, amended by the E4 lowered-expression bridge retirement.

## Context

Faktorial issue
`planitem-planmanual1780639098493226000-full-unified-bytecode-execution-burndown-b-37fc1c9650`
and delivery PR #3271 closed the E4 guardrail slice by tightening the boundary
between production unified-bytecode routing and standalone `ExpressionProgram`
execution.

The codebase intentionally uses `ExpressionProgram` as lowered expression
bytecode. That does not make every direct call to
`ExecutionPlanRunner.EvaluateStandaloneExpressionProgram(...)` a production
fast-path call. Before this delivery, the simple-IR fallback path in
`TypedAstEvaluator.SyncFunctionInvoker` called the standalone runner directly,
which made source scans less able to distinguish production routing from
fallback-only lowered expression execution.

Production-accepted routes should return through
`UnifiedBytecodeVirtualMachine`. Lowered expression-program execution that
remains outside that route must be explicitly classified, either as a bridge,
class-definition/class-field support, dynamic-boundary work, or a fallback-only
surface. The profiler-only bridge was later retired by compiling the synthetic
`ProfileRunner` bytecode cases to standalone unified bytecode and executing the
unified VM directly.

Later E4 work deleted `ExecutionPlanRunner.EvaluateStandaloneExpressionProgram(...)`.
A follow-up slice also deleted `TypedAstEvaluator.EvaluateLoweredExpressionProgram(...)`.
Standalone `ExpressionProgram` payloads now execute through
`UnifiedBytecodeExpressionProgramExecutor`, which compiles them to standalone
unified bytecode and executes `UnifiedBytecodeVirtualMachine` directly. This
keeps standalone expression-program execution centralized without preserving an
AST-evaluator bridge method.

PR #3360 tightened the E4 source gate after the initial guard still used broad
file-level permission for `EvaluateLoweredExpressionProgram(...)` callers. That
was too coarse for files such as `TypedAstEvaluator.ExpressionPrograms.cs` and
`TypedAstEvaluator.SyncFunctionInvoker.cs`, where bridge definitions,
dynamic-boundary forwarding, class-field support, and fallback-only execution
can coexist.

PR #3375 then replayed the same ownership boundary on current `main` and
expanded the guardrail from hand-written source gates into durable proof
manifest rows. That pass made two additional facts test-owned: the profiler
bridge tombstones must be absent from both engine code and `tools/ProfileRunner`,
and `UnifiedBytecodeExpressionProgramExecutor.ExecuteStandalone(...)` call sites
must stay inside the approved helper-owned boundary surface.

PR #3434 rebaselined that proof-manifest row as the finite E4 inventory instead
of a vague "source-gated but not retired" bucket. The row now treats deleted
bridge names as source-absence ratchets and keeps
`UnifiedBytecodeExpressionProgramExecutor.ExecuteDynamic(...)` as the sole named
open E4 dynamic expression bridge. Runner-internal expression evaluation remains
E5-owned runner-retirement inventory, not an E4 closure blocker.

Faktorial issue
`planitem-planitem-planitem-gh3377-rebaseline-the-finite-bytecode-retirement-inven-1e1bc813d8`
then converted the sync-function and binding-program standalone payload call
sites into one child slice. The important boundary was not "all binding target
execution is standalone"; it was "external lowered binding-target callers may
use standalone unified bytecode, while runner-internal binding-target execution
remains E5-owned until the runner tier is retired."

## Decision

Keep standalone `ExpressionProgram` execution centralized in
`UnifiedBytecodeExpressionProgramExecutor`, and keep that executor on unified
bytecode rather than the AST evaluator or IR runner.

- Normal already-lowered callers should use
  `UnifiedBytecodeExpressionProgramExecutor.ExecuteStandalone(...)`.
  Quarantined legacy AST expression callers still use
  `UnifiedBytecodeExpressionProgramExecutor.ExecuteDynamic(...)` until that
  dynamic expression-program tier is retired. Direct calls to
  `ExecutionPlanRunner.EvaluateStandaloneExpressionProgram(...)` are
  tombstoned.
- `UnifiedBytecodeExpressionProgramExecutor.ExecuteStandalone(...)` owns compiling standalone expression
  payloads to standalone unified bytecode and forwarding `newTarget` into
  `UnifiedBytecodeVirtualMachine.Execute` so constructor/class surfaces do not
  lose constructor metadata while using the executor.
- Production routing code must not call
  `ExecutionPlanRunner.EvaluateStandaloneExpressionProgram(...)` directly, and
  that method must not be reintroduced.
- `TypedAstEvaluator.EvaluateLoweredExpressionProgram(...)` is tombstoned and
  must not be reintroduced.
- `TypedAstEvaluator.EvaluateDynamicExpressionProgram(...)` is tombstoned and
  must not be reintroduced.
- `ExecutionPlanRunner.ProfileEvaluateExpressionProgramLoop(...)` is
  tombstoned and must not be reintroduced; profiler cases that can compile
  standalone should execute through `UnifiedBytecodeVirtualMachine`.
- Remaining dynamic expression-program call sites must stay source-gated and
  owner-classified. Already-lowered call sites should call the unified executor
  directly, not revive an AST-evaluator helper.
- The simple-IR return expression path in `SyncFunctionInvoker` remains
  fallback-only. Production-accepted routes are considered first and return
  through `UnifiedBytecodeVirtualMachine` before this simple-IR fallback is
  considered.
- External lowered binding-target callers should use the static lowered
  binding-target core and route nested expression payloads through standalone
  unified bytecode. Runner-internal binding-target execution remains on the
  runner-owned path until the E5 runner-retirement lane removes it deliberately.

## Consequences

- E4 can be tracked as a fallback-boundary guardrail instead of a vague
  "ExpressionProgram means AST fallback" bucket.
- Source-gate tests can prove direct standalone runner and lowered evaluator
  calls are absent while still allowing intended lowered payload execution
  through standalone unified bytecode.
- Proof-manifest source gates should cover both engine and profiling-tool
  owners when the retired bridge previously had tool-visible call sites.
- Future expression-bytecode refactors must use the unified executor or update
  the dynamic executor source gate when adding a new dynamic expression-program
  caller.
- If a future slice deletes the simple-IR fallback call, the guardrail should be
  moved from "fallback-only classified" to "tombstoned" rather than silently
  leaving an allowlist entry.
- If a future slice deletes any other quarantined helper, add or update the
  matching tombstone source gate in the same commit.
- Future E4 rebaselines should preserve the finite-inventory shape: deleted
  bridge names stay as absence ratchets, `ExecuteDynamic(...)` stays explicitly
  source-present while open, and runner-internal expression-program evaluation
  stays classified under E5 until that runner-retirement lane is owned.
- Future binding-target bridge work must preserve the E4/E5 split: eliminating a
  runner helper for external lowered callers is not permission to reroute
  runner-internal binding-target evaluation through standalone execution in the
  same slice.

## Evidence

- Delivery PR #3271 merged as commit `4dd1f22ac`.
- The delivery changed:
  - `src/Asynkron.JsEngine/Ast/TypedAstEvaluator.ExpressionPrograms.cs`
  - `src/Asynkron.JsEngine/Ast/TypedAstEvaluator.SyncFunctionInvoker.cs`
  - `tests/Asynkron.JsEngine.Tests/ExecutionPlanDiagnosticsTests.cs`
- Focused guardrails added:
  - `SourceGate_E4_ProductionRoutes_DoNotCallStandaloneExpressionProgramEvaluator`
  - `SourceGate_E4_LoweredExpressionProgramCallers_AreClassified`
  - Later replacement:
    `SourceGate_E4_LoweredExpressionProgramBridge_IsCompletelyRemoved`
- Focused verification from the delivery:
  - `rtk dotnet test tests/Asynkron.JsEngine.Tests --filter "FullyQualifiedName~ExecutionPlanDiagnosticsTests.SourceGate_E4|FullyQualifiedName~ExecutionPlanDiagnosticsTests.SourceGate_DynamicExpressionProgramBridge|FullyQualifiedName~ExecutionPlanDiagnosticsTests.SourceGate_ExecutionPlanRunner"` passed: 4 tests.
  - `rtk dotnet test tests/Asynkron.JsEngine.Tests --filter "FullyQualifiedName~BytecodeNonResidueDeclineRatchetTests"` passed: 20 tests.
  - Runner seam scan found no `EvaluateExpression(` /
    `ProfileEvaluateExpression(` matches in
    `TypedAstEvaluator.ExecutionPlanRunner*`.
- ADR allocation note: the local `rtk faktorial-api adr-next` wrapper was not
  available in this runtime (`No such file or directory`), so the learn pass
  used the runtime allocator endpoint `POST /api/adrs/next`, which returned
  `{"adr_id":345}`.
- PR #3360, from Faktorial issue
  `planitem-planmanual1780730299657353000-unified-bytecode-remaining-burndown-05-fal-5c9c48de33`,
  refined `SourceGate_E4_LoweredExpressionProgramCallers_AreClassified` from a
  broad file allowlist into explicit role classification. The delivery branch
  commit was `20bedcd83`; the merged PR commit on `main` was `6b44841ce`.
  Focused verification passed:
  - `rtk dotnet test tests/Asynkron.JsEngine.Tests --filter "FullyQualifiedName~ExecutionPlanDiagnosticsTests&FullyQualifiedName~SourceGate_E4"`:
    2 tests.
  - `rtk dotnet test tests/Asynkron.JsEngine.Tests --filter "FullyQualifiedName~UnifiedBytecodeProductionInvocationTests&FullyQualifiedName~SourceGate"`:
    4 tests.
  - `rtk git diff --check`.
- PR #3375, from Faktorial issue
  `planitem-planitem-planmanual1780730299657353000-unified-bytecode-remaining-burndo-dc78f9b61a`,
  added durable proof manifest rows for the profiler tombstones and the
  `ExecuteStandalone(...)` helper ownership allowlist. The delivery branch
  commit was `e6cdf2b9f`; the merged PR commit on `main` was `7fa2a4b0a`.
  Focused verification passed:
  - `rtk dotnet test tests/Asynkron.JsEngine.Tests --filter "FullyQualifiedName~ExecutionPlanDiagnosticsTests.SourceGate|FullyQualifiedName~BytecodeProofManifestTests"`:
    120 tests.
  - `rtk git diff --check`.
- PR #3434, from Faktorial issue
  `planitem-planitem-planitem-gh3377-rebaseline-the-finite-bytecode-retirement-inven-78b3cfcd1a`,
  rebaselined the E4 manifest/checklist wording around the finite source
  inventory and added a source-presence assertion for the live
  `ExecuteDynamic(...)` definition. The delivery branch commit was
  `4f258ac21`; the merged PR commit on `main` was `6acd1acc1`.
  Focused verification passed:
  - `rtk dotnet test tests/Asynkron.JsEngine.Tests --filter "FullyQualifiedName~ExecutionPlanDiagnosticsTests|FullyQualifiedName~BytecodeProofManifestTests"`:
    239 tests.
  - `rtk git diff --check`.
- Faktorial issue
  `planitem-planitem-planitem-gh3377-rebaseline-the-finite-bytecode-retirement-inven-1e1bc813d8`
  converted the sync-function and binding-program standalone payload boundary.
  Local merged delivery commits inspected during the learn pass were
  `1033dfc6c`, `9c3d31019`, and `9f259a1e8`.
  The delivery changed the standalone runner bridge, binding-target runner
  bridge, standalone bytecode binding-target support, matching source gates, and
  proof-manifest/checklist rows. The issue acceptance boundary preserved
  runner-internal binding-target expression execution as E5-owned while routing
  external lowered binding-target payloads through standalone unified bytecode.

## Related

- `docs/rules/expression-bytecode-ast-seams.md`
- `docs/plans/bytecode-burndown-checklist.md`
