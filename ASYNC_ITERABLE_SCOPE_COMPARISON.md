# Async Iterable Scope Comparison Analysis

This document contains the detailed findings from comparing `for await...of` behavior with iterables in different scopes.

## Summary

**Key Finding**: The `for await...of` loop works correctly when the iterable is declared in LOCAL scope (inside the async function), but FAILS when the iterable is declared in GLOBAL scope (outside the async function).

## Test Results

### Execution Behavior

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

4. **Promise Chain May Not Execute**: The most likely explanation is that the promise chain created by the loop (`__iteratorNext(...).then(...).catch(...)`) is not being properly scheduled or executed when dealing with iterables from outer scopes.

5. **Possible Root Causes**:
   - **Scope Chain Issue**: The lambda functions inside the Promise (`__loopCheck`, `__loopResolve`) may not properly capture or access variables from the global scope vs local scope.
   - **Promise Scheduling**: When promises are resolved synchronously (as they are with `__iteratorNext` wrapping sync iterator results), the event loop behavior might differ based on scope context.
   - **Variable Binding**: The way JavaScript function closures work inside the CPS-transformed Promise lambda might have different behavior for global vs local variables.

## Recommended Next Steps

1. **Add More Debug Logging**: Instrument the `__getAsyncIterator` and `__iteratorNext` C# helpers to log when they're called and what they return.

2. **Test Promise Chain Directly**: Create a minimal test that manually constructs the same promise chain as the transformed loop, using global variables, to isolate whether it's a promise issue or a transformation issue.

3. **Check Promise Resolution**: Verify that promises created by `__iteratorNext` are actually being resolved and their `.then()` callbacks are being scheduled when the iterable is from global scope.

4. **Investigate Scope Chain**: Look into how the Evaluator handles variable lookup for the lambda functions created inside the Promise constructor when those lambdas reference variables from different scopes.

## Test File Reference

The tests that generated this data are in:
`tests/Asynkron.JsEngine.Tests/AsyncIterableScopeComparisonTests.cs`

Run with:
```bash
dotnet test --filter "FullyQualifiedName~AsyncIterableScopeComparisonTests"
```
