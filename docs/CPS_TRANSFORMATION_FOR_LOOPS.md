# CPS Transformation for Loops with Await Expressions

## Overview

This document captures the investigation, findings, and implementation approach for supporting `for-of` and `for-await-of` loops with `await` expressions in their bodies within async functions.

## Problem Statement

When a `for-of` or `for-await-of` loop appears inside an async function and contains `await` expressions in the loop body, the loop doesn't work correctly because:

1. The loop is evaluated synchronously by `EvaluateForOf` or `EvaluateForAwaitOf`
2. The loop body contains await expressions that need asynchronous handling
3. The synchronous evaluator can't properly handle awaits, causing variables to not update correctly

### Example Test Case

```javascript
async function test() {
    let result = "";
    let promises = [
        Promise.resolve("a"),
        Promise.resolve("b"),
        Promise.resolve("c")
    ];
    
    for await (let promise of promises) {
        let item = await promise;  // await in body
        result = result + item;
    }
    // Expected: result === "abc"
    // Actual: result === "" (empty)
}
```

## Key Insights

### Insight 1: for-of and for-await-of are Equivalent

**From @rogeralsing:** There's no fundamental difference between `for-of` and `for-await-of` when await appears in the body:

```javascript
// These are functionally the same:
for await (let promise of array) { 
    let x = await promise; 
}

for (let promise of array) { 
    let x = await promise; 
}
```

The issue is NOT specific to `for-await-of`—it affects ANY loop construct with await expressions in the body.

### Insight 2: Pure CPS Approach

**From @rogeralsing:** Using Continuation-Passing Style (CPS), we can handle loops by making the loop body's last fragment return the loop head as the next continuation. This schedules the next iteration via the event queue automatically.

Key principle:
- The last fragment of the loop body should either:
  - Return the head of the loop body if we should keep looping
  - Return the next step outside the loop if we're done

## Implementation Approach

### Transformation Strategy

Transform loops with await into a CPS-style recursive function:

```javascript
// Original:
for (let x of iterable) { 
    await something(x); 
}

// Transformed to:
let __iterator = iterable[Symbol.iterator]();
function __loopCheck() {
    let __result = __iterator.next();
    if (__result.done) {
        return [continuation-after-loop];
    } else {
        let x = __result.value;
        [body statements with await]
        return __loopCheck(); // Body's last fragment returns loop head
    }
}
return __loopCheck();
```

### How CPS Handles the Loop

1. **Iterator Setup**: Get the iterator once before the loop
2. **Loop Check Function**: Create a function that:
   - Calls `iterator.next()` to get the next value
   - Checks if iteration is done
   - If done, returns the continuation after the loop
   - If not done, executes the body and returns a call to itself
3. **Body Transformation**: The body statements (including awaits) are passed through `ChainStatementsWithAwaits`, which transforms them into promise chains
4. **Recursive Call**: The `return __loopCheck()` at the end of the body becomes part of the promise chain, scheduling the next iteration via the event queue

### Code Location

The transformation is implemented in `src/Asynkron.JsEngine/CpsTransformer.cs`:

- **Detection**: Lines ~336-360 in `ChainStatementsWithAwaits` detect `for-of` and `for-await-of` statements with await in the body
- **Transformation**: `TransformForOfWithAwaitInBody` method creates the CPS-based loop structure
- **Loop Check**: `BuildCpsLoopCheck` builds the recursive loop check function

## Current Status

### What Works ✅

1. All 8 original tests pass (no regressions)
2. Loops WITHOUT await in the body work correctly
3. Transformation structure is correct per CPS principles
4. Code compiles and builds successfully

### What Doesn't Work ❌

1. `RegularForOf_WithAwaitInBody` test fails (result is empty)
2. `ForAwaitOf_WithPromiseArray` test fails (result is empty)
3. Loop doesn't execute despite correct transformation structure

### Test Cases

#### Passing Tests (8)
- `ForAwaitOf_WithArray` - Simple array iteration without await
- `ForAwaitOf_WithString` - String iteration
- `ForAwaitOf_WithBreak` - Break statement handling
- `ForAwaitOf_WithContinue` - Continue statement handling
- `ForAwaitOf_WithCustomSyncAsyncIterator` - Custom sync iterator
- `ForAwaitOf_WithSyncIteratorNoAsync` - Sync iterator without async function
- `SymbolAsyncIterator_Exists` - Symbol.asyncIterator availability
- `ForAwaitOf_RequiresAsyncFunction` - Validation test

