## PRIORITY SUBTASKS (current focus)

1. Implement Unified SlotAnalyzer pass post-IR build (params → IR internals → user vars)
2. Remove early AST scope/slot stamping; ensure IR build is independent
3. Update evaluator/env to use new slot layout; fix iterator temp env resolution via plan.RootScopeId
4. Add logging asserts for slot hits/misses in loops/functions; validate no collisions
5. Run ./test.sh, then targeted for-of Test262 subset; iterate until failures drop

---

There are a few test failures right now:
1 internal test fails now, and ~550 Language Tests (262 tests) fail.
Many many of those are ForOf tests.
Not all ForOf tests, but a large subset.

But the 262 test suite has 500+ failures, here is a subset of those:
-----
        Statements_forOf("language/statements/for-of/arguments-mapped-aliasing.js",False)
        Statements_forOf("language/statements/for-of/array-contract-expand.js",False)
        Statements_forOf("language/statements/for-of/head-lhs-cover.js",False)
        Statements_forOf("language/statements/for-of/head-lhs-member.js",False)
        Statements_forOf_dstr("language/statements/for-of/dstr/array-rest-yield-ident-valid.js",False)
        Statements_forOf_dstr("language/statements/for-of/dstr/const-ary-init-iter-close.js",False)
        Statements_forOf_dstr("language/statements/for-of/dstr/const-ary-ptrn-elem-ary-elem-init.js",False)
        Statements_forOf_dstr("language/statements/for-of/dstr/const-ary-ptrn-elem-ary-elem-iter.js",False)
        Statements_forOf_dstr("language/statements/for-of/dstr/const-ary-ptrn-elem-ary-elision-init.js",False)
-----


These are the result of our recent work on "slot stamping", we are trying to get our scope and slot analyzis to work and correctly stamp slots on identifiers.
So that they can resolve variables in their correct scope without having to do a full lookup.

Aim to get to 0 errors.

1. You may not replace IR with AST evaluation.
2. you may not disable tests.
3. You may not disable slot stamping.
4. You may not disable optimizations.

If you need to make a plan first, do that, its OK.
If you need more IR instructions, add that, its OK.
If you need to change existing IR instructions, do that, its OK.
If you need to change the slot stamping logic, do that, its OK.
If you need to change the evaluator logic, do that, its OK.

-----

## Architectural Plan: Unified Slot Analysis

### Problem

The current architecture has a fundamental ordering problem:

1. Parse → AST
2. Scope analyze AST (stamps scope IDs before IR exists)
3. Build IR (references AST nodes that already have scope info)
4. IR slot analysis (tries to fit IR symbols into existing scheme)
5. Conflicts arise because IR symbols weren't known during step 2

This causes slot collisions between IR internal symbols (`__forOf_iter_N`) and user variables.

### Solution

**AST should be blank at first. Nothing done to it.**

Correct flow:

1. Parse → AST (blank - no scope IDs, no slot indices)
2. Build IR for functions/generators/scripts
3. Now we have full picture: IR instructions + AST nodes they reference
4. Single unified scope/slot analysis pass:
   - Assign scope IDs to all scopes (function, block, iteration, etc.)
   - Within each scope, assign slots in order:
     1. Function params first (slots 0, 1, 2...)
     2. IR internal symbols (`__forOf_iter_N`, `__forOf_value_N`, etc.)
     3. User variables in that scope

### Why This Works

- **One source of truth** - Slot indices are assigned with full knowledge of everything
- **No collisions** - IR symbols and user variables are in the same assignment pass
- **Deterministic** - Consistent ordering means predictable slot layout

### Key Insight

**IR building must precede slot analysis**, not the other way around. You can't assign slots correctly until you know what the IR needs.

### Implementation Steps

1. Remove early scope stamping from AST parsing
2. Ensure IR building doesn't depend on pre-stamped AST nodes
3. Create unified `SlotAnalyzer` that walks IR + referenced AST together
4. Assign slots in deterministic order within each scope
5. Update evaluator to use the new slot layout
