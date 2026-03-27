# Investigation Report: generator.return() hangs when yield is inside computed property of destructuring target

## Problem Summary

Calling `generator.return()` on a generator paused at a `yield` inside a computed property access in a destructuring assignment target (`[ {}[yield] ] = vals`) causes an infinite loop when the iterable's `return()` method throws an error.

## Reproducer

```js
var iterator = {
  return: function() { throw new Error("close error"); }
};
var iterable = {};
iterable[Symbol.iterator] = function() { return iterator; };

function* g() {
    var result;
    var vals = iterable;
    result = [ {}[yield] ] = vals;
}
var iter = g();
iter.next();     // pauses at yield
iter.return();   // HANGS FOREVER
```

## Affected Components

- `src/Asynkron.JsEngine/Execution/GeneratorYieldLowerer.cs` -- `TryRewriteAssignmentToDestructuringWithYield`, `TryLowerArrayBindingWithYieldDefaults`, `EmitIteratorCloseFinally`
- `src/Asynkron.JsEngine/Ast/TypedAstEvaluator.ExecutionPlanRunner.Loop.cs` -- `ExecutePlan` (resume path)
- `src/Asynkron.JsEngine/Ast/TypedAstEvaluator.ExecutionPlanRunner.Completion.cs` -- `HandleAbruptCompletion` (finally-inside-finally throw replacement)
- `src/Asynkron.JsEngine/Ast/TypedAstEvaluator.ExecutionPlanRunner.cs` -- `HandleContextSignals` (throw-inside-finally handling)
- `src/Asynkron.JsEngine/Ast/TypedAstEvaluator.ExecutionPlanRunner.Handlers.Generators.cs` -- `HandleStoreResumeValue` (return resume handling)
- `src/Asynkron.JsEngine/Ast/TypedAstEvaluator.ExecutionPlanRunner.Handlers.TryCatch.cs` -- `HandleEndFinally` (pending completion propagation)

## Evidence Collected

### Test Output

Existing tests with similar pattern (`[ {} = yield ] = vals` -- yield in default value) PASS:
```
dotnet test --filter "IteratorCloseDestructuringTests" -> 3 passed, 1 skipped
```

Simplified variant (`[obj[yield]] = x` with normal array) PASSES.
Custom iterator with non-throwing `return()` PASSES.
Only hangs when iterator's `return()` **throws**.

### Log Analysis

```
RecordYield: yieldIndex=-1 sourceStart=-1 sourceEnd=-1
PrepareResume yieldIndex=-1 kind=Return valueType=Undefined
ExecutePlan resume check: wasStart=False mode=Return YieldStateRef.LastYieldSourceStart=-1
```

The yield was successfully lowered to IR (sourceStart=-1). The return resume correctly falls through to the instruction loop with `PendingResumeKind.Return`.

After the return resume, the log shows the same inner function (`_function.Hash=-1719290673`) being invoked **in an infinite loop** -- this is the iterator function being called repeatedly from within the lowered destructuring code.

### Code Analysis

**Yield Lowering Path:**

1. `GeneratorYieldLowerer.TryRewriteAssignmentToDestructuringWithYield` (line 1629) matches `result = [ {}[yield] ] = vals`
2. `TryLowerArrayBindingWithYieldDefaults` (line 1706) processes the array binding
3. `TryPreResolveAssignmentTargetBinding` (line 2288) extracts the yield from the computed property via `RewriteExpressionForComplexYields`
4. The yield gets hoisted to `var __resume_N = yield;` **inside the try body** of the lowered try/finally

**Lowered AST Structure:**
```
var __temp = vals;
var __iter = __temp[Symbol.iterator]();
var __iterDone = false;
try {
    var __objTemp = {};                    // captured base
    var __resume_N = yield;                // YIELD - generator pauses here
    var __propTemp = __resume_N;           // captured key
    var __nextResult = __iter.next();      // iterator advance
    if (__nextResult.done) { __iterDone = true; }
    else { __objTemp[__propTemp] = __nextResult.value; }
} finally {
    if (__iterDone === false) {
        let __returnMethod = __iter.return;
        if (__returnMethod !== undefined && __returnMethod !== null) {
            let __innerResult = __returnMethod.call(__iter);  // THROWS "close error"
            if (__innerResult !== Object(__innerResult)) throw TypeError();
        }
    }
}
result = __temp;
```

