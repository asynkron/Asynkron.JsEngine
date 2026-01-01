# Identifier Slot Optimization Issue

## Summary

The IR execution plan fast paths check for `SlotIndex >= 0` and `ScopeId >= 0` on `IdentifierExpression` nodes, but user variable identifiers (like `s`, `i` in loops) have these set to `-1` (unresolved). This causes all user variable lookups to fall through to the slow scope-chain lookup path.

## Current Architecture

### Three Categories of Variables

1. **Compiler-generated variables** (e.g., `\u0001_resume0`, `\u0001_catch0`)
   - Collected by `IdentifierCollector` (filters for `\u0001` prefix)
   - Stamped with slot info by `SlotAssignmentRewriter`
   - Assigned to `ScopeId=0` (execution plan environment)
   - **Fast path works correctly**

2. **Loop iteration variables** (e.g., `i` in `for (let i = 0; ...)`)
   - Their environments get `SlotMap` via `PushEnvironmentInstruction`
   - The **identifiers themselves** have no slot info (`SlotIndex=-1`, `ScopeId=-1`)
   - **Fast path never triggers** - uses scope chain lookup

3. **Function-level variables** (e.g., `let s = 0` at function body level)
   - Neither environment nor identifiers have slot info
   - **Fully dynamic lookup** via scope chain

### Why Fast Paths Don't Trigger

In `TypedAstEvaluator.ExecutionPlanRunner.cs`, the fast paths check:

```csharp
if (binCond.Left is IdentifierExpression { SlotIndex: >= 0, ScopeId: >= 0 } leftId)
{
    // Fast slot-based lookup
}
else
{
    // Fall back to AST evaluation (slow path)
}
```

Since user identifiers have `SlotIndex=-1` and `ScopeId=-1`, the fast path condition is never satisfied.

## Investigation: Failed Optimization Attempt

### What Was Tried

Modified `IdentifierCollector` to collect user variable symbols from `SimpleVariableDeclarationInstruction`:

```csharp
case SimpleVariableDeclarationInstruction varDecl:
    Identifiers.Add(varDecl.TargetSymbol);  // Collect user variables
    if (varDecl.Initializer is not null)
        Visit(varDecl.Initializer);
    break;
```

### Result

- **Performance improved dramatically**: ExecutePlan went from ~13s to ~8ms
- **But 12 tests failed**: All loop-related tests returned 0 instead of expected values

### Root Cause of Failure

The `SlotAssignmentRewriter` assigns ALL collected symbols to `ScopeId=0` (the execution plan environment):

```csharp
foreach (var symbol in collector.Identifiers)
{
    var slotIndex = AllocateSlot(symbol);
    symbolToScope[symbol] = (0, slotIndex);  // Always ScopeId=0
}
```

But loop variables like `i` in `for (let i = 0; ...)` belong to the **iteration environment** (created by `PushEnvironmentInstruction` with its own `ScopeId`). Putting them in `ScopeId=0` causes lookups to find the wrong (or no) value.

## Correct Fix Required

To make user variable lookups fast, a proper scope analysis pass is needed that:

1. **Tracks variable declaration scopes**: Function body, block, or loop iteration
2. **Assigns slot indices per scope**: Each scope has its own slot numbering
3. **Stamps IdentifierExpression nodes**: With the correct `(ScopeId, SlotIndex)` pair based on where the variable is declared

### Scope-Aware Slot Assignment

| Variable Type | Declaration Location | Correct ScopeId |
|--------------|---------------------|-----------------|
| Function-level `let`/`const`/`var` | Function body | Function's `ScopeId` |
| Loop variable `let`/`const` | For loop header | Iteration's `ScopeId` (from `LoopPlan`) |
| Block variable `let`/`const` | Block statement | Block's `ScopeId` |
| Compiler-generated | Execution plan | `0` (current behavior is correct) |

### Required Changes

1. **Extend scope analysis**: Track which scope each variable declaration belongs to
2. **Build scope-aware symbol maps**: Map each symbol to its declaring scope's ID and slot index
3. **Stamp identifier references**: During AST transformation, update all `IdentifierExpression` nodes that reference analyzed variables

## Open PRs (Not Sufficient)

PRs #318-321 add fast paths that CHECK for slot info:
- #318: Avoid slot assignment allocations
- #319: Optimize slot-based assignments to skip IdentifierExpression allocation
- #320: Optimize compound assignment RHS evaluation with fast paths
- #321: Direct slot reads/writes for assignment expressions

These are ineffective because user identifiers don't have slot info to begin with. The PRs optimize the "already fast" path but don't address the root cause.

## Files Involved

- `src/Asynkron.JsEngine/Execution/IdentifierCollector.cs` - Collects symbols for slot assignment
- `src/Asynkron.JsEngine/Execution/SlotAssignmentRewriter.cs` - Stamps AST nodes with slot info
- `src/Asynkron.JsEngine/Execution/ExecutionPlanBuilder.cs` - Orchestrates slot assignment
- `src/Asynkron.JsEngine/Ast/IdentifierExpression.cs` - Holds `SlotIndex`/`ScopeId` fields
- `src/Asynkron.JsEngine/Ast/FunctionExpression.cs` - Has `SlotMap` for function scope
- `src/Asynkron.JsEngine/Ast/BlockStatement.cs` - Has `SlotMap` for block scope
- `src/Asynkron.JsEngine/Execution/LoopPlan.cs` - Has `IterationSlotMap` for loop scope

## Impact

Without this optimization, every user variable access in the IR execution path goes through scope chain lookup, which is significantly slower than direct slot access. The profiler showed that with (incorrect) slot assignment, performance improved by ~1000x for tight loops.

## Small Improvements Made

While investigating, we added handling for `CompoundAssignmentSlotInstruction`:

1. **IdentifierCollector.cs**: Now visits `CompoundAssignmentSlotInstruction.RhsExpression` to collect any compiler-generated identifiers in the RHS
2. **SlotAssignmentRewriter.cs**: Now rewrites `CompoundAssignmentSlotInstruction.RhsExpression` to stamp any compiler-generated identifiers with slot info

These are safe, incremental improvements that enable the fast path for compiler-generated variables used in compound assignments like `s += \u0001_temp`.
