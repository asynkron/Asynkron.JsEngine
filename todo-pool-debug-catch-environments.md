# Investigation Report: Pool Debug Guards Fail for Catch Environments

## Problem Summary
~27 tests fail with "Pooled object returned without an active lease" because catch environments are created with `new JsEnvironment()` but returned to pool via `JsEnvironmentPool.Return()`, which triggers `PoolDebug.MarkReturned()` to throw since the object was never leased.

## Affected Components
- `src/Asynkron.JsEngine/Ast/TypedAstEvaluator.ExecutionPlanRunner.Handlers.TryCatch.cs` (lines 124-129, 163-168)
- `src/Asynkron.JsEngine/Ast/TypedAstEvaluator.ExecutionPlanRunner.Handlers.Scope.cs` (line 262)
- `src/Asynkron.JsEngine/PoolDebug.cs` (lines 28-36)
- `src/Asynkron.JsEngine/ObjectPool.cs` (line 56)
- `src/Asynkron.JsEngine/Execution/Emitters/TryEmitter.cs` (line 127)

## Evidence Collected

### Test Output
All 27 failing tests show identical stack traces:
```
System.InvalidOperationException : Pooled object returned without an active lease.
   at Asynkron.JsEngine.PoolDebug.MarkReturned(Object item) in PoolDebug.cs:line 32
   at Asynkron.JsEngine.Ast.TypedAstEvaluator.ExecutionPlanRunner.HandlePopEnvironment(...) in Handlers.Scope.cs:line 262
   ...
```

### Log Analysis
All failing tests show the same pattern just before failure:
```
[RealmLogger] Information: JsEnvironment.Reset description=catch
```
The "catch" description confirms a catch environment is being reset/returned.

### Code Analysis

**TryEmitter.cs:127** - Emits PopEnvironmentInstruction with `AllowPooling=false`:
```csharp
var popCatchEnv = ctx.Append(new PopEnvironmentInstruction(catchScopeId, false, leaveTryIndex));
```

**HandleEnterCatch (lines 124-129)** - Creates environment directly, NOT from pool:
```csharp
var catchEnv = new JsEnvironment(
    environment,
    false,
    environment.IsStrict,
    null,
    "catch");
```

**HandleEnterCatchWithDestructuring (lines 163-168)** - Same issue:
```csharp
var catchEnv = new JsEnvironment(
    environment,
    false,
    environment.IsStrict,
    null,
    "catch");
```

**HandlePopEnvironment (line 262)** - IGNORES instruction.AllowPooling:
```csharp
if (shouldPop)
{
    var envToPop = environment;
    environment = environment.Enclosing!;

    // Always return to pool - pool ignores captured environments
    JsEnvironmentPool.Return(envToPop, runner._realmState.Logger);  // <-- BUG!
}
```

**PopEnvironmentInstruction definition** - Has AllowPooling parameter that is ignored:
```csharp
internal sealed record PopEnvironmentInstruction(int ScopeId, bool AllowPooling, int Next)
```

### Pool Debug Flow
1. `ObjectPool.Rent()` calls `PoolDebug.MarkLeased(item)` - marks object as leased
2. `ObjectPool.Return()` calls `PoolDebug.MarkReturned(item)` - expects object to be leased
3. For catch environments: step 1 never happens, but step 2 is called

## Root Cause Analysis

### Hypothesis 1 (Most Likely): HandlePopEnvironment ignores AllowPooling flag

The `PopEnvironmentInstruction` includes an `AllowPooling` boolean parameter that is explicitly set to `false` for catch environments in `TryEmitter.cs`. However, `HandlePopEnvironment` completely ignores this flag and unconditionally calls `JsEnvironmentPool.Return()`.

**Evidence supporting:**
- TryEmitter.cs:127 emits `PopEnvironmentInstruction(catchScopeId, false, ...)`
- The comment at line 260-261 says "Always return to pool - pool ignores captured environments" but this is incorrect - it should also skip non-pooled environments
- The instruction has the `AllowPooling` property specifically for this purpose

**Evidence against:**
- None - this is clearly the bug

### Hypothesis 2: Missing MarkLeasedDebug call for catch environments

Even if we fix HandlePopEnvironment to respect AllowPooling, there's a second consideration: should catch environments be pooled?

The pattern in `HandlePushEnvironment` (lines 131-137) shows proper handling:
```csharp
var newIterationEnv = allowPooling
    ? JsEnvironmentPool.Rent(...)
    : new JsEnvironment(...);
if (!allowPooling)
{
    newIterationEnv.MarkLeasedDebug();  // <-- This handles non-pooled case
}
```

But in `HandleEnterCatch`, if we DO want to pool catch environments:
- We would need to rent from pool instead of `new`
- We would need to call `MarkLeasedDebug()` for non-pooled case

