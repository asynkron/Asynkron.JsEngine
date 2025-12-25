# Nested For-Await-Of Loop Bug

## Problem Statement

When a `for await...of` loop is nested inside a regular `for` loop within an async function, only the first outer iteration executes the inner loop correctly. Subsequent outer iterations fail silently.

```javascript
let sum = 0;
(async function() {
    const arr = [1, 2];
    for (let i = 0; i < 2; i++) {
        for await (const n of arr) {
            sum += n;  // Only runs for i=0
        }
    }
})();
// Expected: sum = 6 (1+2 + 1+2)
// Actual: sum = 3 (1+2 only)
```

## Root Cause Identified

**`IndexOutOfRangeException` in `valueVar.Write()` on second outer iteration.**

The `JsVariable` cached on `IteratorDriverState.ValueVariable` points to a stale environment from the first outer iteration. When the second outer iteration starts:

1. `IteratorInitInstruction` creates a NEW `IteratorDriverState` with empty `ValueVariable`
2. First `IteratorMoveNextInstruction` captures `valueVar = new JsVariable(environment, slotIndex)`
3. The `environment` at capture time has `HasSlots=True` but only 1 slot (SlotCount=1)
4. The `slotIndex` is 1, which is out of bounds for the environment's slots array
5. When `valueVar.Write()` is called, it throws `IndexOutOfRangeException`

## Key Debug Trace

```
[DEBUG] IteratorInitInstruction at PC=3, _currentDriverState=SET
[DEBUG-LOOP] PC=11, Instruction=EnterTryInstruction
[DEBUG-LOOP] PC=4, Instruction=IteratorMoveNextInstruction
[DEBUG] awaitEnumerator.MoveNext() = True
[DEBUG] Suspending: setting AwaitingValue=true, PC will be 4
[DEBUG] RETURNING from ExecutePlan for await (PC=4)
[DEBUG-LOOP] PC=4, Instruction=IteratorMoveNextInstruction
[DEBUG] Resuming from await: AwaitingNextResult=False, AwaitingValue=True
[DEBUG] StoreIteratorValue: awaitedValue=1, Next=10, valueVar.IsValid=True
[DEBUG] Writing via valueVar: SlotIndex=1, Env.HasSlots=True
[DEBUG] EXCEPTION in valueVar.Write: IndexOutOfRangeException: Index was outside the bounds of the array.
```

## Environment Mismatch Analysis

The issue is that when we capture `valueVar` on the second outer iteration, the `environment` variable in `ExecutePlan` may be pointing to a stale per-iteration environment from the first outer iteration, NOT the correct loop scope where the value slot should be stored.

### IR Instruction Flow
```
IteratorInit (PC=3) - creates loop scope, stores iterator state
EnterTry (PC=11)
MoveNext (PC=4) - reads iterator, writes value to loop scope
  -> CreateIterationEnvironment (PC=10) - creates per-iteration child env
  -> loop body
  -> back to MoveNext
  -> if done: LeaveTry
```

### The Bug
When the first outer iteration completes:
1. `environment` may still reference a per-iteration environment from the first outer iteration
2. On second outer iteration, `IteratorInitInstruction` creates new iterator state
3. `MoveNext` captures `valueVar` using the CURRENT `environment`
4. But that environment doesn't have the slots for the value variable!

## Affected Files

- `src/Asynkron.JsEngine/Ast/TypedAstEvaluator.TypedGeneratorInstance.cs`
  - Lines ~1413-1422: `valueVar` capture logic
  - Lines ~1735-1756: `StoreIteratorValue` label

## Potential Fixes

### Option 1: Use the iterator's captured environment
When capturing `valueVar`, use the environment stored in `driverState.IteratorVariable` instead of the current `environment`:

```csharp
if (!valueVar.IsValid && iteratorMoveNextInstruction.ValueSlotIndex >= 0)
{
    // Use the loop scope environment (where iterator is stored), not current env
    var loopScopeEnv = driverState.IteratorVariable.Environment ?? environment;
    valueVar = new JsVariable(loopScopeEnv, iteratorMoveNextInstruction.ValueSlotIndex);
    driverState.ValueVariable = valueVar;
}
```

### Option 2: Don't cache valueVar on driverState
Instead, always look up the value slot fresh on each iteration:

```csharp
// At StoreIteratorValue, don't use cached valueVar
StoreValueBySlot(environment, iteratorMoveNextInstruction.ValueSlot,
    iteratorMoveNextInstruction.ValueSlotIndex, awaitedValue);
```

### Option 3: Reset environment on IteratorInit
In `IteratorInitInstruction`, ensure `environment` is reset to the correct loop scope before subsequent operations.

## Related Tests

- `ForAwaitOf_InIIFE_MultipleIterations_DoesNotComplete`
- `ForAwaitOf_InIIFE_MultipleIterations_DoesNotComplete2`

## Debug Code Added (to be removed)

Multiple `Console.WriteLine` statements were added to trace execution. Search for `[DEBUG]` in `TypedAstEvaluator.TypedGeneratorInstance.cs` to find and remove them.

## Status

**Investigation complete. Fix pending.**
