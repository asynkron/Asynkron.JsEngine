# ADR 0372: Cache ordinary expression payloads on owning AST nodes

## Status

Accepted, amended by the E4 dynamic executor retirement.

## Context

Issue
`planitem-gh3377-rebaseline-the-finite-bytecode-retirement-inventory-retire-fallba-066a53c85a`
and delivery PR #3479 retired the last ordinary
`UnifiedBytecodeExpressionProgramExecutor.ExecuteDynamic(...)` callers for
function parameter defaults and variable initializers.

Before the delivery, those payloads were ordinary AST-owned expression payloads,
but they still used the dynamic expression executor bridge. That made the E4
inventory harder to read: the bridge had to allowlist two non-legacy callers
beside the real dynamic residue in `Ast/Legacy/ExpressionNodeExtensions.cs`,
`Ast/Legacy/LoopPlanExtensions.cs`, and
`Ast/Legacy/StatementNodeExtensions.cs`.

The runtime already had a stronger path for already-lowered payloads:
`UnifiedBytecodeExpressionProgramExecutor.ExecuteStandalone(...)`. The missing
piece was ownership. Parameter defaults are owned by `FunctionParameter`, and
variable initializers are owned by `VariableDeclarator`; each node can cache
the lowered `ExpressionProgram` once and let its ordinary caller execute that
program without pretending the payload is dynamic.

Faktorial issue
`planitem-gh3495-shared-context-e4-expression-payload-retirement-retire-the-live-u-ad82fa8e92`
and PR #3513 generalized that ownership model to `ExpressionNode` itself. The
remaining legacy expression-node, statement, and loop operands had the same
value-only shape: they start from AST-owned expression operands, but when the
operand lowers successfully they should execute the cached lowered program
through standalone unified bytecode instead of a separate dynamic executor
cache.

## Decision

AST-owned expression payloads that can be lowered once should cache the lowered
`ExpressionProgram` on the owning AST node and execute through standalone
unified bytecode.

For parameter defaults and variable initializers, the accepted shape is:

- `FunctionParameter` and `VariableDeclarator` own the cached lowered
  `ExpressionProgram` payload;
- callers use the shared `LoweredExpressionProgramCache.Execute(...)` helper;
- `LoweredExpressionProgramCache.Execute(...)` routes through
  `UnifiedBytecodeExpressionProgramExecutor.ExecuteStandalone(...)`;
- the dynamic bridge source gate tombstones
  `ExecuteDynamic(...)` in `FunctionExpressionExtensions.cs` and
  `VariableKindExtensions.cs`;
- the standalone bridge source gate classifies the shared helper as
  `E4-cached-ordinary-expression-payloads`.

Do not reintroduce dynamic execution for ordinary parameter defaults or
variable initializers just because those payloads are syntactically stored on
AST nodes. After PR #3513, `ExpressionNode` itself owns the reusable lowered
program cache via `IAstCacheable<LoweredExpressionProgramCache>`, and
`LoweredExpressionProgramCache.ExecuteCached(...)` is the accepted value-only
legacy operand helper. Do not reintroduce
`UnifiedBytecodeExpressionProgramExecutor.ExecuteDynamic(...)`; it is a
tombstoned E4 bridge.

## Consequences

- The E4 dynamic expression bridge inventory is smaller and more precise:
  ordinary parameter-default and variable-initializer payloads are no longer
  mixed with legacy dynamic residue.
- Future ordinary payload migrations have one reusable helper shape instead of
  per-caller dynamic bridge usage.
- Source gates now protect both sides of the move: dynamic callers cannot
  reappear in the retired ordinary files, and standalone execution remains
  classified under the shared cache helper.
- The old legacy expression-node, statement, and loop operand residue now stays
  visible as `ExecuteDynamic(...)` source-absence ratchets rather than as an
  allowed open bridge.

## Evidence

- ADR allocation note: `rtk faktorial-api adr-next` was unavailable in this
  runtime (`No such file or directory`), so this learn pass used the Faktorial
  HTTP allocator endpoint `POST /api/adrs/next`, which returned
  `{"adr_id":372}`.
- Delivery PR #3479 merged as commit
  `b212c0a53088a7d7eb6d823e747a47f471296893`.
- Build-stage baseline signal: ordinary
  `UnifiedBytecodeExpressionProgramExecutor.ExecuteDynamic(...)` callers = 2
  (`FunctionExpressionExtensions.cs` and `VariableKindExtensions.cs`).
- Build-stage final signal: ordinary `ExecuteDynamic(...)` callers = 0;
  remaining legacy inventory = `ExpressionNodeExtensions.cs` 47,
  `LoopPlanExtensions.cs` 2, and `StatementNodeExtensions.cs` 10.
- Focused verification recorded by the delivery stage:
  - `rtk dotnet test tests/Asynkron.JsEngine.Tests --filter "FullyQualifiedName~ExecutionPlanDiagnosticsTests|FullyQualifiedName~AstFreeExecutionAssertionTests"`
    passed 145 tests;
  - `rtk jq . docs/plans/bytecode-proof-manifest.json` passed;
  - the runner AST seam scan found no `EvaluateExpression(` /
    `ProfileEvaluateExpression(` hits;
  - `rtk ./tools/profile forloop --memory` completed with total allocated
    968.34 MB.
- PR #3513 merged as commit `901ee8a809485e5383dbd464267ca6df5047bbfc` and
  redirected the remaining 59 legacy dynamic executor call sites to
  `LoweredExpressionProgramCache.ExecuteCached(...)`, then deleted
  `UnifiedBytecodeExpressionProgramExecutor.ExecuteDynamic(...)` and its
  executor-owned dynamic cache. Build-stage verification passed
  `rtk dotnet build src/Asynkron.JsEngine/Asynkron.JsEngine.csproj`, focused
  diagnostics/manifest tests, `AstFreeExecutionAssertionTests`,
  `rtk git diff --check`, and production source scans for the deleted bridge.

## Related

- `docs/rules/expression-bytecode-ast-seams.md`
- `docs/plans/bytecode-proof-manifest.json`
- `tests/Asynkron.JsEngine.Tests/ExecutionPlanDiagnosticsTests.cs`
- ADR 0345:
  `docs/adrs/0345-keep-standalone-expression-program-evaluation-behind-bridge.md`
