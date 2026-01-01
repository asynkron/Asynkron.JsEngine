# TODO: Identifier Slot Optimization

## Investigation Status: BLOCKED - Closure Incompatibility

**Date**: 2026-01-01
**Conclusion**: User variable slot optimization is NOT feasible without implementing full closure analysis.

### Root Cause

The investigation discovered a fundamental architectural issue:

1. **Slots and dictionary bindings are separate storage systems** - they don't share state
2. **Closures access variables through the environment chain** (dictionary bindings), not slots
3. **Inner functions can read/write outer variables** - if outer uses slots but inner uses dictionary, they see different values

### Proof: TdzClosureTest.H4 Failure

```javascript
(function() {
    function f() { x = 1; }  // Inner function writes to x via environment chain (dictionary)
    f();
    var x;                   // If x gets a slot, outer reads from slot (still undefined!)
    return x;                // Expected: 1, Actual: undefined (or crash)
}())
```

When we tried assigning slots to user variables:
1. Outer function's `x` got SlotIndex=0
2. `return x` reads from `_slots[0]` = undefined
3. Inner `f()` writes to `x` via `environment.TrySetIdentifier()` → dictionary binding
4. These are **separate storage locations** - the write never affects the slot!

### What Would Be Required

To make user variable slot optimization work:

1. **Closure analysis** - Determine which variables are captured by inner functions
2. **Captured variables remain in dictionary** - Only non-captured variables get slots
3. **This requires walking ALL nested functions** at plan-build time

Example analysis:
```javascript
function outer() {
    let x = 1;           // x is captured by inner → NO slot
    let y = 2;           // y is NOT captured → CAN have slot
    function inner() {
        return x;        // Captures x
    }
    return inner() + y;
}
```

### Current Design (Correct)

The current implementation only assigns slots to **compiler-generated symbols** (prefixed with `\u0001`):
- `\u0001_resume0`, `\u0001_catch0`, `\u0001_yieldstar0`, etc.
- These are NEVER accessed by closures because they're internal to the IR execution

This is correct behavior. The test bomb tests document the "missing optimization" but the architecture fundamentally prevents it without closure analysis.

### Performance Impact

- User variables use dictionary lookup (O(1) hash, but with string comparison overhead)
- Compiler-generated variables use slot lookup (O(1) array index)
- For tight loops, the dictionary overhead is measurable but not critical
- The profiler showed ~13,000ms for ExecutePlan in worst case, which includes all evaluation overhead

### Files Examined

- `src/Asynkron.JsEngine/Execution/IdentifierCollector.cs` - Only collects `\u0001` prefixed symbols (correct)
- `src/Asynkron.JsEngine/Execution/ExecutionPlanBuilder.cs` - `AssignSlotsToUserVariables()` only handles compiler symbols
- `src/Asynkron.JsEngine/Ast/TypedAstEvaluator.ExecutionPlanRunner.cs` - Shows slot/dictionary separation

### Test Bomb Status

The 8 tests in `SlotOptimizationTestBomb.cs` document the current behavior:

| Test | Status | Meaning |
|------|--------|---------|
| H1-H7 | PASS (assert -1) | User variables have no slot info - this is EXPECTED |
| H8 | PASS | Execution works correctly - confirms correctness |

These tests should remain as-is. They document the architectural constraint, not a bug.

---

## Original Problem Statement (for reference)

User variable identifiers (like `s`, `i` in loops) have `SlotIndex=-1` and `ScopeId=-1`, causing all lookups to use scope-chain traversal instead of direct slot access.

See `docs/identifier-slot-optimization.md` for the original investigation details.

## Future Work (if desired)

If slot optimization for user variables is ever prioritized, the implementation would require:

1. **Add closure analysis pass** before IR building
   - Walk all nested function expressions
   - Track which identifiers are referenced from inner scopes
   - Mark captured identifiers in a set

2. **Modify IdentifierCollector** to include non-captured user variables
   - Check symbol against captured set
   - Only assign slots to non-captured identifiers

3. **Handle edge cases**
   - `eval()` in scope makes all variables potentially captured
   - `with` statements make all variables dynamic
   - `arguments` object can alias parameters

4. **Estimated complexity**: High - requires understanding all closure semantics