## Recommended Fix

### Option A: Respect AllowPooling flag in HandlePopEnvironment (Quick Fix)

**Step-by-step:**
1. In `HandlePopEnvironment`, check `instruction.AllowPooling` before returning to pool
2. Only call `JsEnvironmentPool.Return()` when `instruction.AllowPooling` is true

```csharp
private static InstructionResult HandlePopEnvironment(
    ExecutionPlanRunner runner,
    ExecutionInstruction instr,
    ref JsEnvironment environment,
    EvaluationContext ctx,
    out JsValue returnValue)
{
    var instruction = Unsafe.As<PopEnvironmentInstruction>(instr);
    var shouldPop = instruction.ScopeId >= 0
        ? environment.ScopeId == instruction.ScopeId
        : environment.Description is "scope" or "loop-scope" && environment.Enclosing != null;

    if (shouldPop)
    {
        var envToPop = environment;
        environment = environment.Enclosing!;

        // Only return to pool if AllowPooling is true
        if (instruction.AllowPooling)
        {
            JsEnvironmentPool.Return(envToPop, runner._realmState.Logger);
        }
    }

    runner._programCounter = instruction.Next;
    returnValue = default;
    return InstructionResult.Continue;
}
```

- **Pros:** Simple one-line change, minimal risk
- **Cons:** Catch environments won't be pooled (may have minor allocation overhead)

### Option B: Enable pooling for catch environments (Full Fix)

**Step-by-step:**
1. Modify `TryEmitter.cs` to emit `PopEnvironmentInstruction` with `AllowPooling=true`
2. Modify `HandleEnterCatch` and `HandleEnterCatchWithDestructuring` to rent from pool
3. Keep Option A fix as well (defense in depth)

```csharp
// In HandleEnterCatch:
using var catchEnvPooled = JsEnvironmentPool.Rent(
    environment,
    false,
    environment.IsStrict,
    null,
    "catch",
    logger: runner._realmState.Logger);
var catchEnv = catchEnvPooled.Value!;
// ... rest of setup ...
```

- **Pros:** Better memory efficiency, consistent with other environments
- **Cons:** More complex change, need to handle the Pooled<T> lifecycle carefully

## Test Plan
- [ ] Verify fix resolves all 27 original failing tests
- [ ] Run full test suite: `dotnet test tests/Asynkron.JsEngine.Tests --no-build`
- [ ] Run Test262 suite: `dotnet test tests/Asynkron.JsEngine.Tests.Test262 --no-build`
- [ ] Check for regressions in try/catch/finally behavior
- [ ] Verify generators with try/catch still work correctly

## Additional Notes

### List of All 27 Failing Tests (all same root cause)
All tests involve try/catch constructs directly or indirectly (e.g., via assert.throws helper):
- TryCatchFinally_SimplestReturnFromFinally
- Generator_TryCatchHandlesThrowIr
- CatchParameterPassedToFunctionCall (and StrictMode variant)
- CatchParameterShadowsVarVariable
- AssertThrowsPattern_ShouldCatchErrorObject
- ArgumentsCalleeThrowsInStrictMode
- NestedFunctionCatch_TypeofThrownError
- StrictMode_BlockFunctionDeclaration_ShouldNotHoistToFunctionScope (3 variants)
- ClosureTdz_WithAssertThrowsPattern
- ClosureTdz_AssignBeforeInit_ShouldThrowReferenceError
- ForOfResizableTypedArray_JsSmoke
- AssertThrows_Works_With_GeneratorCallerAccess
- AsyncGenerator_TryCatchWithThrow
- SwitchTest1_SwitchInsideTryWithFinallyReturn
- TryFinally_BlockScopeShadowing_CorrectlyRestored
- TryCatchFinally_SwitchInsideTry_ReturnFromFinally
- Layer2_RegularForOf_TryCatchWorks
- Generator_ThrowDeliversExceptionToYield
- For_ForAwaitOf_For_Mixed
- ForAwaitOf_For_ForAwaitOf_Mixed
- ForAwaitOf_DoubleNestedFor
- DoubleNestedForAwaitOf_For_Inner
- ArrayPrototypeLastIndexOfResizableGrowthMatchesTest262Ctors
- TypedArrayLastIndexOfResizableGrowthMatchesTest262Ctors

### Related Commits
- `bf1c3645` - Added PoolDebug assertions (introduced the guard that now catches this bug)
- `bad9f2d8` - Hardened pool guards but only for HandlePushEnvironment, not HandleEnterCatch

### Why This Wasn't Caught Before
The PoolDebug guards were added recently in DEBUG builds. The actual bug (not respecting AllowPooling) existed before, but:
1. Without guards, returning a non-rented object just worked silently (the object got pooled or GC'd)
2. The guards now correctly detect this contract violation
