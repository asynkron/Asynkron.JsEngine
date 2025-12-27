# Sync IR Execution Path Investigation

**Date:** December 2024
**Status:** DISABLED - Needs more investigation
**Location:** `src/Asynkron.JsEngine/Ast/TypedAstEvaluator.TypedFunction.cs` (search for "DISABLED")

## Overview

The goal was to unify the execution model by using IR (Intermediate Representation) execution for ALL functions, not just generators and async functions. The async/generator IR path already works via `ExecutionPlanRunner`, so the idea was: "Why not use the same path for sync functions?"

### The Key Insight

User's original observation:
> "Don't we already have exactly this for async functions already? Wouldn't using the exact same work, we simply don't have any awaits?"

This led to adding `RunSync()` method to `ExecutionPlanRunner` that runs the plan synchronously and extracts the raw value from the iterator result.

## Implementation

### Files Modified

1. **`TypedAstEvaluator.ExecutionPlanRunner.cs`**
   - Added `RunSync()` method that:
     - Calls `ExecutePlan(ResumeMode.Next, JsValue.Undefined)`
     - Extracts the raw value from the iterator result object
     - Handles both `IteratorResultObject` (lightweight) and `JsObject` (full) cases

2. **`TypedAstEvaluator.TypedFunction.cs`**
   - Added sync IR execution path in `InvokeWithContextSlow`
   - Currently disabled with `if (false && ...)`
   - Skip conditions:
     - `_function.IsGenerator` - generators already use IR
     - `IsClassConstructor` - constructors have special semantics
     - `_homeObject is null` - class methods need super handling
     - `_allowIdentifierCache` - functions with eval/with need AST path
     - `RealmState.EnableFastPaths` - only when fast paths are enabled

3. **`LoopPlanExtensions.cs`**
   - Added comment explaining loop IR is now at function level
   - The AST loop fallback only runs when IR is disabled or failed

4. **`EnvironmentPoolingTests.cs`**
   - Relaxed pooling assertions since IR handles environments differently

## Known Failing Tests When Enabled

### 1. `Flatten_FlattensNestedArrays` - **HANGS**

**Test code:**
```javascript
function flatten(arr) {
    let result = [];
    for (let i = 0; i < arr.length; i = i + 1) {
        let item = arr[i];
        if (typeof item === 'object' && item.length !== undefined) {
            for (let j = 0; j < item.length; j = j + 1) {
                result.push(item[j]);
            }
        } else {
            result.push(item);
        }
    }
    return result;
}
```

**Analysis:**
- Nested for loops with `let` bindings
- Inner loop is conditionally executed (inside if-statement)
- The hang suggests an infinite loop where the counter increment gets lost
- Hypothesis: After inner loop exits, the environment chain is not correctly resolved for the outer loop's post-iteration expression

**Interesting finding:** Simple nested for loops (without if-else) PASS. The `NestedForLoop` test passes.

### 2. `ForLoop_UsesSlotFastPathWithoutMisses` - **FAILS**

**Error:** Slot read misses for identifiers `run`, `i`, `s`

**Analysis:**
- The slot-based identifier resolution optimization isn't working in IR path
- Environments aren't getting their slot metadata initialized correctly
- The IR path calls back to AST evaluator for some expressions, but slot info is lost

### 3. `WhileLoop_UsesSlotFastPathWithoutMisses` - **FAILS**

Same issue as #2 - slot resolution broken.

### 4. `ClassFieldInitializerCanAccessSuper` - **FAILS**

**Test code:**
```javascript
class Derived extends Base {
    field = eval('executed = true; () => super.read;');
}
```

**Analysis:**
- Arrow function created by `eval()` that accesses `super`
- The arrow function doesn't have `_homeObject` set directly
- But it needs to resolve `super.read` through the lexical chain
- The sync IR path skips functions with `_homeObject is null`, but this arrow function needs super bindings anyway

### 5. `ManualCpsLoop` - **FAILS**

