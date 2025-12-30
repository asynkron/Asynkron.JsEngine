# Unify ScriptRunner and ExecutionPlanRunner

## Problem Statement

We have two IR execution engines that duplicate significant logic:

| Runner | Lines | Purpose |
|--------|-------|---------|
| `ScriptRunner` | ~740 | Top-level script execution |
| `ExecutionPlanRunner` | ~2400+ | Function/generator/async execution |

The `ScriptRunner` exists because top-level code has different semantics for `var` declarations (must update global object), but it duplicates the entire instruction dispatch loop.

## Key Semantic Differences

### Variable Declaration Handling

**ScriptRunner** (line 226-230):
```csharp
// Script-level var: use AssignJsValue to update global object
environment.AssignJsValue(varDecl.TargetSymbol, initValue);
```

**ExecutionPlanRunner** (line 1011):
```csharp
// Function-level var: stays local
environment.DefineOrAssignJsValue(varDeclInstruction.TargetSymbol, varValue);
```

### Other Differences

| Aspect | ScriptRunner | ExecutionPlanRunner |
|--------|-------------|---------------------|
| Generator/async | Not supported | Full support |
| Slot initialization | Skipped | Full management |
| State objects | Local stacks only | Lazy state (AsyncState, YieldState, etc.) |
| Environment setup | None (pre-configured) | Full (arguments, this, super, etc.) |

## Solution: Instruction-Level Discrimination

Make the **instruction itself** carry the context. This avoids:
- Mode flags/enums on the runner
- Interface dispatch / vtable lookups
- Runtime polymorphism overhead

The check compiles to a simple branch - zero overhead beyond branch prediction.

---

## Implementation Plan

### Phase 1: Extend `SimpleVariableDeclarationInstruction`

**File:** `src/Asynkron.JsEngine/Execution/Instructions/SimpleVariableDeclarationInstruction.cs`

Add `IsScriptLevel` flag:

```csharp
public sealed record SimpleVariableDeclarationInstruction(
    int Next,
    Symbol TargetSymbol,
    ExpressionNode? Initializer,
    VariableKind VarKind,
    bool IsScriptLevel = false  // NEW: indicates top-level script context
) : ExecutionInstruction(InstructionKind.SimpleVariableDeclaration);
```

### Phase 2: Update ExecutionPlanBuilder for Script Mode

**File:** `src/Asynkron.JsEngine/Execution/ExecutionPlanBuilder.cs`

Add script mode parameter:

```csharp
private bool _isScriptLevel;

public static bool TryBuild(
    FunctionExpression function,
    out ExecutionPlan plan,
    out string? failureReason,
    bool reportDiagnostics = true,
    bool isScriptLevel = false)  // NEW
{
    var builder = new ExecutionPlanBuilder { _isScriptLevel = isScriptLevel };
    // ... rest unchanged
}
```

**File:** `src/Asynkron.JsEngine/Execution/Emitters/VariableDeclarationEmitter.cs` (or wherever var decl is emitted)

When emitting `SimpleVariableDeclarationInstruction`, pass `_isScriptLevel`:

```csharp
new SimpleVariableDeclarationInstruction(
    nextIndex,
    symbol,
    initializer,
    varKind,
    isScriptLevel: _isScriptLevel  // Pass through
)
```

### Phase 3: Update ScriptPlanCache

**File:** `src/Asynkron.JsEngine/Execution/ScriptPlanCache.cs`

Pass `isScriptLevel: true` when building:

```csharp
if (ExecutionPlanBuilder.TryBuild(
    syntheticFunction,
    out var plan,
    out var failureReason,
    reportDiagnostics: false,
    isScriptLevel: true))  // NEW
{
    return new ScriptPlanCache(plan, syntheticFunction, null);
}
```

### Phase 4: Unify Variable Handling in ExecutionPlanRunner

**File:** `src/Asynkron.JsEngine/Ast/TypedAstEvaluator.ExecutionPlanRunner.cs`

Update the `InstructionKind.SimpleVariableDeclaration` case:

```csharp
case InstructionKind.SimpleVariableDeclaration:
{
    var varDeclInstruction = Unsafe.As<SimpleVariableDeclarationInstruction>(instruction);
    // ... evaluate initializer (unchanged) ...

    if (varDeclInstruction.VarKind == VariableKind.Var)
    {
        environment.EnsureFunctionScopedVarBinding(varDeclInstruction.TargetSymbol, context);
        if (varDeclInstruction.Initializer is not null)
        {
            if (!environment.TryAssignBlockedBindingJsValue(varDeclInstruction.TargetSymbol, varValue))
            {
                if (varDeclInstruction.IsScriptLevel)
                {
                    // Script-level var: update global object too
                    environment.AssignJsValue(varDeclInstruction.TargetSymbol, varValue);
                }
                else
                {
                    // Function-level var: local binding only
                    environment.DefineOrAssignJsValue(varDeclInstruction.TargetSymbol, varValue);
                }
            }
        }
    }
    else
    {
        // let/const handling unchanged
        var isConst = varDeclInstruction.VarKind is VariableKind.Const or VariableKind.Using or VariableKind.AwaitUsing;
        environment.DefineJsValue(varDeclInstruction.TargetSymbol, varValue,
            isConst: isConst, isLexical: true, blocksFunctionScopeOverride: true);
    }

    _programCounter = varDeclInstruction.Next;
    continue;
}
```

### Phase 5: Add Static Script Entry Point

