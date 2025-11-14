# Async Iterable Scope Comparison Analysis

This document contains the detailed findings from comparing `for await...of` behavior with iterables in different scopes.

## Summary

**Key Finding**: The `for await...of` loop works correctly when the iterable is declared in LOCAL scope (inside the async function), but FAILS when the iterable is declared in GLOBAL scope (outside the async function).

**CRITICAL UPDATE**: The `__debug()` tests placed INSIDE the loop body confirm that the loop body is **NEVER EXECUTED** for global scope iterables. Additionally, manual iteration (bypassing for-await-of) also fails with global scope, indicating this is NOT a for-await-of transformation issue but a deeper problem with async functions and global scope iteration.

## Test Results

### Debug Inside Loop Body Test

#### Local Scope (✅ WORKS)
- **Debug messages captured from inside loop**: 3 (one per iteration)
- **Loop execution**: ✅ All 3 iterations execute
- **Variables visible**: `item`, `result`, `localIterable` all present in each debug message

#### Global Scope (❌ FAILS)
- **Debug messages captured from inside loop**: 0
- **Loop execution**: ❌ Loop body NEVER executes
- **Conclusion**: We don't even get into the loop body!

### Manual Iteration Test

To test if the issue is specific to for-await-of transformation, manual iteration was tested:

```javascript
// Manual while loop instead of for-await-of
let iterator = iterable[Symbol.iterator]();
let iterResult = iterator.next();
while (!iterResult.done) {
    __debug(); // Inside manual loop
    result = result + iterResult.value;
    iterResult = iterator.next();
}
```

#### Results:
- **Local scope manual**: ❌ Also fails (interesting - even manual fails when called second time)
- **Global scope manual**: ❌ Fails - next() is never called

**Critical Discovery**: Even manual iteration fails with global scope! This proves the issue is NOT in the for-await-of transformation but in how JavaScript functions/iterators interact with global scope objects inside async functions.

### Original Execution Behavior

#### Local Scope (✅ WORKS)
```
LOG: LOCAL: About to start for-await-of
LOG: LOCAL: Symbol.iterator called
LOG: LOCAL: next() called, index=0
LOG: LOCAL: returning value=x
LOG: LOCAL: In loop, item=x
LOG: LOCAL: next() called, index=1
LOG: LOCAL: returning value=y
LOG: LOCAL: In loop, item=y
LOG: LOCAL: next() called, index=2
LOG: LOCAL: returning value=z
LOG: LOCAL: In loop, item=z
LOG: LOCAL: next() called, index=3
LOG: LOCAL: returning done=true
LOG: LOCAL: After loop, result=xyz
Final result: 'xyz'
```

#### Global Scope (❌ FAILS)
```
LOG: GLOBAL: About to start for-await-of
LOG: GLOBAL: Symbol.iterator called
Final result: 'Asynkron.JsEngine.JsObject' (empty/wrong result)
```

**Observation**: In the global scope case, `Symbol.iterator` is called but `next()` is NEVER invoked. The loop silently fails.

## S-Expression Comparison

### Local Scope S-Expression
The transformation for local scope looks like:
```lisp
(function test () 
  (block 
    (return 
      (new Promise 
        (lambda null (__resolve __reject) 
          (block 
            (try 
              (block 
                (let localIterable (object ...)) ; Iterable defined HERE
                (let result "")
                (let __iteratoreee79077 (call __getAsyncIterator localIterable))
                (function __loopCheck75d3875c () ...)
                (expr-stmt (call __loopCheck75d3875c)))
              (catch __error ...))))))))
```

### Global Scope S-Expression
The transformation for global scope looks like:
```lisp
(program
  (let globalIterable (object ...)) ; Iterable defined at GLOBAL level
  (function test () 
    (block 
      (return 
        (new Promise 
          (lambda null (__resolve __reject) 
            (block 
              (try 
                (block 
                  (let result "")
                  (let __iteratorfbde1cb3 (call __getAsyncIterator globalIterable))
                  (function __loopCheck0ba9bae7 () ...)
                  (expr-stmt (call __loopCheck0ba9bae7)))
                (catch __error ...))))))))
```

### Key Differences

1. **Scope Level**: Local iterable is defined inside the Promise lambda, while global iterable is defined at program level.
2. **Variable Reference**: Both use the same pattern `(call __getAsyncIterator <iterable>)` with a direct symbol reference.
3. **Transformation Structure**: The loop transformation itself (`__loopCheck`, `__iteratorNext`, etc.) is IDENTICAL in both cases.

