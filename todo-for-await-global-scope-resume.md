# Investigation Report: For-await-of Loop Fails to Resume After Completion (Global Scope)

## Problem Summary
The `for await...of` loop correctly iterates through all items and receives `done: true` from the iterator, but code AFTER the loop never executes when the iterable is declared in global/module scope. The loop works correctly when the iterable is declared in local scope (inside the async function).

## Affected Components
- `/Users/rogerjohansson/git/asynkron/Asynkron.JsEngine/src/Asynkron.JsEngine/Ast/TypedAstEvaluator.ExecutionPlanRunner.cs` (lines 650-950) - `HandleAsyncIteratorMoveNext`
- `/Users/rogerjohansson/git/asynkron/Asynkron.JsEngine/src/Asynkron.JsEngine/Ast/TypedAstEvaluator.AsyncFunctionInvoker.cs` - async function driver
- `/Users/rogerjohansson/git/asynkron/Asynkron.JsEngine/src/Asynkron.JsEngine/Execution/Emitters/ForOfEmitter.cs` - IR generation for for-await-of
- `/Users/rogerjohansson/git/asynkron/Asynkron.JsEngine/tests/Asynkron.JsEngine.Tests/AsyncIterableScopeComparisonTests.cs` - failing test

## Evidence Collected

### Test Output
```
[LOCAL] LOCAL: returning done=true
[LOCAL] LOCAL: After loop, result=xyz      <-- CODE AFTER LOOP EXECUTES
[LOCAL] Final result: 'Asynkron.JsEngine.JsTypes.JsObject'

[GLOBAL] GLOBAL: returning done=true
[GLOBAL] GLOBAL: About to start for-await-of   <-- IMMEDIATELY JUMPS TO SECOND test() CALL
                                               <-- "After loop" NEVER PRINTED
[GLOBAL] Final result: 'Asynkron.JsEngine.JsTypes.JsObject'
```

### Snapshot Analysis
- **LOCAL scope**: 5 snapshots captured (before loop, 3 iterations, after loop)
- **GLOBAL scope**: 4 snapshots captured (before loop, 3 iterations, NO after loop)

### Code Flow When `done=true` Is Received

1. `HandleAsyncIteratorMoveNext` (line 813-832) detects `done=true`:
   ```csharp
   var doneAwait = awaitResultObj.TryGetProperty("done", out var awaitDoneValue) &&
                   JsOps.ToBoolean(awaitDoneValue);
   if (doneAwait)
   {
       // Cleanup code...
       IteratorStateRef.CurrentDriverState = null;
       _programCounter = instruction.BreakIndex;  // Jump to loop exit
       returnValue = default;
       return InstructionResult.Continue;  // Should continue to next instruction
   }
   ```

2. `BreakIndex` points to:
   - `PopEnvironmentInstruction` (for let/const bindings) -> `BreakableExitInstruction`
   - `BreakableExitInstruction` -> `LeaveTryInstruction`
   - `LeaveTryInstruction` -> `nextIndex` (code after the loop)

3. `HandleLeaveTry` calls `CompleteTryNormally(nextIndex)` which should:
   - If `TryStack` is empty: set `_programCounter = nextIndex` and continue
   - If `TryStack` has finally: schedule finally execution then resume

## Root Cause Analysis

### Hypothesis 1 (Most Likely): Async State Machine Premature Completion

When the async function suspends for an await inside `for await...of`, and later resumes with the iterator's `done=true` result, the state machine may be incorrectly interpreting the iterator's completion signal as the async function's completion signal.

**Evidence supporting:**
- The log shows `done=true` is correctly returned by the iterator
- The log shows the code after the loop is NOT executed (missing "After loop" log)
- The async function returns a JsObject (the Promise), indicating it "completed" without executing the return statement

**Mechanism:**
Looking at `ExecuteAsyncStep` (TypedAstEvaluator.ExecutionPlanRunner.Core.cs:252-294):
```csharp
internal AsyncGeneratorStepResult ExecuteAsyncStep(ResumeMode mode, JsValue resumeValue)
{
    // ...
    var result = ExecutePlan(mode, resumeValue);

    if (AsyncStateRef.PendingPromise.TryGetPropertyAccessor(out _))
    {
        return new AsyncGeneratorStepResult(AsyncGeneratorStepKind.Pending, ...);
    }

    if (result.TryGetObject<IJsPropertyAccessor>(out var obj) &&
        obj.TryGetProperty("done", out var doneRaw) &&
        obj.TryGetProperty("value", out var value))
    {
        var done = doneRaw.IsTruthy;
        return done
            ? new AsyncGeneratorStepResult(AsyncGeneratorStepKind.Completed, ...)  // <-- BUG HERE?
            : new AsyncGeneratorStepResult(AsyncGeneratorStepKind.Yield, ...);
    }
    // ...
}
```

The `AsyncFunctionInvoker.DriveToCompletion` (line 104-107) treats `Completed` as the end of the async function:
```csharp
case ExecutionPlanRunner.AsyncGeneratorStepKind.Completed:
    // Async function completed - resolve with the return value
    AsyncInvokeWithOneArg(resolve, step.Value);
    break;
```

