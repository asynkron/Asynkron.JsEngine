# Expression Bytecode Migration Report (2026-05-22)

Plan lineage: Part 7 - Final proof and migration decision (`issue #1542`)

## Why this report exists
- Story 1: give maintainers a current, concrete view of what expression bytecode already covers and which AST/runtime dependencies still remain.
- Story 2: give performance-focused follow-up work one safe next slice with measurable baseline evidence, while avoiding high-risk semantic bundles.

## Completed bytecode coverage (what is already in place)

Evidence surfaces:
- Coverage map: `docs/expression-bytecode-coverage.md`
- Compiler dispatch and failure classification: `src/Asynkron.JsEngine/Execution/Instructions/ExpressionProgramCompiler.cs`
- Bytecode op inventory: `src/Asynkron.JsEngine/Execution/Instructions/ExpressionOp.cs`
- Source-gate tests: `tests/Asynkron.JsEngine.Tests/ExpressionProgramCoverageMapTests.cs`, `tests/Asynkron.JsEngine.Tests/ExecutionPlanDiagnosticsTests.cs`

Current shape summary:
- `ExpressionOpKind` already spans literal/identifier loads, call-target setup, object/array construction, property get/set/update/delete families, unary/binary ops, control flow, and invocation.
- The coverage map tracks concrete `ExpressionNode` families as:
  - directly supported,
  - shape-dependent with classified failure buckets,
  - unsupported or intentionally not compiled directly (lowered/rerouted seams like `await`/`yield`).
- Failure taxonomy is explicit and test-guarded via `ExpressionProgramFailureCode` buckets instead of broad opaque failures.

Focused proof run (current worktree):
- `rtk dotnet test tests/Asynkron.JsEngine.Tests/Asynkron.JsEngine.Tests.csproj --filter "FullyQualifiedName~ExpressionProgramCoverageMapTests"`
  - Result: passed (`1` test)

## Remaining AST/runtime dependencies

### A. Unwanted runner AST-eval seams (target: absent)
Command:
- `rtk rg "EvaluateExpression\\(|ProfileEvaluateExpression\\(" src/Asynkron.JsEngine/Ast/TypedAstEvaluator.ExecutionPlanRunner*`

Result:
- No matches (exit status `1` from `rg`), which is the expected "not found" signal.

Interpretation:
- No direct `ExecutionPlanRunner` AST-eval seams (`EvaluateExpression` / `ProfileEvaluateExpression`) are currently present in the scanned runner files.

### B. Approved/expected legacy-dynamic boundaries (still present)
These are acceptable boundaries for now and should stay explicit in planning:
- Shape-dependent compile failures routed through `ExpressionProgramCompileFailure` + `ExpressionProgramFailureCode` classifications.
- Lowering/IR seams for expressions that are intentionally not compiled directly yet (for example dedicated awaited/yield instruction paths).
- Unsupported bucket paths preserved by diagnostics tests rather than hidden fallbacks.

Focused source-gate proof run (current worktree):
- `rtk dotnet test tests/Asynkron.JsEngine.Tests/Asynkron.JsEngine.Tests.csproj --filter "FullyQualifiedName~ExecutionPlanDiagnosticsTests.ExpressionProgramFailureClassification_CoversCurrentBacklogBuckets|FullyQualifiedName~ExecutionPlanDiagnosticsTests.DetailedSnapshot_UnsupportedExpressionProgramBuckets_MatchRepresentativeProbe"`
  - Result: passed (`2` tests)

Build-stage refresh (issue `#1545`, 2026-05-22):
- `rtk dotnet test tests/Asynkron.JsEngine.Tests --filter "FullyQualifiedName~ExecutionPlanDiagnosticsTests.ExpressionProgramFailureClassification_CoversCurrentBacklogBuckets|FullyQualifiedName~ExecutionPlanDiagnosticsTests.DetailedSnapshot_UnsupportedExpressionProgramBuckets_MatchRepresentativeProbe"`
  - Result: passed (`2` tests, `2.1 s`)
- Recommendation check: static/literal property-name normalization remains the safest next slice.
- Deferred high-risk groups remain out of scope for this issue: optional-chain/super interaction buckets, update-target and compound-assignment ordering-sensitive buckets, delete-target semantics, and broad `UnsupportedExpressionNode` burn-down.

## Performance and storage deltas

Baseline reference:
- `docs/expression-bytecode-baseline-2026-05-22.md`

Refreshed storage diagnostics (current worktree):
- Command: `rtk dotnet test tests/Asynkron.JsEngine.Tests/Asynkron.JsEngine.Tests.csproj --filter "FullyQualifiedName~ExpressionProgramStorageDiagnosticsTests"`
  - Result: passed (`11` tests)
- Command: `rtk dotnet run --project tools/ProfileRunner/ProfileRunner.csproj -c Release -- forloop --expression-program-storage`
  - `programs=7`
  - `total_ops=10`
  - `encoded_op_estimated_bytes=80`
  - `packed_op_shape`: `flags=1`, `immediate0=10`, `immediate1=1`, `both_immediates=1`
  - constants: `literals=4`, `strings=0`, `objects=0`, `identifiers=7`, `spread_masks=0`
  - side-state estimates: `max_stack_slots=9`, `stack_value_bytes=216`, `stack_flag_words=7`, `stack_flag_bytes=56`
  - op histogram: `LoadLiteral=4`, `LoadIdentifier=3`, `LoadIdentifierCallTarget=1`, `Binary=1`, `Call=1`

Refreshed allocation baseline (current worktree):
- Command: `rtk ./tools/profile forloop --memory`
  - `total allocated = 7.05 MB`
  - top sampled owners remain led by `JsValue[]`, `String`, `PropertyDescriptor`

Delta summary vs same-day baseline doc:
- Storage footprint metrics are stable and materially unchanged in shape.
- Total allocated memory remains `7.05 MB`.
- No regression signal in this focused proof loop.

## Next safe bytecode expansion slice (single recommendation)

Recommended next slice:
- Static/literal property-name normalization bucket group:
  - `UnsupportedStaticObjectPropertyName`
  - `InvalidComputedObjectKey`
  - `UnsupportedDotAccessPropertyName`
  - `UnsupportedDirectMemberCallPropertyName`
  - `UnsupportedTaggedTemplateMemberAccessName`

Why this slice:
- Classification/normalization-focused and already isolated in backlog evidence.
- Lower semantic risk than optional-chain/super/update-target/delete/compound-assignment families.
- Strongly aligned with existing failure taxonomy and source-gate tests, enabling narrow proof-first implementation.

Deferred high-risk groups (explicitly not in this slice):
- `super` and optional-chain interaction buckets
- update-target and compound-assignment ordering-sensitive buckets
- broad `UnsupportedExpressionNode` catch-all burn-down

## Build proof checklist (AC-6 evidence)
- Coverage-map test: passed (`ExpressionProgramCoverageMapTests`)
- Storage diagnostics test: passed (`ExpressionProgramStorageDiagnosticsTests`)
- Unsupported/source-gate diagnostics tests: passed (2 targeted `ExecutionPlanDiagnosticsTests`)
- AST seam scan: no `EvaluateExpression` / `ProfileEvaluateExpression` matches in runner files
- Profiling command: `./tools/profile forloop --memory` succeeded with `7.05 MB` total allocated

## Notes
- This issue intentionally delivers documentation/evidence synthesis only; it does not implement new bytecode support.
- One transient build warning occurred during profiling (`MSB3026` retry on `Asynkron.JsEngine.dll` copy); build completed and profiling output was produced successfully.
