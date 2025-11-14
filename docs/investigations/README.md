# Investigation & Debugging Documentation

This folder contains investigation notes, debugging plans, and analysis documents from various development efforts on the Asynkron.JsEngine.

## Current Investigations

### Critical Parser & CPS Transformer Issues

#### [PARSER_VS_CPS_ANALYSIS.md](PARSER_VS_CPS_ANALYSIS.md)
**Investigation Question:** Is this a parser issue or a CPS transformer issue?

**Key Findings:**
- Parser correctly handles method shorthand syntax ✅
- Method shorthand works in isolation ✅
- Fails in CPS-transformed for-await-of loops ❌
- Issue is at the intersection of global scope, method shorthand, CPS transformation, and runtime invocation

**Conclusion:** NOT a parser issue - this IS a CPS transformer/runtime issue with how global scope functions are invoked from within CPS-transformed async code.

#### [PROMISE_REJECTION_INVESTIGATION.md](PROMISE_REJECTION_INVESTIGATION.md)
**Investigation Focus:** Promise rejection handling in for-await-of loops

**Critical Discovery:**
- Function body is NOT empty ✅
- next() IS being called ✅
- Loop DOES enter ✅
- But promise rejections don't propagate ❌

**Key Insight:** Issue is specific to method shorthand in Symbol.iterator context.

#### [EXCEPTION_CHANNEL_RESULTS.md](EXCEPTION_CHANNEL_RESULTS.md)
**Implementation:** Exception channel for capturing unhandled exceptions

**Success:**
- Exception channel successfully captures exceptions ✅
- Exception type: `InvalidOperationException` - "The empty list does not have a head."
- Context: "Iterator.next() invocation"
- Root cause confirmed: function body is corrupted/empty Cons

### Async Iteration & CPS Transformation

#### [CPS_ASYNC_ITERATION_STATUS.md](CPS_ASYNC_ITERATION_STATUS.md)
**Status:** ❌ NOT FULLY FIXED (as of November 2025)

**What Works:**
- for-await-of outside async functions ✅
- for-await-of inside async with simple iterables (arrays, strings) ✅
- 10 basic async iteration tests pass ✅

**What Doesn't Work:**
- CPS transformer only transforms for-await-of when loop BODY contains `await`
- Fails with generators in async context
- Custom async iterators not properly handled
- Promise rejections not propagated
- Sync iterators fail in async context

#### [DEBUGGING_PLAN.md](DEBUGGING_PLAN.md)
**Problem:** for-await-of loops with sync iterators from global scope

**Critical Breakthrough:** "The empty list does not have a head."
- Error occurs when calling next() on iterators from global scope
- Symbol.iterator IS called ✅
- Iterator object IS returned ✅
- But calling next() throws Cons-related error ❌

**Root Cause:** Global scope function definitions are corrupted or not properly stored - function body becomes empty Cons.

#### [CPS_LOOP_DEBUGGING_NOTES.md](CPS_LOOP_DEBUGGING_NOTES.md)
Additional debugging notes and findings from CPS loop transformation work.

### Scope & Environment Issues

#### [ASYNC_ITERABLE_SCOPE_COMPARISON.md](ASYNC_ITERABLE_SCOPE_COMPARISON.md)
**Key Finding:** for-await-of works with LOCAL scope iterables but FAILS with GLOBAL scope iterables

**Evidence:**
- Local scope: Loop body executes 3 times ✅
- Global scope: Loop body NEVER executes ❌
- Manual iteration also fails with global scope ❌

**Conclusion:** Not a for-await-of transformation issue - deeper problem with async functions and global scope iteration.

## SunSpider Test Suite Analysis

#### [SUNSPIDER_ANALYSIS.md](SUNSPIDER_ANALYSIS.md)
Analysis of SunSpider benchmark test failures with improved error messages.

**Results:** 9 passing / 17 failing

**Failure Categories:**
1. Parse errors (7 tests) - ASI, minified code, complex expressions
2. Runtime errors (10 tests) - missing features, type errors

#### [SUNSPIDER_TEST_FINDINGS.md](SUNSPIDER_TEST_FINDINGS.md)
Detailed findings from SunSpider test runs.

#### [SUNSPIDER_UPDATE_2025.md](SUNSPIDER_UPDATE_2025.md)
2025 update on SunSpider test compatibility status.

---

## Investigation Timeline

All investigations are from November 2025 development cycle, focusing on:
1. Async iteration edge cases
2. CPS transformer limitations
3. Global vs local scope behavior differences
4. Parser correctness validation
5. Exception handling and debugging improvements

## Related Documentation

- [CPS_TRANSFORMATION_PLAN.md](../CPS_TRANSFORMATION_PLAN.md) - Overall CPS transformation strategy
- [ASYNC_AWAIT_IMPLEMENTATION.md](../ASYNC_AWAIT_IMPLEMENTATION.md) - Async/await implementation details
- [ARCHITECTURE.md](../ARCHITECTURE.md) - System architecture overview

---

**Note:** These investigations represent ongoing debugging efforts. Some issues may have been resolved since the investigation was documented. Always check the latest test results and code comments for current status.
