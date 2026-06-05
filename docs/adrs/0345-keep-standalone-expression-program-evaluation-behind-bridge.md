# ADR 0345: Keep standalone ExpressionProgram evaluation behind bridge

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
class-definition/class-field support, profiling, dynamic-boundary work, or a
fallback-only surface.

## Decision

Keep direct standalone `ExpressionProgram` runner calls centralized in
`TypedAstEvaluator.ExpressionPrograms`.

- Normal callers should use `EvaluateLoweredExpressionProgram(...)` or
  `EvaluateDynamicExpressionProgram(...)` instead of calling
  `ExecutionPlanRunner.EvaluateStandaloneExpressionProgram(...)` directly.
- `EvaluateLoweredExpressionProgram(...)` owns forwarding `newTarget` to the
  standalone runner so constructor/class fallback surfaces do not lose
  constructor metadata while using the bridge.
- Production routing code must not call
  `ExecutionPlanRunner.EvaluateStandaloneExpressionProgram(...)` or
  `ExecutionPlanRunner.ProfileEvaluateExpressionProgramLoop(...)` directly.
- Remaining lowered expression-program call sites must stay source-gated and
  owner-classified. A new caller should name whether it is a
  class-definition/class-field surface, bridge/profiling helper,
  dynamic-boundary helper, or fallback-only path.
- The simple-IR return expression path in `SyncFunctionInvoker` remains
  fallback-only. Production-accepted routes are considered first and return
  through `UnifiedBytecodeVirtualMachine` before this simple-IR fallback is
  considered.

## Consequences

- E4 can be tracked as a fallback-boundary guardrail instead of a vague
  "ExpressionProgram means AST fallback" bucket.
- Source-gate tests can prove direct standalone runner calls stay out of
  production routing code while still allowing intended lowered payload
  execution.
- Future expression-bytecode refactors must update the bridge or the source
  gate classification when adding a new lowered expression-program caller.
- If a future slice deletes the simple-IR fallback call, the guardrail should be
  moved from "fallback-only classified" to "tombstoned" rather than silently
  leaving an allowlist entry.

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

## Related

- `docs/rules/expression-bytecode-ast-seams.md`
- `docs/plans/bytecode-burndown-checklist.md`
