# ADR 0345: Keep standalone ExpressionProgram evaluation on unified bytecode

## Status

Accepted

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
The remaining `EvaluateLoweredExpressionProgram(...)` helper is still an E4
bridge surface, but it now compiles standalone `ExpressionProgram` payloads to
standalone unified bytecode and executes `UnifiedBytecodeVirtualMachine`
directly. This keeps the source-level bridge visible while removing the old
runner-backed standalone expression execution path.

PR #3360 tightened the E4 source gate after the initial guard still used broad
file-level permission for `EvaluateLoweredExpressionProgram(...)` callers. That
was too coarse for files such as `TypedAstEvaluator.ExpressionPrograms.cs` and
`TypedAstEvaluator.SyncFunctionInvoker.cs`, where bridge definitions,
dynamic-boundary forwarding, class-field support, and fallback-only execution
can coexist.

## Decision

Keep standalone `ExpressionProgram` execution centralized in
`TypedAstEvaluator.ExpressionPrograms`, and keep that bridge on unified bytecode
rather than the IR runner.

- Normal callers should use `EvaluateLoweredExpressionProgram(...)` or
  `EvaluateDynamicExpressionProgram(...)`; direct calls to
  `ExecutionPlanRunner.EvaluateStandaloneExpressionProgram(...)` are tombstoned.
- `EvaluateLoweredExpressionProgram(...)` owns compiling standalone expression
  payloads to standalone unified bytecode and forwarding `newTarget` into
  `UnifiedBytecodeVirtualMachine.Execute` so constructor/class surfaces do not
  lose constructor metadata while using the bridge.
- Production routing code must not call
  `ExecutionPlanRunner.EvaluateStandaloneExpressionProgram(...)` directly, and
  that method must not be reintroduced.
- `ExecutionPlanRunner.ProfileEvaluateExpressionProgramLoop(...)` is
  tombstoned and must not be reintroduced; profiler cases that can compile
  standalone should execute through `UnifiedBytecodeVirtualMachine`.
- Remaining lowered expression-program call sites must stay source-gated and
  owner-classified. A new caller should name whether it is a
  class-definition/class-field surface, bridge helper, dynamic-boundary helper,
  or fallback-only path.
- Source gates should classify by role, not only by source file. When multiple
  roles can live in the same file, match the defining line or nearby marker
  that proves the specific bridge, dynamic-boundary, class-field, or
  fallback-only purpose.
- The simple-IR return expression path in `SyncFunctionInvoker` remains
  fallback-only. Production-accepted routes are considered first and return
  through `UnifiedBytecodeVirtualMachine` before this simple-IR fallback is
  considered.

## Consequences

- E4 can be tracked as a fallback-boundary guardrail instead of a vague
  "ExpressionProgram means AST fallback" bucket.
- Source-gate tests can prove direct standalone runner calls are absent while
  still allowing intended lowered payload execution through standalone unified
  bytecode.
- Future expression-bytecode refactors must update the bridge or the source
  gate classification when adding a new lowered expression-program caller.
- A file-level allowlist can hide role drift inside a legitimate owner file.
  Treat a new `EvaluateLoweredExpressionProgram(...)` line in an existing owner
  file as unclassified until its purpose is proven.
- If a future slice deletes the simple-IR fallback call, the guardrail should be
  moved from "fallback-only classified" to "tombstoned" rather than silently
  leaving an allowlist entry.
- If a future slice deletes any other quarantined helper, add or update the
  matching tombstone source gate in the same commit.

## Evidence

- Delivery PR #3271 merged as commit `4dd1f22ac`.
- The delivery changed:
  - `src/Asynkron.JsEngine/Ast/TypedAstEvaluator.ExpressionPrograms.cs`
  - `src/Asynkron.JsEngine/Ast/TypedAstEvaluator.SyncFunctionInvoker.cs`
  - `tests/Asynkron.JsEngine.Tests/ExecutionPlanDiagnosticsTests.cs`
- Focused guardrails added:
  - `SourceGate_E4_ProductionRoutes_DoNotCallStandaloneExpressionProgramEvaluator`
  - `SourceGate_E4_LoweredExpressionProgramCallers_AreClassified`
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

## Related

- `docs/rules/expression-bytecode-ast-seams.md`
- `docs/plans/bytecode-burndown-checklist.md`
