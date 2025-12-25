# Loop Environment Cache - Invariants and Behaviors

## Scope/Environment Architecture

### ScopeId Assignment (Static Analysis - ScopeAnalyzer)
- **ScopeId** is assigned during static analysis by `ScopeAnalyzer`
- Each scope (function, block, loop iteration) gets a unique ScopeId
- ScopeIds are sequential integers starting from 0 or 1
- **ScopeId=-1** indicates "no scope" or "global scope"

### Environment Creation (Runtime)
- `JsEnvironment` is the runtime representation of a scope
- `JsEnvironment.ScopeId` is set from the AST node's ScopeId when the environment is created
- `JsEnvironment.Enclosing` points to the parent environment (scope chain)

### Slot System
- **Slots** are a fixed-size array (`JsValue[]`) for O(1) variable access
- `SlotMap` maps `Symbol` -> slot index
- `SlotCount` is determined during static analysis
- **Slots are optional**: An environment can work without slots (uses dictionary fallback)

## Invocation Paths

### InvokeSimpleFast Path
- Used for "simple" functions: no async, no defaults, no destructuring, no lexical template
- **Parameters are bound to SLOTS** (direct array access)
- `InitializeSlots` is called to create the slots array
- Slot lookup works correctly

### InvokeWithContext Path
- Used for "complex" functions: has lexical bindings, async, defaults, etc.
- **Parameters are bound to DICTIONARY** (not slots)
- Slots are NOT initialized (to avoid breaking parameter lookup)
- **CRITICAL**: If slots are initialized with `Undefined`, slot lookup succeeds but returns wrong value!

### Why InvokeWithContext Can't Initialize Slots
1. Parameters are resolved via `TryReadIdentifierWithSlot`
2. If slot exists and has value (even `Undefined`), it returns that value
3. It does NOT fall back to dictionary when slot has a value
4. So if we `InitializeSlots` (fills with `Undefined`), parameter reads return `Undefined` instead of actual value

## Loop Environment Caching

### Goal
Cache the per-iteration environment in the enclosing FUNCTION scope's slot array, allowing reuse across loop executions.

### Key Properties on AST Nodes
- `ForStatement.LoopEnvSlotIndex` - slot index in function scope for caching
- `ForStatement.LoopEnvScopeId` - ScopeId of the function scope containing the slot
- Same for `ForEachStatement`

### Slot Allocation Strategy
- Loop env cache slots are allocated in the **enclosing function scope** (not immediate parent)
- Uses `GetEnclosingFunctionScope()` to find the function scope during analysis
- This ensures the cache persists across outer loop iterations

## Scope Chain at Runtime (Nested Loops Example)

For code like:
```javascript
(function() {           // ScopeId=1 (function scope)
    let sum = 0;
    const arr = [1,2,3];
    for (let i = 0; i < 3; i++) {        // ScopeId=2 (outer for per-iteration)
        for (const n of arr) {            // ScopeId=3 (inner for-of per-iteration)
            sum += n;
        }
    }
    return sum;
})();
```

Runtime scope chain (from innermost to outermost):
```
5(for-of iteration) -> 4(for-of loop) -> 3(for per-iteration) -> 2(for loop) -> 1(function) -> null
```

**Note**: ScopeIds at runtime may differ from AST ScopeIds due to:
- Loop environments reusing ScopeIds
- Block environments adding intermediate scopes

## Current Issue: Scope Resolution

### Problem
- `LoopEnvScopeId=2` (function scope where slot is allocated)
- But runtime scope chain shows different structure
- `FindByScopeId(2)` finds the scope, but it has `slots=True` already (from per-iteration init)
- The FUNCTION scope (ScopeId=1) has `slots=False`

### Observation from Debug Log
```
ScopeChain=5(slots=False) -> 4(slots=True) -> 3(slots=True) -> -1(slots=False) -> 2(slots=True) -> 1(slots=False)
```

- ScopeId=1 is the FUNCTION scope but has `slots=False` (never initialized)
- ScopeId=2 has `slots=True` but that's the per-iteration scope, not function scope