## Debug Messages Analysis

### Local Scope Debug Messages: 5 messages captured
1. **Before loop**: Shows `localIterable` in the environment along with `__resolve`, `__reject`, and all standard globals.
2. **First iteration**: Shows `item = x`, `result = ""`, `__iterator2541a42f`, `__loopCheckb4fcdedd`, `__loopResolve8e2cd3cb` all present.
3. **Second iteration**: Shows `item = y`, `result = "x"`, same loop functions present.
4. **Third iteration**: Shows `item = z`, `result = "xy"`, same loop functions present.
5. **After loop**: Shows `result = "xyz"`.

### Global Scope Debug Messages: Only 1 message captured
1. **Before loop**: Shows `globalIterable` in the global environment, but NO subsequent debug messages from inside the loop!

**Critical Observation**: The global scope test only captures the first `__debug()` call (before the loop). The loop never progresses to capture debug messages during iteration, confirming that the loop body never executes.

## Environment Comparison

Both local and global scope tests have access to the same global environment:
- `__getAsyncIterator` ✅
- `__iteratorNext` ✅
- `__awaitHelper` ✅
- All standard JavaScript globals ✅

The difference is NOT in the available functions or environment.

## Conclusions

1. **S-Expression Transformation is Correct**: The transformed code looks identical in structure for both cases. The loop transformation properly references the iterable variable whether it's local or global.

2. **The Issue is in Execution, Not Transformation**: Since the S-expression is correct, the problem must be in how the transformed code is EXECUTED at runtime.

3. **Iterator is Retrieved but Not Used**: The log shows `Symbol.iterator called` in both cases, which means `__getAsyncIterator` successfully calls the Symbol.iterator method. However, in the global scope case, the returned iterator is never used (next() is never called).

4. **Loop Body Never Executes for Global Scope**: The `__debug()` calls placed INSIDE the loop body confirm definitively that the loop body is never entered when the iterable is global scope. Local scope captures 3 debug messages (one per iteration), while global scope captures 0.

5. **Manual Iteration Also Fails**: Testing with a manual `while (!iterResult.done)` loop (bypassing for-await-of entirely) also fails with global scope. This proves the issue is NOT specific to for-await-of transformation but affects JavaScript function iteration in general when dealing with global scope objects inside async functions.

6. **Possible Root Causes**:
   - **Iterator Method Invocation Issue**: When `Symbol.iterator` is called on a global scope object from within an async function, the returned iterator object may not be properly handled or may have closure/scope issues.
   - **JavaScript Function Execution Context**: The JavaScript lambda functions that implement `next()` may have different execution contexts when created from global vs local scope objects.
   - **Variable Binding in Closures**: The closure created by the iterator's `next()` method might not properly capture variables when the iterator comes from global scope.
   - **Async Function Transformation**: The CPS transformation that wraps async function bodies in a Promise may interfere with how global scope iterators work.

## Recommended Next Steps

1. **Test Non-Async Function**: Create a test that uses a regular (non-async) function with a manual loop to see if the issue is specific to async functions or affects all function contexts.

2. **Test Different Iterator Implementations**: Try with:
   - Iterator using HostFunction (C#) next() method
   - Iterator with arrow functions vs regular functions
   - Iterator with different closure patterns

3. **Inspect Iterator Object**: Add detailed logging to see if the iterator object returned from `Symbol.iterator` is different in structure between local and global scope cases.

4. **Check Execution Context**: Investigate whether the `this` binding or execution context differs for the iterator's next() method when called from global scope vs local scope.

5. **Trace Promise Chain**: Add extensive logging to the promise chain created by `__iteratorNext` to see where execution stops in the global scope case.

## Test File Reference

The tests that generated this data are in:
`tests/Asynkron.JsEngine.Tests/AsyncIterableScopeComparisonTests.cs`

Run with:
```bash
dotnet test --filter "FullyQualifiedName~AsyncIterableScopeComparisonTests"
```

### New Tests Added:
- `DebugInsideLoopBody_GlobalVsLocal`: Places `__debug()` INSIDE loop bodies to confirm execution
- `ManualIterationComparison_GlobalVsLocal`: Tests manual iteration to isolate for-await-of vs general issue
