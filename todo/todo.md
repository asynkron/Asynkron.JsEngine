# Test262 Failure Investigation - IR Catch Block Implementation

## Status: IN PROGRESS - Test Bomb Analysis Complete

## Test Bomb Results (2024-12-30)

Systematic testing revealed **two distinct bugs** in the IR catch block implementation:

### Bug 1: Previous Statement Completion Value Leaks Through Empty Blocks

When there's a statement before a compound statement (try/catch, if/else), and the executed block is **empty**, the previous statement's completion value incorrectly propagates:

| Test | Code | Expected | Actual |
|------|------|----------|--------|
| H5 | `eval('1; try { throw null; } catch (e) { }')` | undefined | **1** |
| H6 | `eval('1; try { } catch (e) { }')` | undefined | **1** |
| H13 | `eval('5; if (false) { 1; } else { }')` | undefined | **5** |
| H15 | `eval('1; for (var i = 0; i < 1; i++) { }')` | undefined | **0** |
| H17 | `eval('1; switch (1) { case 1: break; }')` | undefined | **True** |

### Bug 2: Finally Normal Completion Incorrectly Overwrites Try Value

Per ES spec 13.15.8, a finally block with **normal completion** should preserve the try/catch completion value. Currently it's overwriting it:

| Test | Code | Expected | Actual |
|------|------|----------|--------|
| H9 | `eval('try { 1; } finally { 9; }')` | 1 | **9** |

The spec says: "If F.[[Type]] is normal, set F to C" - meaning finally's normal completion should preserve the previous value.

### Additional Finding: Break From Finally Causes Infinite Loop

The `Replicate_BreakFromFinally` test shows an infinite loop where BREAK instruction at [10] keeps going to [7] repeatedly without exiting the loop.

### Passing Tests (21 of 27)

Core functionality works:
- Empty try/catch blocks return undefined (when no previous statement)
- Try/catch with values return correct values
- Finally preserves try/catch completion (when empty)
- If/else completion values work (when no previous statement)
- Catch parameter scoping works correctly
- Continue/return/break from catch works
- Nested try/catch works
- Eval basics work

### Test Files Created

- `tests/Asynkron.JsEngine.Tests/CatchCompletionValueReplicationTest.cs` - Replication of Test262 failures
- `tests/Asynkron.JsEngine.Tests/CatchBlockTestBomb.cs` - 27 hypothesis tests

---

## Original Root Cause (Pre-IR Implementation)

The catch block delegation to AST evaluation causes thrown values to be lost when:
1. `assert.throws` runs via IR (no eval in its body)
2. Its try block calls a function that uses AST path (due to `eval`)
3. Error propagates from AST back to IR catch handler
4. The synthetic `let thrown = #catchSlot` reads `undefined` instead of the thrown value

The fix: Emit catch blocks entirely in IR, avoiding AST delegation.

## Implementation Plan

### 1. Add `EnterCatchInstruction` (Instructions.cs)
```csharp
internal sealed record EnterCatchInstruction(
    int Next,
    Symbol? CatchParameterSymbol,  // The catch(e) parameter
    int ScopeId,
    int SlotCount,
    ImmutableDictionary<Symbol, int> SlotMap)
    : ExecutionInstruction(InstructionKind.EnterCatch, Next);
```

### 2. Add `InstructionKind.EnterCatch` to enum

### 3. Modify `TryFrame` to store thrown value
Add `ThrownValue` field so `EnterCatchInstruction` can read it.

### 4. Modify `HandleAbruptCompletion`
Store thrown value in `TryFrame.ThrownValue` instead of (or in addition to) the catch slot symbol.

### 5. Add handler in `ExecutionPlanRunner`
```csharp
case InstructionKind.EnterCatch:
{
    var enterCatch = Unsafe.As<EnterCatchInstruction>(instruction);
    var frame = TryCatchStateRef.TryStack.Peek();
    var thrownValue = frame.ThrownValue;

    // Create catch environment with slots
    var catchEnv = new JsEnvironment(environment, ...);
    catchEnv.InitializeSlots(enterCatch.SlotCount, enterCatch.ScopeId);

    // Bind catch parameter directly to thrown value
    if (enterCatch.CatchParameterSymbol is { } param)
    {
        catchEnv.DefineJsValue(param, thrownValue);
    }

    environment = catchEnv;
    _programCounter = enterCatch.Next;
    continue;
}
```

