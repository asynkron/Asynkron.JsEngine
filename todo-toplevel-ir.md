# Top-Level IR Compilation

## Current State

The engine currently has a split execution model:

| Code Location | Execution Method |
|---------------|------------------|
| Top-level script statements | AST walking (`EvaluateStatementJsValue`) |
| Simple sync functions | AST walking (fast path via `EvaluateBlockJsValue`) |
| Complex sync functions | IR via `ExecutionPlanRunner` |
| Generators | IR via `ExecutionPlanRunner` |
| Async functions | IR via `ExecutionPlanRunner` |
| Async generators | IR via `ExecutionPlanRunner` |

Top-level code in `ProgramNodeExtensions.cs` iterates through statements and evaluates each via AST walking:

```csharp
foreach (var statement in program.Body)
{
    var completionJs = statement.EvaluateStatementJsValue(executionEnvironment, context);
    // ...
}
```

## Proposed Change

Treat the entire script as a synthetic function body and IR-compile everything:

```javascript
// This top-level script:
let x = 1;
for (let i = 0; i < 1000; i++) {
  x += i;
}
console.log(x);

// Would be conceptually treated as:
(function __script__() {
  let x = 1;
  for (let i = 0; i < 1000; i++) {
    x += i;
  }
  console.log(x);
})();
```

## Benefits

1. **Unified execution path** - Single code path to maintain instead of AST + IR split
2. **Top-level loop optimization** - Hot loops at script level get IR benefits
3. **Consistent performance** - No surprising performance cliffs between top-level and function code
4. **Simpler architecture** - ExecutionPlanRunner becomes the single execution engine

## Implementation Considerations

### Script vs Function Semantics

The IR builder needs to distinguish "script body" from "function body":

| Aspect | Function Body | Script Body |
|--------|---------------|-------------|
| `var` declarations | Create environment bindings | Create properties on `globalThis` |
| `this` value | Depends on call | `globalThis` (or `undefined` in strict) |
| `return` statement | Valid, exits function | Syntax error |
| Hoisting scope | Function scope | Global scope |

### Global Object Integration

`var` at top-level must:
1. Check for conflicting lexical declarations
2. Create a property on `globalThis` (not just an environment binding)
3. Handle `configurable: true` for deletability

Could add a `ScriptVarDeclareInstruction` that does the global property dance.

### Module Support

For ES modules, additional handling needed:
- `import` declarations (static, hoisted)
- `export` declarations (namespace object)
- Top-level `await` (already requires async-style IR)

### Direct Eval

Direct `eval()` can inject declarations into the current scope. The IR would need to:
- Detect potential direct eval calls
- Either fall back to AST walking for that scope, or
- Generate IR that can handle dynamic scope modification

### Error Handling

Top-level `return` should produce a syntax error at parse time, not runtime. This is already handled by the parser, so IR compilation can assume no top-level returns.

## Migration Path

1. **Phase 1**: Add `ScriptExecutionPlanBuilder` that wraps `ExecutionPlanBuilder` with script-specific semantics
2. **Phase 2**: Handle `var` hoisting to global object
3. **Phase 3**: Add fallback for unsupported constructs (direct eval with scope injection)
4. **Phase 4**: Remove AST-walking code path from `ProgramNodeExtensions`
5. **Phase 5**: Optimize - since everything is IR, can do cross-statement optimizations

## Open Questions

- Should we keep AST walking as a fallback for IR compilation failures?
- How to handle REPL-style incremental evaluation (each line is a new "script")?
- Performance impact of IR compilation overhead for small scripts?

## Files to Modify

- `src/Asynkron.JsEngine/Ast/ProgramNodeExtensions.cs` - Entry point
- `src/Asynkron.JsEngine/Execution/ExecutionPlanBuilder.cs` - Add script mode
- `src/Asynkron.JsEngine/Execution/Instructions/` - New instructions for global var handling
- `src/Asynkron.JsEngine/Ast/TypedAstEvaluator.ExecutionPlanRunner.cs` - Script execution support
