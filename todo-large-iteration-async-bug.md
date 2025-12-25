# Large Iteration Async For-Await-Of Bug

## Problem Statement

The test `ForAwaitOf_InIIFE_MultipleIterations_DoesNotComplete2` fails with 5000 outer iterations. The async function doesn't complete - `finalSum` remains 0.

```javascript
'use strict'
var finalSum = 0;
const arr = [1, 2, 3, 4, 5, 6, 7, 8, 9, 10];
(async function() {
    let sum = 0;
    for (let i = 0; i < 5000; i++) {
        for await (const n of arr) {
            sum += n;
        }
    }
    finalSum = sum;
})();
finalSum;
// Expected: 275000 (55 * 5000)
// Actual: 0
```

## Root Cause Analysis

### The Environment Chain Problem

Each loop iteration was creating a **child environment** of the previous iteration instead of a **sibling**:

```
Expected (siblings - all share same parent):
function scope → loop scope → iteration 0
                            → iteration 1
                            → iteration 2
                            ...

Actual (chain - each is child of previous):
function scope → loop scope → iter 0 → iter 1 → iter 2 → ... → iter 999
```

After 999 iterations, the environment chain depth hit `MaxCallDepth = 1000` and silently stopped.

### Why This Happened

1. **`CreateIterationEnvironmentInstruction`** creates per-iteration environments for `let`/`const` bindings
2. After each iteration, `environment` variable still points to that iteration's environment
3. On next iteration, when creating the new environment, the code was using `environment` as parent
4. Should have used the **loop scope** (the parent of all iteration environments)

### The Fix: ScopeId Matching

Detect if we're in a subsequent iteration by checking if current environment was created by a previous `CreateIterationEnvironmentInstruction`:

```csharp
if (environment.ScopeId == createEnvInstruction.ScopeId)
{
    // Current env is a per-iteration env from previous iteration
    // Go up to the loop scope
    loopScope = environment.Enclosing;
}
else
{
    // First iteration - current env IS the loop scope
    loopScope = environment;
}
```

### Missing Piece: Regular For Loops

`TryBuildLoopPlan` in `SyncGeneratorIrBuilder.cs` was NOT emitting `CreateIterationEnvironmentInstruction` for regular `for` loops with `let` bindings. Only `for-of` and `for-await-of` loops had this.

Added emission for regular for loops:
```csharp
// For lexical declarations (let/const), emit CreateIterationEnvironmentInstruction
if (!plan.PerIterationBindings.IsDefaultOrEmpty)
{
    var createEnvIndex = Append(new CreateIterationEnvironmentInstruction(
        bodyEntry,
        plan.PerIterationBindings,
        plan.IterationScopeId,
        plan.IterationSlotCount,
        slotMapBuilder.ToImmutable()));
    iterationBodyEntry = createEnvIndex;
}
```

## Remaining Issue: Triple Nested Loops

The `NestedForAwaitOf_TripleNested` test still fails:

```javascript
for (let i = 0; i < 2; i++) {
    for (let j = 0; j < 2; j++) {
        for await (const n of arr) {
            sum += n;
        }
    }
}
// Expected: 12, Actual: 3
```

### Why Triple Nested Fails

After async resume (when `for-await-of` yields and resumes):
1. The `environment` variable gets reset to **function scope** (not the per-iteration env)
2. ScopeId matching fails because `environment.ScopeId` doesn't match the instruction's ScopeId
3. We incorrectly treat it as "first iteration" and create a child of function scope

### Potential Fix

For `for-await-of` loops, `_currentDriverState.IteratorVariable.Environment` holds the correct loop scope. Need to:
1. Use `_currentDriverState` for for-await-of loops after async resume
2. Fall back to scopeId matching for regular for loops

The challenge is distinguishing when `_currentDriverState` is relevant vs stale from a nested loop.

## Test Results

| Test | Status |
|------|--------|
| 5000 iteration test | PASSING |
| Double nested | PASSING |
| Triple nested | FAILING (expected 12, got 3) |

## Files Modified

- `src/Asynkron.JsEngine/Execution/SyncGeneratorIrBuilder.cs` - Added `CreateIterationEnvironmentInstruction` emission
- `src/Asynkron.JsEngine/Ast/TypedAstEvaluator.TypedGeneratorInstance.cs` - ScopeId matching logic