### 6. Modify `TryEmitter.TryEmitTry`
Instead of `BuildCatchBlock` + `StatementInstruction`:
- Emit `PopEnvironmentInstruction` at end
- Recursively emit catch body statements as IR
- Emit `EnterCatchInstruction` as handler entry point

### 7. Remove `BuildCatchBlock` (no longer needed)

## Files to Modify

1. `src/Asynkron.JsEngine/Execution/Instructions/Instructions.cs` - Add EnterCatchInstruction
2. `src/Asynkron.JsEngine/Execution/Instructions/InstructionKind.cs` - Add EnterCatch enum
3. `src/Asynkron.JsEngine/Ast/TypedAstEvaluator.ExecutionPlanRunner.Types.cs` - Add ThrownValue to TryFrame
4. `src/Asynkron.JsEngine/Ast/TypedAstEvaluator.ExecutionPlanRunner.Completion.cs` - Store thrown value in frame
5. `src/Asynkron.JsEngine/Ast/TypedAstEvaluator.ExecutionPlanRunner.cs` - Add EnterCatch handler
6. `src/Asynkron.JsEngine/Execution/Emitters/TryEmitter.cs` - Emit catch as IR
7. `src/Asynkron.JsEngine/Execution/ExecutionPlanBuilder.cs` - Remove BuildCatchBlock

## Test Cases

After implementation, these should pass:
- `ArgumentsObject("language/arguments-object/10.5-1-s.js", True)`
- `ArgumentsObject("language/arguments-object/10.5-7-b-1-s.js", True)`
- `ArgumentsObject("language/arguments-object/10.6-13-c-1-s.js", True)`
- `ArgumentsObject("language/arguments-object/10.6-14-c-4-s.js", True)`
- `ArgumentsObject("language/arguments-object/10.6-2gs.js", True)`

Unit tests in `ThrowBugTests.cs` should continue to pass.

---

## Next Steps: Fix Completion Value Bugs

Based on test bomb analysis, the following fixes are needed:

### Fix 1: Empty Block Completion Value

Empty blocks (catch, try, if/else, for body, switch case) should set completion value to `undefined`, not preserve the previous statement's value.

**Likely locations:**
- `src/Asynkron.JsEngine/Execution/Emitters/` - Block emission not setting completion value
- `src/Asynkron.JsEngine/Ast/TypedAstEvaluator.ExecutionPlanRunner.cs` - Completion value propagation

**Fix approach:**
1. When emitting an empty block, emit an instruction that sets completion value to `undefined`
2. Or ensure the completion value tracking resets at statement boundaries

### Fix 2: Finally Normal Completion

Per ES spec 13.15.8, finally's normal completion should preserve try/catch completion value:
```
TryStatement : try Block Finally
1. Let B be the result of evaluating Block.
2. Let F be the result of evaluating Finally.
3. If F.[[Type]] is normal, set F to B.  <-- THIS IS THE KEY
4. Return Completion(UpdateEmpty(F, undefined)).
```

**Fix approach:**
1. Track the try/catch completion value before entering finally
2. If finally completes normally, restore the try/catch completion value
3. Only use finally's completion if it's abrupt (return/throw/break/continue)

### Fix 3: Break From Finally Infinite Loop

The IR emitter for break inside finally is not correctly exiting the loop structure.

**Fix approach:**
1. Investigate the `TryEmitter` and how break targets are calculated within finally blocks
2. Ensure break instruction properly pops all try frames and exits to the correct target

### Verification

After fixes, run:
```bash
dotnet test tests/Asynkron.JsEngine.Tests --filter "FullyQualifiedName~CatchBlockTestBomb"
```

All 27 tests should pass.
