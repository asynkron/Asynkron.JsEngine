# Debugging Plan: Global Scope Iterator Failure

## Problem Statement
When `for await...of` loops iterate over sync iterators from global scope objects, the iterator's `next()` method is never called. The loop body never executes.

## Investigation Strategy

### Phase 1: Isolate the Failure Point ✅

Created `AsyncIteratorDebuggingTests.cs` with 8 progressive tests:

1. **DirectIteratorCall_GlobalScope**: Direct call to global iterator (baseline)
2. **IteratorCallFromAsyncFunction_NoPromiseWrapper**: Call from async function
3. **IteratorCallFromPromiseCallback**: Call from Promise executor
4. **IteratorCallFromNestedPromiseChain**: Call from nested Promise.then() chain
5. **IteratorWithClosureVariables_GlobalScope**: Iterator with closure variables
6. **UseActualHelpers_GlobalIterator**: Using `__getAsyncIterator` and `__iteratorNext`
7. **CompareLocalVsGlobal_MinimalCase**: Side-by-side comparison
8. **InstrumentedIteratorNext_DetailedLogging**: Using built-in `__iteratorNext` with logging

### Initial Test Results

**Test 7 (CompareLocalVsGlobal_MinimalCase)**: ❓ **BOTH LOCAL AND GLOBAL WORK!**

This is surprising and important:
```javascript
// This WORKS for both local and global scope:
let iterator = { next: () => ({ value: 1, done: false }) };
async function test() {
  return new Promise(resolve => {
    Promise.resolve().then(() => {
      let result = iterator.next();  // WORKS!
      resolve(result);
    });
  });
}
```

**Conclusion from Phase 1**: The issue is NOT simply "global scope iterators don't work in Promise chains". Simple iterator calls DO work. The problem must be more specific to the for-await-of loop structure.

## Phase 2: Analyze the Difference

### What's Different in For-Await-Of?

The CPS-transformed for-await-of creates a more complex structure:

1. **Multiple nested functions**: `__loopCheck`, `__loopResolve`
2. **Recursive calls**: The loop continuation calls `__loopCheck` again
3. **Helper function involvement**: Uses `__getAsyncIterator` and `__iteratorNext`
4. **Iterator creation**: The iterator is created via `Symbol.iterator()` call

### Hypotheses to Test

**Hypothesis 1: Symbol.iterator() Call Issue**
- Maybe calling `Symbol.iterator()` on a global object returns an iterator with broken closure
- The iterator object itself might be malformed

**Hypothesis 2: __getAsyncIterator Wrapper Issue**
- The helper might not properly handle iterators from global scope objects
- Something about how it invokes `Symbol.iterator` breaks the iterator

**Hypothesis 3: Recursive Function Call Issue**
- The `__loopCheck` → `__loopResolve` → `__loopCheck` recursion might lose context
- Deep nesting of promises in the recursive structure might cause issues

**Hypothesis 4: Iterator State Corruption**
- The iterator object might be passed through too many contexts
- Closure variables (like `index`) might not be accessible after being passed around

## Phase 3: Targeted Tests (TODO)

### Test A: Symbol.iterator() Direct Call
```javascript
let globalIterable = { [Symbol.iterator]() { /* ... */ } };
async function test() {
  let iter = globalIterable[Symbol.iterator]();
  // Does calling next() on this iter work?
  return iter.next();
}
```

### Test B: __getAsyncIterator Direct Test
```javascript
let globalIterable = { [Symbol.iterator]() { /* ... */ } };
async function test() {
  let iter = __getAsyncIterator(globalIterable);
  // Does this iter work in Promise chain?
  return new Promise(resolve => {
    Promise.resolve().then(() => resolve(iter.next()));
  });
}
```

### Test C: Recursive Promise Chain
```javascript
let globalIter = { next: () => ({ value: 1, done: false }) };
async function test() {
  function loop() {
    return new Promise(resolve => {
      Promise.resolve().then(() => {
        let result = globalIter.next();
        if (result.done) resolve();
        else loop();  // Recursive
      });
    });
  }
  return loop();
}
```

### Test D: Compare Iterator Creation Methods
```javascript
// Method 1: Direct object with next
let iter1 = { next: () => ({}) };

// Method 2: Via Symbol.iterator
let iterable = { [Symbol.iterator]() { return { next: () => ({}) }; } };
let iter2 = iterable[Symbol.iterator]();

// Do both work the same in async/Promise contexts?
```

## Phase 4: Deep Instrumentation (TODO)

### Option A: Modify StandardLibrary.cs
Add detailed logging to:
- `CreateGetAsyncIteratorHelper()` at lines 3420-3460
- `CreateIteratorNextHelper()` at lines 3467-3505

Track:
- When methods are called
- What objects are passed
- What gets returned
- Any exceptions thrown

### Option B: Environment Chain Inspector
Create tool to dump the full environment chain when `next()` is about to be invoked:
- What variables are in scope?
- What's the closure chain?
- Is the iterator object still valid?

### Option C: Promise Scheduling Tracer
Log every promise creation, resolution, and callback scheduling:
- When are `.then()` callbacks queued?
- When do they actually execute?
- Are any being dropped/skipped?

## Expected Outcomes

By the end of this investigation, we should know:
1. **Exactly where** execution stops (which function call fails)
2. **Why** it fails (exception? silent return? lost reference?)
3. **What's different** between working (local scope) and failing (global scope) cases
4. **How to fix** it (environment injection? closure fixing? different invocation pattern?)

## Next Steps

1. Run tests A, B, C, D from Phase 3
2. Based on results, implement deep instrumentation from Phase 4
3. Document the exact failure point and mechanism
4. Propose and test a fix
