There are a few test failures right now:
All tests in internal test suite pass.

But the 262 test suite has 500+ failures, here is a subset of those:
-----
       ArgumentsObject
        ArgumentsObject("language/arguments-object/10.6-10-c-ii-1.js",False)
        ArgumentsObject("language/arguments-object/10.6-10-c-ii-2.js",False)
       ArgumentsObject_mapped
        ArgumentsObject_mapped("language/arguments-object/mapped/mapped-arguments-nonconfigurable-nonwritable-3.js",False)
        ArgumentsObject_mapped("language/arguments-object/mapped/mapped-arguments-nonconfigurable-nonwritable-4.js",False)
        ArgumentsObject_mapped("language/arguments-object/mapped/mapped-arguments-nonconfigurable-nonwritable-5.js",False)
        ArgumentsObject_mapped("language/arguments-object/mapped/mapped-arguments-nonconfigurable-strict-delete-2.js",False)
        ArgumentsObject_mapped("language/arguments-object/mapped/mapped-arguments-nonconfigurable-strict-delete-3.js",False)
        ArgumentsObject_mapped("language/arguments-object/mapped/mapped-arguments-nonconfigurable-strict-delete-4.js",False)
        ArgumentsObject_mapped("language/arguments-object/mapped/nonconfigurable-descriptors-define-failure.js",False)
        ArgumentsObject_mapped("language/arguments-object/mapped/nonconfigurable-descriptors-set-value-by-arguments.js",False)
        ArgumentsObject_mapped("language/arguments-object/mapped/nonconfigurable-descriptors-set-value-with-define-property.js",False)
        ArgumentsObject_mapped("language/arguments-object/mapped/nonconfigurable-descriptors-with-param-assign.js",False)
       BlockScope_shadowing
        BlockScope_shadowing("language/block-scope/shadowing/const-declarations-shadowing-parameter-name-let-const-and-var-variables.js",False)
        BlockScope_shadowing("language/block-scope/shadowing/const-declarations-shadowing-parameter-name-let-const-and-var-variables.js",True)
        BlockScope_shadowing("language/block-scope/shadowing/dynamic-lookup-from-closure.js",False)
        BlockScope_shadowing("language/block-scope/shadowing/dynamic-lookup-from-closure.js",True)
        BlockScope_shadowing("language/block-scope/shadowing/let-declarations-shadowing-parameter-name-let-const-and-var.js",False)
        BlockScope_shadowing("language/block-scope/shadowing/let-declarations-shadowing-parameter-name-let-const-and-var.js",True)
        BlockScope_shadowing("language/block-scope/shadowing/lookup-from-closure.js",False)
        BlockScope_shadowing("language/block-scope/shadowing/lookup-from-closure.js",True)
       Comments_hashbang
        Comments_hashbang("language/comments/hashbang/function-constructor.js",False)
       Destructuring_binding
        Destructuring_binding("language/destructuring/binding/typedarray-backed-by-resizable-buffer.js",False)
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