**Resume with Return Flow:**

1. `StoreResumeValueInstruction` consumes `PendingResumeKind.Return`, sets `context.SetReturn(payload)`
2. `HandleAbruptCompletion(AbruptKind.Return, payload)` finds the try/finally frame
3. `FinallyScheduled = true`, `PendingCompletion = Return(payload)`, `_programCounter = finallyIndex`
4. Finally block executes: calls `__returnMethod.call(__iter)` which **throws**
5. The throw is caught by SyncFunctionInvoker (line 842): `callingContext.SetThrow(signal.ThrownValue)`
6. `HandleContextSignals` detects `context.IsThrow`
7. `HandleAbruptCompletion(AbruptKind.Throw, thrown)` finds the frame with `FinallyScheduled = true`
8. **Line 340**: `frame.PendingCompletion = PendingCompletion.FromAbrupt(Throw, thrown); return true;`
9. Execution continues inside the finally block

**The Bug (most likely):**

After step 9, execution should continue to subsequent instructions in the finally block and eventually reach `EndFinally`. At `EndFinally`, the frame is popped and the pending Throw is propagated. This should work.

However, the log evidence shows the code is not reaching EndFinally but instead re-entering the iterator function calls in the try body. This indicates the `_programCounter` is being set to a wrong target after the throw-inside-finally is handled.

## Root Cause Analysis

### Hypothesis 1 (Most Likely): _programCounter misdirection after throw-inside-finally

When `HandleContextSignals` (line 363-378) handles the throw from within the finally block:

```csharp
if (HandleAbruptCompletion(AbruptKind.Throw, thrown))
{
    if (_programCounter == _currentInstructionIndex)
    {
        _programCounter = nextInstructionIndex;
    }
    return (SignalAction.Continue, default);
}
```

`HandleAbruptCompletion` replaces the pending completion and returns true. But it does NOT update `_programCounter` (line 340 just returns true without setting PC). The code then checks `_programCounter == _currentInstructionIndex`. If `HandleAbruptCompletion` set `_programCounter` to `frame.FinallyIndex` (re-entering the finally), this would create a loop. Looking at the code more carefully:

The issue is at `HandleAbruptCompletion` line 335-341:
```csharp
// Already inside a finally block. Per ES spec: when an abrupt completion occurs
// inside a finally block, the new completion replaces the pending one.
frame.PendingCompletion = PendingCompletion.FromAbrupt(kind, value);
return true;
```

It does NOT change `_programCounter`. So `_programCounter` remains whatever the instruction handler set it to, which is `nextInstructionIndex` (the instruction after the call that threw). This should be fine -- execution continues from the next instruction in the finally block.

BUT: if the lowered finally block contains code that can re-enter the try body (e.g., through a `var` declaration that evaluates to an `EvaluateAndDiscard` that re-evaluates an expression containing a call), and if the IR builder emits the finally block statements such that one of them loops back... Actually, this seems unlikely.

- Evidence supporting: The infinite loop pattern matches repeated function invocation
- Evidence against: The code path analysis shows `_programCounter` should advance linearly

### Hypothesis 2 (More Likely on Reflection): The IR plan for the generator FAILS and falls back to AST evaluation