**File:** `src/Asynkron.JsEngine/Ast/TypedAstEvaluator.ExecutionPlanRunner.cs`

Add lightweight static method for script execution:

```csharp
/// <summary>
/// Runs an execution plan for script-level code.
/// This is a lightweight path that skips generator/async machinery setup.
/// The environment is already configured with hoisted declarations.
/// </summary>
public static JsValue RunScript(
    ExecutionPlan plan,
    JsEnvironment environment,
    EvaluationContext context)
{
    // Scripts don't need generator/async state - run directly
    var programCounter = plan.EntryPoint;
    var resultValue = JsValue.Undefined;
    var tryStack = new Stack<TryFrame>();
    var loopStack = new Stack<LoopFrame>();

    // Reuse the instruction dispatch loop
    // Either inline it here or extract to shared method
    return ExecuteInstructionLoop(
        plan,
        environment,
        context,
        ref programCounter,
        ref resultValue,
        tryStack,
        loopStack);
}
```

Alternative: Extract core loop to shared method:

```csharp
private static JsValue ExecuteInstructionLoop(
    ExecutionPlan plan,
    JsEnvironment environment,
    EvaluationContext context,
    ref int programCounter,
    ref JsValue resultValue,
    Stack<TryFrame> tryStack,
    Stack<LoopFrame> loopStack,
    // Optional state for generator/async - null for scripts
    AsyncState? asyncState = null,
    YieldState? yieldState = null)
{
    // Main switch statement logic
}
```

### Phase 6: Update ProgramNodeExtensions

**File:** `src/Asynkron.JsEngine/Ast/ProgramNodeExtensions.cs`

Replace `ScriptRunner.Run()` call with `ExecutionPlanRunner.RunScript()`:

```csharp
if (scriptPlanCache.Succeeded)
{
    try
    {
        // Use unified runner with script entry point
        var irResult = ExecutionPlanRunner.RunScript(
            scriptPlanCache.Plan,
            executionEnvironment,
            context);
        return irResult;
    }
    catch (NotSupportedException ex)
    {
        // Fall back to AST walking
        context.RealmState.Logger?.LogWarning(
            "Script IR execution fallback: {Reason}",
            ex.Message);
    }
}
```

### Phase 7: Delete ScriptRunner

**Files to delete:**
- `src/Asynkron.JsEngine/Ast/TypedAstEvaluator.ScriptRunner.cs`

**Optional cleanup:**
- Merge `ScriptPlanCache` into `ExecutionPlanCache` with a `ForScript()` factory method

---

## Module Support (Future)

ES Modules have additional semantics that can follow the same pattern:

| Semantic | Solution |
|----------|----------|
| `import` bindings | Add `ImportBindingInstruction` |
| `export` declarations | Add `ExportBindingInstruction` |
| Module namespace | Pass `ModuleNamespace` to runner |
| Top-level await | Already supported (async plan) |

Instructions carry the context, not the runner.

---

## Performance Considerations

The instruction flag approach has **zero runtime overhead**:

```csharp
// Just a bool field access + branch
if (varDeclInstruction.IsScriptLevel)
{
    environment.AssignJsValue(...);
}
else
{
    environment.DefineOrAssignJsValue(...);
}
```

- Bool is on same cache line as other instruction fields
- Single conditional branch (CPU branch prediction optimizes)
- Direct method calls (no vtable lookup)

Avoided alternatives:
- Interface dispatch (vtable lookup per call)
- Mode enum on runner (checked on every instruction)
- Handler delegates (indirect call overhead)

---

## Files Summary

| File | Change |
|------|--------|
| `Instructions/SimpleVariableDeclarationInstruction.cs` | Add `IsScriptLevel` field |
| `Execution/ExecutionPlanBuilder.cs` | Add `isScriptLevel` parameter |
| `Execution/Emitters/*.cs` | Pass `isScriptLevel` when emitting var decl |
| `Execution/ScriptPlanCache.cs` | Pass `isScriptLevel: true` |
| `Ast/TypedAstEvaluator.ExecutionPlanRunner.cs` | Handle `IsScriptLevel` flag, add `RunScript()` |
| `Ast/ProgramNodeExtensions.cs` | Call `RunScript()` instead of `ScriptRunner.Run()` |
| `Ast/TypedAstEvaluator.ScriptRunner.cs` | DELETE |

---

## Pre-Existing Test Failures

These tests are already failing before any unification work begins. Do not confuse with regressions:

| Test | Status |
|------|--------|
| `ForLoop_LexicalBindings_AreFreshPerIteration_ScopeBodyLexOpen` | Known failure |
| `DebugFunction_CapturesLoopInCallStack` | Known failure |

---

## Checklist

- [ ] Phase 1: Add `IsScriptLevel` to `SimpleVariableDeclarationInstruction`
- [ ] Phase 2: Add `isScriptLevel` parameter to `ExecutionPlanBuilder.TryBuild()`
- [ ] Phase 3: Update `ScriptPlanCache.Build()` to pass `isScriptLevel: true`
- [ ] Phase 4: Update `ExecutionPlanRunner` var handling to check `IsScriptLevel`
- [ ] Phase 5: Add `ExecutionPlanRunner.RunScript()` static method
- [ ] Phase 6: Update `ProgramNodeExtensions` to use `RunScript()`
- [ ] Phase 7: Delete `ScriptRunner.cs`
- [ ] Run tests to verify script execution still works
- [ ] Run benchmarks to verify no performance regression
