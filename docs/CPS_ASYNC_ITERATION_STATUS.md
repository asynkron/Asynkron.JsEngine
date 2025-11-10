# CPS Transformer Status for Async Iteration

**Date:** November 2025  
**Status:** ❌ NOT FULLY FIXED

## Investigation Summary

Investigated whether the async loop CPS transformer is fully fixed by unskipping and testing 5 previously skipped tests in `AsyncIterationTests.cs`.

### Result: Not Fixed

All 5 tests fail when unskipped, confirming the CPS transformer has known limitations that prevent certain async iteration patterns from working.

## Test Results

| Test | Status | Issue |
|------|--------|-------|
| `ForAwaitOf_WithGenerator` | ❌ FAILS | `sum = 0` instead of `6` - loop doesn't execute |
| `ForAwaitOf_WithCustomAsyncIterator` | ❌ FAILS | `result = ""` (empty) - promises from `next()` not awaited |
| `ForAwaitOf_ErrorPropagation` | ❌ FAILS | `errorCaught = false` - promise rejections not handled |
| `ForAwaitOf_SyncErrorPropagation` | ❌ FAILS | `errorCaught = false` - sync exceptions not caught |
| `ForAwaitOf_FallbackToSyncIterator` | ❌ FAILS | `result = ""` (empty) - sync iterator doesn't work in async context |

## What Works ✅

1. **for-await-of outside async functions**: Works perfectly for all iterable types
2. **for-await-of inside async with simple iterables**: Arrays and strings work when body has no `await`
3. **10 other async iteration tests pass**: Basic async iteration functionality is solid

Example that works:
```javascript
async function test() {
    for await (let char of "hello") {  // ✅ Works!
        result = result + char;
    }
}
```

## What Doesn't Work ❌

The CPS transformer only transforms `for-await-of` when the loop BODY contains `await`. This check is at **line 349-360** in `CpsTransformer.cs`:

```csharp
// Check if body contains await
var parts = ConsList(forAwaitCons);
if (parts.Count >= 4 && ContainsAwait(parts[3]))
{
    // Transform for-await-of with await in body
    return TransformForOfWithAwaitInBody(...);
}
```

This misses cases where:
1. The iterator's `next()` method returns promises (async iterators)
2. Iterating over generators (which may yield promises)
3. Using sync iterators that need CPS context for proper sequencing

Example that fails:
```javascript
async function test() {
    for await (let num of generator()) {  // ❌ Fails! No await in body
        sum = sum + num;  // This never executes
    }
}
```

## Root Cause

### Problem 1: Transform Detection
The CPS transformer checks if the loop BODY contains `await`, but doesn't detect when the ITERATOR itself returns promises or needs async handling.

### Problem 2: Promise Handling in Loop
Even when transformed, the `BuildCpsLoopCheck` method calls `iterator.next()` and immediately accesses `.done` and `.value` without waiting for promises:

```javascript
// Current implementation (simplified)
let __result = __iterator.next();  // May return a promise!
if (__result.done) {  // ❌ Accesses .done before promise resolves
    // ...
}
```

For async iterators where `next()` returns `Promise.resolve({value: x, done: false})`, this fails.

### Problem 3: Evaluator Limitation
When the transformer doesn't handle it, the evaluator throws at **line 560** in `Evaluator.cs`:

```csharp
throw new InvalidOperationException(
    "Async iteration with promises requires async function context. " +
    "Use for await...of inside an async function."
);
```

But we ARE inside an async function! The issue is the transformer didn't recognize it needs transformation.

## Technical Details

### Current CPS Transform Flow
1. Parser creates `(for-await-of (let variable) iterable body)` S-expression
2. CPS transformer checks if BODY contains `await` keyword
3. If YES: transforms to CPS loop with iterator protocol
4. If NO: passes through to evaluator
5. Evaluator tries to run synchronously
6. Fails if iterator returns promises

### What Needs to Be Fixed

To fully support async iteration in CPS-transformed functions:

1. **Always transform for-await-of in async functions**
   - Don't check if body has await
   - Any `for-await-of` in async context needs transformation
   
2. **Handle promise-returning next()**
   - Wrap `iterator.next()` result in `Promise.resolve()`
   - Use `.then()` to check `.done` and extract `.value`
   - Chain properly with rest of loop
   
3. **Support Symbol.asyncIterator protocol**
   - Check for `Symbol.asyncIterator` first
   - Fall back to `Symbol.iterator`
   - Handle both sync and async iterator results

4. **Proper error propagation**
   - Catch promise rejections from `next()`
   - Propagate to async function's reject handler
   - Handle sync exceptions in try-catch

### Estimated Effort
- **Detection fix**: 2-4 hours
- **Promise handling**: 8-12 hours  
- **Error propagation**: 3-5 hours
- **Testing & edge cases**: 4-6 hours
- **Total**: ~20-25 hours

## Workarounds

Until the CPS transformer is fixed, users can:

1. **Use regular for-of with explicit await**:
   ```javascript
   async function test() {
       for (let promise of promises) {
           let value = await promise;  // Explicit await works
           // use value
       }
   }
   ```

2. **Use Promise.all for batching**:
   ```javascript
   async function test() {
       const values = await Promise.all(promises);
       for (let value of values) {
           // use value
       }
   }
   ```

3. **Use reduce for sequential processing**:
   ```javascript
   async function test() {
       await promises.reduce(async (prev, promise) => {
           await prev;
           const value = await promise;
           // process value
       }, Promise.resolve());
   }
   ```

## Related Files

- **Tests**: `tests/Asynkron.JsEngine.Tests/AsyncIterationTests.cs`
- **CPS Transformer**: `src/Asynkron.JsEngine/CpsTransformer.cs` (lines 349-360, 1089-1342)
- **Evaluator**: `src/Asynkron.JsEngine/Evaluator.cs` (lines 486-650)
- **Documentation**:
  - `docs/CPS_LOOP_DEBUGGING_NOTES.md`
  - `docs/CPS_TRANSFORMATION_FOR_LOOPS.md`
  - `docs/LARGE_FEATURES_NOT_IMPLEMENTED.md`

## Recommendations

### For Users
- Use workarounds listed above
- Avoid `for-await-of` with async iterators or generators inside async functions
- Use explicit `await` in loop body instead

### For Contributors
- Fixing this requires deep knowledge of CPS transformation
- Start with detection fix (simplest)
- Add tests incrementally
- Consider the complexity vs. benefit tradeoff
- Most use cases have simple workarounds

## Conclusion

The async loop CPS transformer is **not fully fixed**. While basic async iteration works, edge cases involving async iterators, generators, and promise-returning iterator protocols fail. The tests remain skipped with updated documentation describing the specific failures.

The existing workarounds are sufficient for most use cases, and fixing the transformer would require significant effort (20-25 hours) for scenarios that can be handled with simple code patterns.

---

**Last Updated**: November 10, 2025  
**Document Version**: 1.0  
**Investigation By**: GitHub Copilot Agent
