# JsValue Usage and Overload Patterns

## Replace `object` with `JsValue`
- Many bugs come from passing untyped `object` values; prefer `JsValue` for JavaScript values.
- When a method receives `object`, update it to accept `JsValue` directly instead of adding guards/casts.

## Evaluator Overload Pattern (avoid boxing)
Add `JsValue`-returning overloads to hot evaluator methods.

### Pattern
1. Keep existing method for compatibility:
```csharp
private object? EvaluateBlock(...)
{
    var (jsResult, hasJsResult, objResult) = EvaluateBlockCore(...);
    return hasJsResult ? jsResult.ToObject() : objResult;
}
```
2. Add `JsValue` overload for hot paths:
```csharp
private JsValue EvaluateBlockJsValue(...)
{
    var (jsResult, hasJsResult, objResult) = EvaluateBlockCore(...);
    return hasJsResult ? jsResult : JsValue.FromObject(objResult);
}
```
3. Extract shared core returning both forms:
```csharp
private (JsValue jsResult, bool hasJsResult, object? objResult) EvaluateBlockCore(...)
{
    // track JsValue separately from object results
}
```

### Apply Here
- `EvaluateStatement` (StatementNodeExtensions.cs)
- `EvaluateBlock` (BlockStatementExtensions.cs)
- `EvaluateIf` (IfStatementExtensions.cs)
- `EvaluateExpression` already returns `JsValue`

### Loop Usage
```csharp
var lastValueJs = JsValue.Undefined;
while (true)
{
    lastValueJs = EvaluateStatementJsValue(plan.Body, iterationEnvironment, context, loopLabel);
}
return lastValueJs.ToObject();
```

### Fast Path in `EvaluateStatementJsValue`
```csharp
switch (statement)
{
    case BlockStatement block: return EvaluateBlockJsValue(block, env, ctx);
    case ExpressionStatement expr: return EvaluateExpression(expr.Expression, env, ctx);
    case IfStatement ifStmt: return EvaluateIfJsValue(ifStmt, env, ctx);
}
var result = EvaluateStatement(statement, env, ctx, activeLabel);
return JsValue.FromObject(result);
```

### Impact (ForLoop benchmark)
- `let` loops (50k): allocations 4.99 MB → 3.84 MB (~23% reduction)
- `var` loops (100k): allocations 29.52 MB → 27.24 MB (~8% reduction)
- Execution time improved ~19% for `let` loops