#### Failing Tests (2)
- `RegularForOf_WithAwaitInBody` - Regular for-of with await in body
- `ForAwaitOf_WithPromiseArray` - for-await-of with await in body

#### Skipped Tests (5)
- `ForAwaitOf_WithCustomAsyncIterator` - Async iterator where next() returns promises
- `ForAwaitOf_FallbackToSyncIterator` - Sync iterator in async context
- `ForAwaitOf_ErrorPropagation` - Promise rejection handling
- `ForAwaitOf_SyncErrorPropagation` - Sync error handling
- `ForAwaitOf_WithGenerator` - Generator support

## Investigation Notes

### Possible Issues

The transformation creates the correct structure but doesn't execute. Potential causes:

1. **Function Call Scheduling**: The initial `return __loopCheck()` might not be triggering execution properly in the CPS context
2. **Iterator Access**: The `Symbol.iterator` access might not work correctly in the transformed code
3. **Event Queue Integration**: The recursive call might not be scheduling correctly via the event queue
4. **Promise Chain Issue**: `ChainStatementsWithAwaits` might not be handling the recursive call as expected

### Debugging Steps Needed

1. Verify that `Symbol.iterator` access works in transformed code
2. Check if the function declaration and initial call execute
3. Trace through `ChainStatementsWithAwaits` to see how it handles the recursive call
4. Verify that the recursive call is being transformed into a promise chain
5. Check if there are any silent errors being thrown

## Code Examples

### Current Transformation Output

```javascript
// Input:
async function test() {
    for (let x of [1, 2, 3]) {
        let y = await Promise.resolve(x);
        console.log(y);
    }
}

// After CPS transformation (conceptual):
function test() {
    return new Promise((__resolve, __reject) => {
        let __iterator = [1,2,3][Symbol.iterator]();
        function __loopCheck() {
            let __result = __iterator.next();
            if (__result.done) {
                return __resolve(null);
            } else {
                let x = __result.value;
                // Body transformed by ChainStatementsWithAwaits:
                return Promise.resolve(x).then((y) => {
                    console.log(y);
                    return __loopCheck(); // Recursive call
                });
            }
        }
        return __loopCheck();
    });
}
```

## Next Steps

### Short Term (Debugging)

1. Add instrumentation to verify transformation is being triggered
2. Trace execution through the transformed code
3. Verify iterator retrieval works correctly
4. Check promise chain construction for recursive call

### Medium Term (Fix)

1. Identify the specific integration issue preventing execution
2. Adjust transformation if needed to properly schedule via event queue
3. Validate with incrementally complex test cases
4. Unskip and fix remaining tests one by one

### Long Term (Complete Implementation)

1. Handle break/continue/return statements in loop body
2. Support async iterators (where `next()` returns promises)
3. Implement proper error propagation through promise rejections
4. Support for-await-of with Symbol.asyncIterator protocol
5. Performance optimization and edge case handling

## References

### Related Files

- `src/Asynkron.JsEngine/CpsTransformer.cs` - CPS transformation implementation
- `src/Asynkron.JsEngine/Evaluator.cs` - Synchronous evaluator (lines ~486-650 for ForAwaitOf)
- `tests/Asynkron.JsEngine.Tests/AsyncIterationTests.cs` - Test cases

### Key Commits

- `0d4ac15` - Unskipped ForAwaitOf_WithPromiseArray test
- `1eee196` - Implemented CPS-based loop transformation
- `bcf5f20` - Added test showing for-of has same issue
- `1d9f42f` - Framework for selective transformation

## Conclusion

The CPS approach is conceptually sound and follows proper continuation-passing style principles. The transformation structure is correct: the loop body's continuation points back to the loop check, with the event queue handling scheduling automatically through promise chains.

The implementation is in place but requires debugging to identify why the transformed code doesn't execute despite having the correct structure. Once this integration issue is resolved, the approach should support all required test cases incrementally.

---

**Document Version**: 1.0  
**Last Updated**: 2025-11-09  
**Status**: Work in Progress - Debugging phase
