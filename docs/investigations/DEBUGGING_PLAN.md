# Debugging Plan: Global Scope Iterator Failure

## Problem Statement
When `for await...of` loops iterate over sync iterators from global scope objects, the iterator's `next()` method is never called. The loop body never executes.

## CRITICAL BREAKTHROUGH ✅

**Error Found**: "The empty list does not have a head."

This error occurs when:
1. Calling `next()` on an iterator created from a global scope object
2. Even when calling directly (not just in for-await-of)
3. Happens inside async functions when invoking methods on iterators from global scope

### Test Results

**Test F (Actual for-await-of with logging)**:
```
LOG: !!! Symbol.iterator called !!!
LOG: !!! Returning iterator object !!!
LOG: >>> Exception in loop: The empty list does not have a head.
```
- Symbol.iterator IS called ✅
- Iterator object IS returned ✅  
- But calling `next()` throws "The empty list does not have a head" ❌

**Test A (Symbol.iterator direct call)**:
```
LOG: Symbol.iterator method called
LOG: Got iterator: object
LOG: Iterator has next: function
LOG: Calling next() on iterator
LOG: Error: The empty list does not have a head.
```
- Same error even without for-await-of ✅
- Issue is with calling methods on objects created from global scope

## Root Cause Analysis

"The empty list does not have a head" is a Cons-related error from the S-expression evaluator. This suggests:

1. **The iterator's `next()` function has malformed S-expression body**
2. **The function body is an empty Cons list somehow**
3. **Global scope function definitions are being corrupted or not properly stored**

### Where This Error Comes From

The error "The empty list does not have a head" comes from `Cons.cs` when trying to access `.Head` on an empty Cons list. This happens during evaluation when:
- The evaluator tries to execute a function body
- The body is expected to be a non-empty Cons
- But it's actually an empty Cons

### Why Global Scope Is Different

When a function (like `next()`) is defined in global scope:
- It's parsed and stored with its body as S-expression
- When accessed from within an async function context
- Something corrupts or loses the function body
- Resulting in an empty Cons when the function tries to execute

## Investigation Status: FAILURE POINT IDENTIFIED ✅

We now know:
1. **When it fails**: When calling any method on an object created in global scope from within an async function
2. **What error**: "The empty list does not have a head"
3. **Why**: The function body S-expression is empty/corrupted
4. **Where to look**: Function storage/retrieval mechanism and how it interacts with scope

## Next Steps

1. **Find where function bodies are stored** - JsFunction class, closure handling
2. **Trace function body retrieval** - When `next()` is invoked, how is its body retrieved?
3. **Check if global functions lose their bodies** - Is this specific to certain types of function definitions?
4. **Test with different function types**:
   - Regular function: `function next() {}`
   - Arrow function: `next: () => {}`
   - Method shorthand: `next() {}`

## Implementation Status

### Phase 1: Isolate the Failure Point ✅ COMPLETE

Created `AsyncIteratorDebuggingTests.cs` with comprehensive tests.

**Key Test Results**:
- Test 7 (CompareLocalVsGlobal_MinimalCase): Both work ✅
- Test F (ActualForAwaitOf_WithLogging): Found exception! ✅
- Test A (SymbolIteratorDirectCall): Exception confirmed! ✅

### Phase 2: Analyze the Difference ✅ COMPLETE

**Confirmed**: The issue is NOT about Promise chains or scope access. It's about **function body corruption/loss** for functions defined in global scope objects.

### Phase 3: Targeted Tests ✅ COMPLETE

Added tests A-F:
- ✅ Test A: Symbol.iterator() Direct Call - **EXCEPTION FOUND**
- ✅ Test B: __getAsyncIterator Direct Test
- ✅ Test C: Recursive Promise Chain  
- ✅ Test D: Compare Iterator Creation Methods
- ✅ Test E: Exception Capture
- ✅ Test F: Actual for-await-of - **EXCEPTION FOUND**

### Phase 4: Deep Investigation (IN PROGRESS)

Now that we know it's "The empty list does not have a head", we need to:
1. Find why function bodies become empty Cons
2. Check if it's related to how JsFunction stores/retrieves the body
3. Investigate if closures are being incorrectly captured or lost

## Expected Resolution

The fix will likely involve:
1. Ensuring function bodies are properly retained when functions are stored in global scope objects
2. Fixing how function closures capture their environment
3. Possibly related to how the parser/evaluator handles function expressions in object literals at global scope
