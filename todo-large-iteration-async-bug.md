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

## Key Findings

### Exact Threshold
- 998 outer iterations: works
- 999+ outer iterations: fails silently (no error, just stops)

### Comparison with Simple Await
- `for (let i = 0; i < 2000; i++) { await Promise.resolve(); }` - **WORKS** (2000 iterations)
- `for (let i = 0; i < 2000; i++) { for await (const n of [1]) { ... } }` - **FAILS** at 998

The issue is specific to `for await...of`, not general async/await.

### Behavior at Failure
```javascript
var lastI = -1;
for (let i = 0; i < 1001; i++) {
    lastI = i;
    for await (const n of arr) { ... }
}
// lastI = 998, meaning iteration 999 (i=999) never executes its inner loop
```

The loop runs 999 times successfully (i=0 to i=998), but on the 1000th iteration (i=999), the for-await-of inner loop doesn't execute and the async function silently stops.

### No Errors Logged
No exceptions are thrown. No errors in the logger. The async function just stops progressing.

## Hypothesis

The threshold of 999 iterations correlates with the environment chain depth limit (1000) or the `MaxCallDepth = 1000` setting. The environment chain walking fix may be creating a very deep chain that hits this limit.

## Related Test

- `ForAwaitOf_InIIFE_MultipleIterations_DoesNotComplete2` in `MicrotaskDrainingTests.cs`

## Status

**INVESTIGATING** - Issue is specific to for-await-of with 999+ outer iterations