### Root Cause Hypothesis
The `LoopEnvScopeId` from static analysis doesn't match what's expected at runtime because:
1. Static analysis assigns ScopeId to the FunctionExpression
2. At runtime, that ScopeId might be used for a different purpose
3. Or the function environment never gets that ScopeId assigned

## Key Methods

### `JsEnvironment.FindByScopeId(scopeId)`
- Walks the scope chain (via `Enclosing`)
- Returns first environment with matching `ScopeId`
- Returns `null` if not found

### `JsEnvironment.TryResolveSlot(scopeId, slotIndex, out env)`
- Calls `FindByScopeId(scopeId)`
- Also checks that `_slots` exists and `slotIndex < _slots.Length`
- Used for slot-based variable access

### `JsEnvironment.InitializeSlots(slotCount)`
- Creates `_slots` array if null or too small
- Fills all slots with `JsValue.Undefined`
- **WARNING**: This can break dictionary-based parameter lookup!

## FOUND BUG: ScopeId Mismatch Between Parses

### Evidence
AST (from `engine.ParseProgram(script)`):
```
FunctionExpression: ScopeId=2, SlotCount=2
ForEachStatement: LoopEnvScopeId=2
```

Runtime (from `engine.Evaluate(script)` - separate parse!):
```
LoopEnvScopeId=9 (!!)
ScopeChain=12(slots=False) -> 11 -> 10 -> -1 -> 9(slots=True) -> 8 -> null
```

### Root Cause
The ScopeId counter is likely GLOBAL across all parses. When:
1. Engine is created and warms up (parses "1+1") - allocates ScopeIds
2. Test parses script for debug output - allocates ScopeIds 1-7
3. Test evaluates script (parses AGAIN) - allocates ScopeIds 8-14 (or similar)

The IteratorDriverPlan stores the ScopeId from the FIRST parse, but at runtime we're using environments from the SECOND parse!

### Solution Options
1. **Use relative scope depth instead of absolute ScopeId** for loop env cache
2. **Ensure ScopeId is stable across parses** (reset counter per parse?)
3. **Store the cached env in the AST cache, not in environment slots**

### Quick Fix for Testing
Remove the `engine.ParseProgram(script)` call from the test - it's just for debug output and causes the ScopeId mismatch.

## SOLVED: Root Causes and Fixes

### Issue 1: ScopeId Mismatch Between Parses
**Root Cause**: `ScopeAnalyzer` is a single instance per JsEngine, and `_nextScopeId` is an instance field that increments across all parses.

**Fix**: When debugging/testing, parse once and evaluate the parsed program: `engine.Evaluate(parsed)` instead of `engine.Evaluate(script)`.

### Issue 2: Slot Array Not Large Enough
**Root Cause**: When multiple loops are nested, the outer loop's lazy initialization created a slot array with only `LoopEnvSlotIndex + 1` slots. The inner loop, which has a higher slot index, would find `HasSlots=true` but `SlotCount` too small.

**Fix**: Changed the lazy initialization condition from `!functionEnv.HasSlots` to `!functionEnv.HasSlots || functionEnv.SlotCount <= plan.LoopEnvSlotIndex`. This ensures the slot array is expanded if needed.

### Issue 3: Calling InitializeSlots in InvokeWithContext Breaks Closures
**Root Cause**: `InvokeWithContext` stores parameters in the dictionary (not slots). If we call `InitializeSlots` (which fills with `Undefined`), slot lookups succeed with wrong values instead of falling back to dictionary.

**Fix**: Keep using lazy initialization in loop code. Don't initialize slots in `InvokeWithContext`.

## Key Invariants

1. **FunctionExpression.SlotCount** includes all slots needed for variables AND loop env caches
2. **ScopeAnalyzer** allocates loop env slots via `functionScope.AllocateAnonymousSlot()`
3. **InvokeWithContext** does NOT initialize slots (parameters are in dictionary)
4. **InvokeSimpleFast** DOES initialize slots (parameters go directly in slots)
5. **FindByScopeId** returns the first environment in the chain matching the ScopeId
6. **Lazy slot initialization** must expand if existing array is too small
