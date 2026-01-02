No known test failures right now (slot stamping / environment pooling).

Verified with:
- `./test.sh`
- `dotnet test tests/Asynkron.JsEngine.Tests --filter "FullyQualifiedName~EnvironmentPoolingTests|FullyQualifiedName~SlotStampingTests"`


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
