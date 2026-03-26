# Investigation Report: for-in var loop variable undefined in strict mode

## Problem Summary
`for (var key in o)` in strict mode produces `undefined` for the loop variable `key` after the loop ends. Non-strict mode works correctly.

## Affected Components
- `src/Asynkron.JsEngine/Execution/ExecutionPlanBuilder.Emitters.cs` - EmitContext scope ID mismatch
- `src/Asynkron.JsEngine/Ast/TypedAstEvaluator.ExecutionPlanRunner.Core.cs` - RunScript double slot initialization
- `src/Asynkron.JsEngine/JsEnvironment.cs` - ResetSlotLayoutForPlan missing slot name population

## Root Cause Analysis

The bug affects the **IR execution path** (not the AST evaluator as initially suspected).

### Three-part root cause

**Part 1: Scope ID mismatch in EmitContext (ExecutionPlanBuilder.Emitters.cs:29)**

`GetEmitContext()` passes `_analysisRootScopeId` to `EmitContext`, which stamps internal synthetic identifiers (like `__forIn_value`) with that scope ID. However, the plan's `RootScopeId` uses `_rootScopeId` which diverges from `_analysisRootScopeId` when `function.ScopeId == 0`:

- `_analysisRootScopeId = function.ScopeId >= 0 ? function.ScopeId : 0` -> evaluates to 0
- `_rootScopeId = function.ScopeId > 0 ? function.ScopeId : SyntheticScopeIdAllocator.NextFunctionRoot()` -> evaluates to a large synthetic ID

This means `__forIn_value` gets `ScopeId=0` but the environment gets `ScopeId=synthetic` from the plan's RootScopeId.

**Part 2: Double slot initialization in RunScript (ExecutionPlanRunner.Core.cs:112-136)**

In strict mode, `ProgramNodeExtensions.ResetSlotLayoutForPlan` initializes the strict wrapper environment's slots (because `!HasSlots` is true). Then `RunScript` calls `InitializeSlots` again, APPENDING more slots and using the pre-existing count as `_slotOffset`. This causes IR slot index 1 to resolve to `1 + existingSlotCount` which points to the second copy of the slots, not the first.

In non-strict mode, the global environment ALREADY has slots (from realm setup), so `ResetSlotLayoutForPlan` is skipped. `RunScript`'s `InitializeSlots` correctly appends and `_slotOffset` accounts for the pre-existing global slots.

**Part 3: Missing slot name population (JsEnvironment.cs:3839)**

`ResetSlotLayoutForPlan` creates slots and sets the slot MAP (Symbol -> index dictionary) but does NOT populate the `JsSlot.Name` fields in the slot array. When `TryValidateSlotTarget` tries to verify a slot by checking `ReferenceEquals(slot.Name, name)`, it finds null names and fails with `name_mismatch`.

### Execution flow in strict mode (before fix)
1. Parser creates `ProgramNode` with `ScopeId=0`, `IsStrict=true`
2. `ProgramNodeExtensions` creates strict wrapper environment
3. `ResetSlotLayoutForPlan` initializes 2 slots (for `__forIn_state`, `__forIn_value`) with ScopeId=synthetic, but slot names are null
4. Hoisting adds `o` (slot 2) and `key` (slot 3) with proper names
5. `RunScript` sees `existingSlotCount=4`, adds 2 more slots, sets `_slotOffset=4`
6. `ForInMoveNext` writes current key to slot `1+4=5` (correct second copy)
7. Binding statement reads `__forIn_value` at `slotIndex=1, scopeId=0` -- fails to resolve:
   - Scope ID 0 doesn't match the environment's synthetic scope ID
   - Falls back to dictionary lookup which finds nothing
   - Returns `undefined`

## Applied Fix (3 changes)

### Change 1: Fix EmitContext scope ID (ExecutionPlanBuilder.Emitters.cs:29)
Pass `_rootScopeId` instead of `_analysisRootScopeId` to `EmitContext` so synthetic identifiers are stamped with the same scope ID as the plan's `RootScopeId`.

### Change 2: Skip double initialization in RunScript (ExecutionPlanRunner.Core.cs:112-136)
When `environment.LayoutId == plan.LayoutId` (meaning `ResetSlotLayoutForPlan` already configured the slot layout), skip the second `InitializeSlots` call and use `slotOffset=0`.

### Change 3: Populate slot names in ResetSlotLayoutForPlan (JsEnvironment.cs:3839)
Call `PopulateSyntheticSlotNames(slotSymbols)` so the slot entries have proper names for `TryValidateSlotTarget` verification.

## Test Plan
- [x] Verify fix resolves `for (var key in o)` in strict mode
- [x] Verify non-strict mode still works
- [ ] Run full test suite for regressions
- [ ] Check for-of loops in strict mode as a related scenario