**The bug:** When `HandleAsyncIteratorMoveNext` returns `InstructionResult.Continue` after receiving `done=true`, the instruction loop continues execution. BUT if `ExecuteInstructionLoop` exits normally (falls through the `while` loop because `_programCounter >= instructionsLength`), it creates an iterator result with `done=true`:
```csharp
// Line 311-314 in ExecutionPlanRunner.Loop.cs
_state = GeneratorState.Completed;
_done = true;
TryCatchStateRef.TryStack.Clear();
return CreateIteratorResult(JsValue.Undefined, true);
```

This happens when the instruction array is exhausted. The question is: **why does the instruction array get exhausted in the GLOBAL scope case but not in the LOCAL scope case?**

### Hypothesis 2: Environment/Scope Chain Corruption

The iterator's scope chain may become corrupted when the iterable is in global scope, causing the `_programCounter` to point to an invalid instruction index after loop completion.

**Evidence supporting:**
- The bug manifests ONLY when the iterable is in global scope
- Both cases use the same iterator protocol and should have identical control flow

**Evidence against:**
- The iterator correctly produces all values (x, y, z) and `done=true`
- The loop body executes correctly for all iterations

### Hypothesis 3: For-await-of IR Generation Difference

The IR emitter may generate different instruction sequences when the iterable expression refers to a global variable vs a local variable.

**Evidence supporting:**
- ForOfEmitter.cs uses `instruction.IterableExpression.EvaluateExpression(iterableEnv, context)` which may behave differently for global vs local scope references

**Evidence against:**
- The S-expression transformation (parsing) should be identical
- The test shows iteration works correctly in both cases

## Recommended Fix

### Option A: Debug the Exact Point of Divergence

Add targeted logging to trace the exact instruction flow:

1. In `HandleAsyncIteratorMoveNext`, log when `done=true` is detected and the `BreakIndex`:
   ```csharp
   _realmState.Logger?.LogInformation(
       "AsyncIterator done=true, jumping to BreakIndex={Index}, instructionsLength={Len}",
       instruction.BreakIndex, _plan!.Instructions.Length);
   ```

2. In `ExecuteInstructionLoop`, log when the loop exits normally:
   ```csharp
   _realmState.Logger?.LogInformation(
       "ExecuteInstructionLoop exiting: PC={PC}, instructionsLength={Len}",
       _programCounter, instructionsLength);
   ```

3. Compare the logged values for LOCAL vs GLOBAL scope scenarios.

- Pros: Non-invasive, will pinpoint the exact divergence
- Cons: Requires additional test run to collect logs

### Option B: Check BreakIndex vs Instructions.Length

Add a DEBUG assertion to verify that `BreakIndex` is within bounds:

```csharp
if (doneAwait)
{
    Debug.Assert(instruction.BreakIndex >= 0 && instruction.BreakIndex < _plan!.Instructions.Length,
        $"Invalid BreakIndex {instruction.BreakIndex} for instructions length {_plan.Instructions.Length}");
    // ... rest of code
}
```

- Pros: Catches invalid jump targets immediately
- Cons: Does not fix the root cause

### Option C: Verify Instruction Continuity

The most likely fix is in `ExecuteAsyncStep` - it should NOT treat iterator results from internal operations (like for-await-of) as the async function's completion:

```csharp
internal AsyncGeneratorStepResult ExecuteAsyncStep(ResumeMode mode, JsValue resumeValue)
{
    // ...
    var result = ExecutePlan(mode, resumeValue);

    // Check if we're still in the middle of execution vs truly complete
    if (_state == GeneratorState.Suspended)
    {
        // Function yielded/awaited, not completed
        return new AsyncGeneratorStepResult(AsyncGeneratorStepKind.Pending, ...);
    }

    if (_state == GeneratorState.Completed)
    {
        // Only now check the result for done flag
        // ...
    }
    // ...
}
```

- Pros: Addresses the root cause
- Cons: Requires careful analysis to avoid breaking other cases

## Test Plan
- [ ] Add DEBUG logging to trace instruction flow in both scenarios
- [ ] Verify fix resolves original failing test: `dotnet test --filter "FullyQualifiedName~CompareGlobalVsLocalScope_WithDebug"`
- [ ] Run related async iterator tests: `dotnet test --filter "Category~AsyncRuntime&Category~IteratorRuntime"`
- [ ] Check for regressions: `dotnet test tests/Asynkron.JsEngine.Tests`
- [ ] Profile if performance-sensitive: `./tools/profile <script> --cpu --memory`

## Additional Notes

1. The test runs `test()` twice - once at the end of `engine.Evaluate()` and once explicitly via `await engine.Evaluate("test()")`. Both invocations fail in the GLOBAL scope case.

2. The LOCAL scope case succeeds because the entire async function body (including iterable definition) is compiled into a single execution plan with all instruction indices correctly computed.

3. The GLOBAL scope case has the iterable defined at the module level, which means the iterable's iterator factory function is a separate closure. When the for-await-of loop accesses `globalIterable[Symbol.iterator]()`, it may be creating a different execution context.

4. Key difference to investigate: In the LOCAL case, `iterableEnvironment` includes `localIterable` in the function scope. In the GLOBAL case, `iterableEnvironment` must look up `globalIterable` through the scope chain to the global/module scope.

5. The `instruction.BreakIndex` value should be logged to verify it points to the correct post-loop instruction.
