# Async For-Of Optimization Plan

## Implementation Status

| Phase | Status | Notes |
|-------|--------|-------|
| Phase 1: Store JsVariables on IteratorDriverState | **COMPLETED** | Eliminated `_iteratorVariables` dictionary entirely |
| Phase 2: Remove DefineOrAssignJsValue | Skipped | Kept for backward compatibility with dictionary-based lookups |
| Phase 3: Fast path for sync iterators | Deferred | Requires more analysis, ES spec mandates Promise.resolve wrapping |
| Phase 4: Specialized fast loop | Future | Major architectural change |

---

## For-Await-Of Semantics on Sync Iterators

When `for await (x of syncIterator)` is used with a **sync iterator**, the ES spec requires:

1. **Each `.next()` call returns `{ value, done }` synchronously**
2. **BUT the value is wrapped in `Promise.resolve(value)`** before being assigned
3. **This creates a microtask per iteration** - even for non-promise values

In the code, this happens at line 1641 in `TypedAstEvaluator.TypedGeneratorInstance.cs`:
```csharp
var rawValue = resultObj.TryGetProperty("value", out var yieldedAwait)
    ? yieldedAwait : JsValue.Undefined;
if (!TryResolvePromiseOrYield(rawValue, context, out var fullyAwaitedValue))
```

The `TryResolvePromiseOrYield` call wraps/awaits the value, causing the microtask.

---

## Current Problem: Dictionary Lookups Per Iteration

The profiler showed **Dictionary Resize ~17% of hot path** because the current async for-of does:

**Every iteration:**
```csharp
// Two dictionary lookups
_iteratorVariables.TryGetValue(iteratorMoveNextInstruction.IteratorSlot, out iterVar);
_iteratorVariables.TryGetValue(iteratorMoveNextInstruction.ValueSlot, out valueVar);

// When storing value - MORE dictionary operations:
valueVar.Environment.DefineOrAssignJsValue(
    iteratorMoveNextInstruction.ValueSlot, awaitedValue);
```

---

## How Fast For-Loop Does It

The fast for-loop in `LoopPlanExtensions.cs` avoids ALL dictionary lookups by:

1. **Pre-resolving slot refs ONCE before the loop:**
```csharp
ref var loopVarRef = ref loopVarEnv.GetSlotRef(loopVarId.SlotIndex);
```

2. **Using direct memory access in tight loop:**
```csharp
while (CheckCondition(loopVarRef.NumberValue, limit, comparison))
{
    // Direct ref access - NO dictionary lookups!
    accumRef = new JsValue(s + i);
    loopVarRef = new JsValue(i + 1);
}
```

---

## Optimization Plan

### Phase 1: Store JsVariables on IteratorDriverState (Eliminate Dictionary)

**Problem:** `_iteratorVariables` dictionary is looked up on EVERY iteration.

**Solution:** Store the `JsVariable` directly on `IteratorDriverState`:

```csharp
// In IteratorDriverState, add fields:
public JsVariable IteratorVariable;  // Pre-resolved at IteratorInit
public JsVariable ValueVariable;     // Pre-resolved at IteratorInit
```

**At `IteratorInitInstruction`:** (lines 1380-1390)
```csharp
// Instead of storing in _iteratorVariables dictionary:
driverState.IteratorVariable = new JsVariable(environment, iteratorInitInstruction.IteratorSlotIndex);
if (iteratorInitInstruction.ValueSlotIndex >= 0)
    driverState.ValueVariable = new JsVariable(environment, iteratorInitInstruction.ValueSlotIndex);
```

**At `IteratorMoveNextInstruction`:** (lines 1398-1415)
```csharp
// Remove dictionary lookups entirely:
var iterVar = driverState.IteratorVariable;  // Direct field access
var valueVar = driverState.ValueVariable;    // Direct field access
```

### Phase 2: Use Ref Access for Value Writes (Eliminate DefineOrAssignJsValue)

**Problem:** After writing via `valueVar.Write()`, we also call `DefineOrAssignJsValue` which does dictionary operations.

**Solution:** Remove the redundant `DefineOrAssignJsValue` call - slot-based access is sufficient:

```csharp
// Current (lines 1739-1750):
if (valueVar.IsValid)
{
    valueVar.Write(awaitedValue);
    // REMOVE THIS - redundant dictionary operation:
    // valueVar.Environment.DefineOrAssignJsValue(
    //     iteratorMoveNextInstruction.ValueSlot, awaitedValue);
}
```

The slot already IS the binding. The `DefineOrAssignJsValue` is redundant.

### Phase 3: Fast Path for Sync Iterators (Skip Promise Wrapping in Non-Async Mode)

**Problem:** Even sync iterators pay the `TryResolvePromiseOrYield` cost per iteration.

**Solution:** In non-async-step mode with sync iterator, skip promise resolution:

```csharp
if (!driverState.IsAsyncIterator && !_asyncStepMode)
{
    // Fast path: sync iterator in sync mode - value is already resolved
    awaitedValue = rawValue;  // Skip TryResolvePromiseOrYield
}
else
{
    // Existing code for async iterators or async step mode
    if (!TryResolvePromiseOrYield(rawValue, context, out awaitedValue)) ...
}
```

### Phase 4 (Future): Specialized Fast Loop for Simple Sync For-Of

For sync iterators with simple bodies (no await, no yield), we could generate the same tight C# loop used by `ExecuteFastNumericLoop`. This would require:

1. Detecting at IR generation time that the for-of is sync and body is simple
2. Generating a `FastForOfPlan` similar to `FastNumericLoopPlan`
3. Executing with tight C# loop + pre-resolved refs

---

## Expected Impact

| Optimization | Impact |
|--------------|--------|
| Store JsVariable on IteratorDriverState | Eliminates 2 dictionary lookups per iteration |
| Remove DefineOrAssignJsValue | Eliminates dictionary write per iteration |
| Fast path for sync iterators | Eliminates promise wrapping overhead for sync case |

This should bring async for-of much closer to the fast for-loop performance, especially for sync iterators in non-async contexts.

---

## Files Modified (Phase 1)

- `src/Asynkron.JsEngine/Execution/IteratorDriverState.cs` - Added `IteratorVariable` and `ValueVariable` JsVariable fields
- `src/Asynkron.JsEngine/Ast/TypedAstEvaluator.TypedGeneratorInstance.cs`:
  - Replaced `_iteratorVariables` dictionary with `_currentDriverState` single field
  - `IteratorInitInstruction`: Sets `IteratorVariable` on state and caches `_currentDriverState`
  - `IteratorMoveNextInstruction`: Uses cached `_currentDriverState` for O(1) access
  - `CreateIterationEnvironmentInstruction`: Uses `_currentDriverState.IteratorVariable.Environment` for loop scope