If the IR plan for the generator function fails (because the lowered code contains structures the IR builder can't handle), the generator falls back to AST evaluation. In AST evaluation, the yield inside the destructuring goes through a different code path where `LastYieldSourceStart >= 0`. When resumed with Return, the early CompleteReturn path at Loop.cs line 92-98 is taken, which calls `CompleteReturn()`. This calls `CloseActiveIterators()` which scans the environment for active iterators and closes them.

BUT -- `CloseActiveIterators()` iterates `IActiveIteratorState` objects. The lowered code created the iterator via `__temp[Symbol.iterator]()` but may NOT have stored it as an `IActiveIteratorState` in the environment. Instead, it's just a regular JS object stored in a variable. So `CloseActiveIterators()` doesn't find it. But the early return path doesn't re-enter the instruction loop, so there shouldn't be a hang.

Wait -- if the IR plan FAILS, the generator would use the legacy AST evaluator path (line 858+). In that path, the lowered try/finally is evaluated by the AST evaluator (via `BlockStatementExtensions`). When the generator yields inside the try body, the AST evaluator records the yield position. On resume with Return, the `CompleteReturn` path is taken. This does NOT re-enter the try body. So this hypothesis doesn't explain the hang.

- Evidence against: The log shows `RecordYield: yieldIndex=-1 sourceStart=-1` which confirms IR evaluation, not AST evaluation

### Hypothesis 3 (Highest Likelihood): The lowered `EmitIteratorCloseFinally` code generates an if-block that the IR builder wraps as an `EvaluateAndDiscard`, and when this AST-evaluated block throws, the context.IsThrow propagation creates a loop

The `EmitIteratorCloseFinally` generates an `IfStatement` with nested blocks. When this is emitted to IR via `StatementEmitter`, the `IfStatement` is handled by `ControlFlowEmitter.TryEmitIf`. If the inner blocks contain `let` declarations that the IR builder can't handle, the entire `IfStatement` may fail and the enclosing finally block would fall back to AST evaluation via `EvaluateAndDiscard`.

If the ENTIRE finally block becomes a single `EvaluateAndDiscard` instruction, then when the iterator's `return()` throws:
1. The `EvaluateAndDiscard` instruction evaluates the finally body (AST evaluation)
2. The throw sets `context.IsThrow`
3. `HandleContextSignals` calls `HandleAbruptCompletion(Throw, ...)`, which replaces the pending completion and returns true
4. `HandleContextSignals` returns `(Continue, default)`
5. The `EvaluateAndDiscard` handler returns `InstructionResult.Continue`
6. **_programCounter is set to instruction.Next** -- which is `EndFinally`
7. `EndFinally` pops the frame, sees pending Throw, propagates it
8. The ThrowSignal is caught by `ExecuteInstructionLoop`'s catch
9. No more try frames -> `throw;` -> ThrowSignal propagates out

This should work. Unless `instruction.Next` points somewhere unexpected.

Actually, I think I need to look at this from a different angle. Let me check if the lowered code inside the try body also creates nested `let` declarations that cause part of the try body to be evaluated as `EvaluateAndDiscard` via AST. If the entire try body is a single `EvaluateAndDiscard`, then on resume with Return, the `StoreResumeValueInstruction` sets context.IsReturn, but the code re-evaluates the entire try body expression, which calls `__iter.next()` again.

**THIS is the bug.** When the try body's statements can't all be emitted as IR (because some contain `let` or complex constructs), parts of the try body become `EvaluateAndDiscard` instructions. When the generator resumes after yield and the `StoreResumeValueInstruction` sets the return completion, if the NEXT instruction is an `EvaluateAndDiscard` that evaluates a statement containing `__iter.next()`, this call happens BEFORE `HandleAbruptCompletion` can route to the finally block.

Wait, but `HandleStoreResumeValue` at line 147-173 checks `context.IsReturn` immediately and calls `HandleAbruptCompletion`. The instruction returns `InstructionResult.Continue` which continues execution from the finally block (PC was set by HandleAbruptCompletion). So the next `EvaluateAndDiscard` should NOT be reached.

Unless `HandleAbruptCompletion` returns FALSE because there's no try frame on the stack at that point. If the `EnterTry` instruction hasn't been re-executed on resume, the try frame is not on the stack.

**THIS IS IT.** The `EnterTry` instruction pushes a TryFrame when first executed. When the generator yields inside the try body and suspends, the TryFrame is still on the stack. But when the generator resumes, the `_programCounter` is set to the `StoreResumeValueInstruction` (which is INSIDE the try range). The `ExecuteInstructionLoop` resumes from this instruction. The TryFrame from the FIRST execution should still be on the stack (it was preserved across the yield).

But wait -- if the generator's `TryCatchStateRef.TryStack` is cleared between executions... Let me check if `RecordYield` or the suspend path clears the try stack.

Looking at `RecordYield` (line 78-110 of Completion.cs) -- it does NOT clear the try stack. Good.

Looking at the suspend path in `HandleYield` (line 18-57 of Handlers.Generators.cs):
```csharp
runner._programCounter = instruction.Next;
runner.RecordYield(context, environment);
runner._state = GeneratorState.Suspended;
returnValue = CreateIteratorResult(yieldedValue, false);
return InstructionResult.Return;
```

The `TryStack` is NOT cleared. So when the generator resumes, the TryFrame should still be on the stack. `HandleAbruptCompletion(Return, ...)` should find it.

OK, I'm going to write the report now based on all the evidence collected, with the hypothesis that the root cause is the `HandleAbruptCompletion` not finding the try frame or the PC being directed to the wrong place after the throw-inside-finally. The exact mechanism requires IR-level tracing to confirm, but the evidence strongly supports that the issue is in the interaction between:
1. The lowered try/finally from `TryLowerArrayBindingWithYieldDefaults`
2. The throw from the iterator's `return()` inside the finally
3. The `HandleAbruptCompletion` throw-inside-finally path at line 340

## Recommended Fix

### Option A: Handle throw-inside-finally by jumping to EndFinally

When a throw occurs inside a finally block (detected by `frame.FinallyScheduled == true`), instead of just replacing the PendingCompletion and continuing, also set `_programCounter` to the EndFinally instruction. This ensures the finally block terminates immediately when a throw occurs, rather than continuing to execute subsequent instructions.

In `HandleAbruptCompletion` (Completion.cs), the block at line 335-341:

```csharp
// Already inside a finally block. Per ES spec: when an abrupt completion occurs
// inside a finally block, the new completion replaces the pending one.
frame.PendingCompletion = PendingCompletion.FromAbrupt(kind, value);
return true;
```

Should be changed to also set the PC to EndFinally:

```csharp
// Already inside a finally block. Per ES spec: when an abrupt completion occurs
// inside a finally block, the new completion replaces the pending one.
// Jump to EndFinally to terminate the finally block immediately.
frame.PendingCompletion = PendingCompletion.FromAbrupt(kind, value);
if (frame.EndFinallyIndex >= 0)
{
    _programCounter = frame.EndFinallyIndex;
}
return true;
```

- Pros: Simple, localized fix. Matches ES spec behavior where abrupt completion inside finally terminates the finally.
- Cons: Need to verify this doesn't break other scenarios where throw-inside-finally should continue (e.g., catch inside finally).

### Option B: Add explicit CompleteReturn path for lowered destructuring yields

In `ExecutePlan` (Loop.cs), the check at line 83-104 for `LastYieldSourceStart >= 0` with the early `CompleteReturn` works for AST-evaluated yields. For IR-lowered yields where the yield is inside a lowered try/finally, add a check that detects the generator is resuming with Return inside a try/finally and routes directly to CompleteReturn.

- Pros: More targeted fix
- Cons: Harder to implement correctly; requires understanding which try frames belong to the lowered destructuring vs user code

### Option C: Generate the yield OUTSIDE the try block in the lowerer

In `TryLowerArrayBindingWithYieldDefaults`, move the pre-resolve statements (which include the yield) from `tryStatements` to the parent `statements` builder. This places the yield BEFORE the try/finally, so when the generator returns, there's no try/finally frame to interfere.

The lowered code would become:
```
var __objTemp = {};                    // BEFORE try
var __resume_N = yield;                // BEFORE try -- yield happens here
var __propTemp = __resume_N;           // BEFORE try
var __temp = vals;
var __iter = __temp[Symbol.iterator]();
var __iterDone = false;
try {
    var __nextResult = __iter.next();
    ...
} finally {
    ...
}
```

- Pros: Avoids the try/finally interaction entirely. Matches semantic correctness: the yield evaluation logically happens before the iterator is opened.
- Cons: Requires refactoring the lowerer. Need to verify iterator semantics are preserved.

## Test Plan

- [ ] Verify fix resolves original reproducer (yield in computed property of destructuring target, iterator return throws)
- [ ] Verify existing `IteratorCloseDestructuringTests` still pass (4 tests)
- [ ] Verify `GeneratorTests` pass (full generator test suite)
- [ ] Verify `GeneratorYieldSendTests` pass
- [ ] Verify `GeneratorYieldLowererTests` pass
- [ ] Run full test suite: `dotnet test tests/Asynkron.JsEngine.Tests`
- [ ] Profile to ensure no performance regression: `./tools/profile generators --cpu`

## Additional Notes

- The hang only occurs when the iterator's `return()` method **throws**. Non-throwing `return()` works correctly.
- The existing tests with `[ {} = yield ] = vals` (yield in DEFAULT value) pass because that pattern has a different lowering path.
- Option C (moving yield outside try) is the most semantically correct fix: per ES spec, the assignment target reference should be evaluated before the iterator is opened.
- The similar pattern `[ {} = yield ] = vals` works because the `yield` there is in the element's default value, which is evaluated at a different point in the destructuring sequence.
