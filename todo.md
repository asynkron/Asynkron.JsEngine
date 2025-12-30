# Test262 Failure Investigation - IR Catch Block Implementation

## Status: IN PROGRESS - Implementing Pure IR Catch Blocks

## Root Cause Identified

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
