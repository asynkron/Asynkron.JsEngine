# Performance: forofiteration — Cache SyncFunctionInvoker static analysis per FunctionExpression

## Summary

Eliminated repeated AST tree traversals in `SyncFunctionInvoker..ctor` by caching the results
as a `FunctionInvokerStaticPlan` on the `FunctionExpression` node. Combined with a secondary
fix removing a redundant `DefineOrAssignJsValue` call in the SyncIterator StoreValue hot path,
the `forofiteration` benchmark improved ~15% (487ms → 413ms median).

## Baseline signal

Baseline timestamp: 2026-05-30T04:49:00Z
Baseline signal: forofiteration asynkron_ms = 487 (median of 445, 487, 524 across 3 runs)
Baseline Jint: 301ms median

## Final signal

Final timestamp: 2026-05-30T05:02:53Z
Final signal: forofiteration asynkron_ms = 413 (median of 408, 413, 413, 418 across 4 runs, 678ms outlier excluded)
Final Jint: ~280ms (stable)
Signal delta: −74ms / −15.2% improvement

## Root cause analysis

### Hot path profile (speedscope, forofiteration, 2000 IIFE iterations)

The `forofiteration` benchmark executes this IIFE 2000 times:

```js
(function() {
    let sum = 0;
    let arr = [1,2,...,10, ...×14];
    for (const n of arr) { sum += n; }
    return sum;
})();
```

Profile showed `SyncFunctionInvoker..ctor` = 60.7ms total, dominated by:

```
57.4ms  SyncFunctionInvoker.ContainsParameterVarDeclarationWithoutInitializer
         └── allocates new Stack<StatementNode> + traverses AST body per invocation
```

Two other traversals also ran per-invocation:
- `ContainsFunctionDeclarationParameterConflict` (via `ScopeDynamicnessAnalyzer`)
- `ContainsNonParameterCalleeIdentifier` (via `ScopeDynamicnessAnalyzer`)
- `ContainsInnerFunctionExpression` (×3 per constructor, though the BlockStatement already caches this one)

All four return values are purely determined by the function's immutable AST — they do not
depend on closure, realm, or runtime state. Re-computing them on every invocation was waste.

### Fix 1: FunctionInvokerStaticPlan (main win)

Added `FunctionInvokerStaticPlan` cached on `FunctionExpression` via the `IAstCacheable<T>`
pattern. The plan computes all four booleans once, on first invocation, and every subsequent
invocation reads the cached result directly:

```csharp
var invokerStatics = ((IAstCacheable<FunctionInvokerStaticPlan>)_function).GetOrCreateCache();
_hasFunctionDeclarationParameterConflict = invokerStatics.HasFunctionDeclarationParameterConflict;
_hasParameterVarDeclarationWithoutInitializer = invokerStatics.HasParameterVarDeclarationWithoutInitializer;
_hasNonParameterCalleeCall = invokerStatics.HasNonParameterCalleeCall;
// ...and uses invokerStatics.HasInnerFunctionExpression instead of ContainsInnerFunctionExpression(function)
```

`ContainsParameterVarDeclarationWithoutInitializer` was moved from private `SyncFunctionInvoker`
to `ScopeDynamicnessAnalyzer` where the other similar methods live.

### Fix 2: Remove redundant DefineOrAssignJsValue in SyncIterator StoreValue

When `valueVar.IsValid` in the SyncIterator StoreValue instruction handler,
`valueVar.Write(currentValue)` already performs the write. The subsequent
`valueVar.Environment.DefineOrAssignJsValue(instruction.ValueSlot, currentValue)` was a
redundant dictionary write per for...of iteration. Removed.

## Files changed

- `src/Asynkron.JsEngine/Ast/FunctionInvokerStaticPlan.cs` — new plan class
- `src/Asynkron.JsEngine/Ast/FunctionExpression.cs` — added `IAstCacheable<FunctionInvokerStaticPlan>`
- `src/Asynkron.JsEngine/Ast/ScopeDynamicnessAnalyzer.cs` — moved `ContainsParameterVarDeclarationWithoutInitializer` here as `internal static`
- `src/Asynkron.JsEngine/Ast/TypedAstEvaluator.SyncFunctionInvoker.cs` — use cached plan, removed dead private method, remove redundant `DefineOrAssignJsValue`

## Proof commands

```bash
# Baseline (without changes, run 3× and take median):
git stash && ./benchmark.sh forofiteration

# Final (with changes, run 3× and take median):
git stash pop && dotnet build src/Asynkron.JsEngine -c Release && ./benchmark.sh forofiteration

# Smoke set (no regressions):
./benchmark.sh --smoke

# Internal tests:
dotnet test tests/Asynkron.JsEngine.Tests -c Release
```

## Smoke set (no regressions)

| profile           | asynkron_ms | jint_ms | delta                   |
|-------------------|-------------|---------|-------------------------|
| fib               | 2           | 891     | Asynkron 445.50x faster |
| forloop           | 2996        | 3371    | Asynkron 1.13x faster   |
| ir-arithmetic     | 1281        | 980     | Jint 1.31x faster       |
| functioncalls     | 1808        | 2238    | Asynkron 1.24x faster   |
| functioncalls-lite| 453         | 433     | Tie                     |