Environment tracking issues similar to the nested loop problem.

## What Works

When sync IR is enabled, these pass:
- 33 basic ForLoop tests
- ForLoopPerIterationTests (3/3)
- NestedForLoop test (simple nested loops without conditionals)
- Sum tests and other npm package tests

This suggests the core loop IR works, but edge cases with:
- Conditional inner loops (if-else containing loops)
- Slot-based identifier caching
- Eval + super access

## Why Async IR Works But Sync IR Doesn't

The async IR path works because:
1. Yield/await suspension forces environment state to be carefully tracked
2. `_currentDriverState` is populated during async execution
3. Resume logic handles environment reconstruction

For sync execution:
1. No suspension points, so no `_currentDriverState`
2. Environment tracking relies on simpler logic that has edge case bugs
3. The `CreateIterationEnvironmentInstruction` handler's fallback cases don't handle all scenarios

## Root Cause Hypothesis

The `CreateIterationEnvironmentInstruction` handler (around line 929 in `ExecutionPlanRunner.cs`) has logic to find the correct `loopScope` and `previousIterEnv`:

```csharp
if (environment.ScopeId == createEnvInstruction.ScopeId) {
    // Case 1: Current env is a per-iteration env from previous iteration
} else if (_currentDriverState?.CurrentIterationEnvironment != null) {
    // Case 2: After async resume
} else if (_currentDriverState?.LoopScopeEnvironment != null) {
    // Case 2c: First iteration after async resume
} else {
    // Case 3: Walk up environment chain
}
```

For sync execution, `_currentDriverState` is always null, so we always fall through to Case 3. The environment chain walk may not correctly handle:
- Nested loops where inner loop env is still in `environment`
- Conditional loops where the if-statement creates intermediate scopes
- Loop exits that return to outer loop's post-iteration

## Next Steps to Fix

1. **Add sync-specific driver state** - Track current iteration environments for sync execution similar to async

2. **Fix environment chain after inner loop exit** - When inner loop exits, ensure `environment` is correctly updated or the walk logic handles this

3. **Initialize slot metadata in IR environments** - When `CreateIterationEnvironmentInstruction` creates new environments, ensure slot maps are properly set

4. **Handle eval-created functions with super** - These need the lexical super binding even though `_homeObject` is null on the function itself

## How to Re-Enable for Testing

In `TypedAstEvaluator.TypedFunction.cs`, find the big "DISABLED" banner and change:
```csharp
if (false && !_function.IsGenerator && ...
```
to:
```csharp
if (!_function.IsGenerator && ...
```

Then run:
```bash
dotnet test tests/Asynkron.JsEngine.Tests --filter "Flatten_FlattensNestedArrays|ForLoop_UsesSlotFastPathWithoutMisses"
```

## Test Commands

```bash
# Run all tests (should pass with IR disabled)
dotnet test tests/Asynkron.JsEngine.Tests

# Run just the failing tests
dotnet test tests/Asynkron.JsEngine.Tests --filter "FullyQualifiedName~Flatten_FlattensNestedArrays|FullyQualifiedName~ForLoop_UsesSlotFastPathWithoutMisses|FullyQualifiedName~WhileLoop_UsesSlotFastPathWithoutMisses|FullyQualifiedName~ClassFieldInitializerCanAccessSuper|FullyQualifiedName~ManualCpsLoop"

# Run with timeout for hanging tests
timeout 10 dotnet test tests/Asynkron.JsEngine.Tests --filter "Flatten_FlattensNestedArrays"
```

## Files to Study

- `TypedAstEvaluator.ExecutionPlanRunner.cs` - The IR executor, especially `CreateIterationEnvironmentInstruction` handling
- `Execution/ExecutionPlanBuilder.cs` - How loops are compiled to IR
- `Execution/LoopNormalizer.cs` - How for/while/do-while are normalized to LoopPlan
- `Execution/LoopPlan.cs` - The loop plan structure with per-iteration bindings
