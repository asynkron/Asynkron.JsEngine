# Loop Environment Slot Caching

## Goal
Cache loop iteration environments as slots in the parent scope, eliminating separate caching mechanisms and unifying with the existing identifier slot system.

## Design

### Concept
Instead of caching iteration environments on `IteratorDriverPlan._cachedIterationEnvironment`, store them as JsValue slots in the parent JsEnvironment - just like variables.

```javascript
function foo() {
    let a = 1;             // slot 0
    for (const x of arr) { // iteration env cached in slot 1
        ...
    }
}
```

### Runtime Pattern
```csharp
ref var slot = ref loopEnv.GetSlotRef(plan.LoopEnvSlotIndex);
if (slot.IsUndefined) {
    var iterEnv = new JsEnvironment(loopEnv, ...);
    iterEnv.InitializeSlots(plan.IterationSlotCount, plan.IterationScopeId);
    slot = JsValue.FromObjectUnsafe(iterEnv);
} else {
    var iterEnv = (JsEnvironment)slot.ObjectValue!;
    iterEnv.Reset(loopEnv, ...);
}
```

## Implementation Tasks

### Phase 1: AST Node Changes
- [ ] Add `LoopEnvSlotIndex` property to `ForEachStatement` (default -1)
- [ ] Add `LoopEnvSlotIndex` property to `ForStatement` (default -1)
- [ ] Verify property is copied in any AST cloning/rewriting

### Phase 2: Scope Analyzer Changes
- [ ] In `VisitForEachStatement`: when `CanReuseIterationEnvironment` is true, allocate a slot in the LOOP environment (not iteration env) and assign to `LoopEnvSlotIndex`
- [ ] In `VisitForStatement`: same logic for for-loops with per-iteration bindings
- [ ] Increment parent scope's `SlotCount` to include loop env slots
- [ ] Add tests for slot assignment

### Phase 3: Plan Updates
- [ ] Add `LoopEnvSlotIndex` to `IteratorDriverPlan` constructor and property
- [ ] Add `LoopEnvSlotIndex` to `LoopPlan` if needed for regular for-loops
- [ ] Remove `_cachedIterationEnvironment` field from `IteratorDriverPlan`
- [ ] Remove `GetOrResetIterationEnvironment` method from `IteratorDriverPlan`
- [ ] Update `IteratorDriverFactory.CreatePlan` to pass slot index

### Phase 4: Runtime Execution Changes
- [ ] Update `IteratorDriverPlanExtensions.ExecuteIteratorDriverJsValue`:
  - When `LoopEnvSlotIndex >= 0` AND `CanReuseIterationEnvironment`:
    - Use slot-based caching pattern
  - Else: fall back to pool/allocation
- [ ] Update `ForEachStatementExtensions` for for-in loops similarly
- [ ] Update `LoopPlanExtensions` for regular for-loops if applicable
- [ ] Remove pool return calls for cached loop environments (they live in slots)

### Phase 5: Testing
- [ ] Update `ForOfEnvCacheTest` to verify slot-based caching
- [ ] Add test: multiple loops in same scope get different slot indices
- [ ] Add test: nested loops cache correctly
- [ ] Add test: closures disable slot caching (falls back to allocation)
- [ ] Run full test suite
- [ ] Profile to verify allocation reduction

### Phase 6: Cleanup
- [ ] Remove any dead code from old caching approach
- [ ] Update comments/docs to reflect new design

## Edge Cases Handled

| Case | Behavior |
|------|----------|
| Closures capture iteration variable | `CanReuseIterationEnvironment=false` → no slot caching, allocate fresh |
| Nested loops | Inner loop's slot is in outer's iteration env |
| Multiple loops in same scope | Each gets unique slot index |
| Recursion | Each call frame has own slots |
| break/return/throw | Cached env stays in slot |

## Benefits

1. **No separate caching mechanism** - reuses existing slot infrastructure
2. **Natural lifecycle** - cached env dies with parent scope
3. **Unified design** - loops are just another kind of "binding"
4. **Works for all loop types** - for, for-of, for-in, while (if needed)
5. **Simpler code** - removes IteratorDriverPlan._cachedIterationEnvironment complexity
