# TODO: Identifier Slot Optimization

IMPORTANT:
Once you are done, make sure to create a pull request to github!

Key findings:
- Slots and dictionary bindings are separate storage systems
- Closures access variables through environment chain (dictionary), not slots
- Inner functions writing to outer variables would break if outer function uses slots
- Would need full closure analysis to determine which variables can safely use slots

The worker investigated, tried a fix (which broke 12 tests), understood the root cause, and documented the architectural constraint.

## Problem

User variable identifiers (like `s`, `i` in loops) have `SlotIndex=-1` and `ScopeId=-1`, causing all lookups to use slow scope-chain traversal instead of direct slot access.

See `docs/identifier-slot-optimization.md` for full investigation details.

## Failing Tests (Fix These!)

9 tests in `tests/Asynkron.JsEngine.Tests/SlotOptimizationTests.cs` define the expected behavior:

| Test | Status | What it verifies |
|------|--------|-----------------|
| `LoopVariable_InCondition_ShouldHaveSlotInfo` | ❌ FAIL | Loop variable `i` in `i < 10` should have slot info |
| `CompoundAssignment_RhsIdentifier_ShouldHaveSlotInfo` | ❌ FAIL | RHS `i` in `s += i` should have slot info |
| `ReturnStatement_Identifier_ShouldHaveSlotInfo` | ❌ FAIL | Return variable `s` should have slot info |
| `LoopEnvironment_Identifiers_ShouldReferenceSlots` | ❌ FAIL | Identifiers should reference environment slots |
| `NestedLoops_AllVariables_ShouldHaveSlotInfo` | ❌ FAIL | All loop vars `i`, `j`, `sum` should have slots |
| `ShadowedVariables_ShouldHaveDifferentScopes` | ❌ FAIL | Inner/outer `x` should have different ScopeIds |
| `SimpleFunctionVariable_ShouldHaveSlotInfo` | ❌ FAIL | Basic `let x = 42; return x` should have slots |
| `SlotFastPath_ShouldBeUsedForLoopVariables` | ❌ FAIL | **Proves slow path**: 0 "slot read hit" logs |
| `Execution_LoopWithVariables_ProducesCorrectResult` | ✅ PASS | Sanity check - execution is correct |

Run:
```bash
dotnet test tests/Asynkron.JsEngine.Tests --filter "FullyQualifiedName~SlotOptimizationTests"
```

**When the fix is complete, all 9 tests will pass.**

## Proposed Solution

Replace the current `IdentifierCollector` with a scope-tracking walker that builds a complete symbol-to-scope map.

### Flow

```
1. Build IR fully (existing)
   └── Creates PushEnvironmentInstruction with ScopeId, SlotMap, PerIterationBindings
   └── Creates SimpleVariableDeclarationInstruction for declarations
   └── Creates BranchInstruction, CompoundAssignmentSlotInstruction, etc. with AST refs

2. Collect scope structure by walking IR
   └── Track "current scope" stack as we encounter PUSH_ENV/POP_ENV
   └── Build two data structures:

       declarations: Map<ScopeId, Map<Symbol, SlotIndex>>
       scopeParents: Map<ScopeId, ScopeId>  (child → parent)

   └── When we see PushEnvironmentInstruction:
       - Record scopeParents[instruction.ScopeId] = currentScope
       - Push instruction.ScopeId onto scope stack
       - Copy instruction.SlotMap entries into declarations[scopeId]
   └── When we see SimpleVariableDeclarationInstruction:
       - Add to declarations[currentScope][symbol] = allocatedSlot
   └── When we see PopEnvironmentInstruction:
       - Pop scope stack

3. Rewrite AST nodes with scope-aware resolution
   └── Walk IR again, tracking current scope via PUSH_ENV/POP_ENV
   └── For each IdentifierExpression encountered:
       - Start from currentScope
       - Look for symbol in declarations[scope]
       - If not found, look in scopeParents[scope] (walk up)
       - If found: stamp with (scopeId, slotIndex)
       - If not found in any scope: leave as -1 (closure/global, use dynamic lookup)
```

### Example

```javascript
function run() {
    let s = 0;
    for (let i = 0; i < 10; i++) {
        s += i;
    }
    return s;
}
```

```
IR Instructions:
  [0] SIMPLE_VAR_DECL s = 0           // ScopeId=0 (function level)
  [1] PUSH_ENV scopeId=1, slots={i→0} // Iteration scope
  [2] SIMPLE_VAR_DECL i = 0           // In scope 1
  [3] BRANCH (i < 10) ? [4] : [7]     // i should use scopeId=1, slot=0
  [4] COMPOUND s Add= i               // s→(0,0), i→(1,0)
  [5] INCREMENT i++                   // i→(1,0)
  [6] JUMP [3]
  [7] POP_ENV
  [8] RETURN s                        // s→(0,0)

After scope tracking:
  s → (scopeId=0, slotIndex=0)  // Function scope
  i → (scopeId=1, slotIndex=0)  // Iteration scope
```

## Implementation Steps

- [ ] **Verify IR structure first**: Print IR for a simple for loop to confirm:
  - Does `PushEnvironmentInstruction.SlotMap` already contain loop variable `i`?
  - Is there a separate `SimpleVariableDeclarationInstruction` for `i`?
  - What ScopeIds are assigned?

- [ ] Create `ScopeAwareSlotCollector` that:
  - Walks IR once, tracking scope stack via PUSH_ENV/POP_ENV
  - Builds `declarations: Dictionary<int, Dictionary<Symbol, int>>`
  - Builds `scopeParents: Dictionary<int, int>`
  - Extracts slot info from `PushEnvironmentInstruction.SlotMap`
  - Allocates slots for `SimpleVariableDeclarationInstruction` targets
  - Continues to handle `\u0001` prefixed compiler-generated symbols

- [ ] Create `ScopeAwareSlotRewriter` that:
  - Walks IR again, tracking current scope
  - For each `IdentifierExpression`, resolves through scope chain
  - Stamps with (ScopeId, SlotIndex) or leaves as -1 if not found

- [ ] Update `ExecutionPlanBuilder.AssignSlotsToUserVariables()` to use new collector + rewriter

- [ ] Handle `var` vs `let`/`const`:
  - `var` declarations go to function scope (ScopeId=0)
  - `let`/`const` declarations go to current block/loop scope

- [ ] Run tests to verify correctness

- [ ] Run profiler to measure improvement

## Files to Modify

- `src/Asynkron.JsEngine/Execution/IdentifierCollector.cs` → Replace with scope-tracking version
- `src/Asynkron.JsEngine/Execution/ExecutionPlanBuilder.cs` → Update `AssignSlotsToUserVariables()`
- `src/Asynkron.JsEngine/Execution/SlotAssignmentRewriter.cs` → May need updates for scope-aware rewriting

## Gaps & Edge Cases to Address

### 1. Variable Shadowing
```javascript
let x = 1;
for (let x = 0; ...) { ... } // Different x in different scope
```
The same Symbol name might exist in multiple scopes. Need to track declarations per-scope, not globally.

### 2. References to Outer Scope Variables
In `s += i` inside the loop:
- `i` → declared in scope 1 (iteration)
- `s` → declared in scope 0 (function)

When rewriting, we must look up where each symbol was **declared**, not just the current scope.

**Key insight**: The map structure should be:
```
declarations: Map<ScopeId, Map<Symbol, SlotIndex>>  // What's declared in each scope
scopeParents: Map<ScopeId, ScopeId>                 // Scope hierarchy
```

During rewriting, for each identifier reference at position P (inside scope S):
1. Look for symbol in scope S's declarations
2. If not found, look in parent scope
3. Repeat until found (gives us the declaring ScopeId and SlotIndex)

### 3. Nested Scopes
```javascript
for (...) {
    for (...) {           // scope 2
        { let x; }        // scope 3 (block)
    }
}
```
Need proper scope stack push/pop to handle arbitrary nesting.

### 4. IR Traversal Order
IR has jumps/branches - not purely linear. However, PUSH_ENV/POP_ENV should still be properly nested in instruction index order. Need to verify this assumption.

### 5. For Loop Initialization (VERIFY FIRST)
The example shows `SIMPLE_VAR_DECL i = 0` as a separate instruction, but for `let` loops, `i` might already be in `PushEnvironmentInstruction.SlotMap`.

**Action**: Before implementing, print actual IR for a for loop to understand the structure. This is the first implementation step.

### 6. Compiler-Generated Variables
Current collector handles `\u0001` prefixed symbols. New collector must continue to handle these (they go in ScopeId=0).

### 7. Closures
```javascript
function outer() {
    let x = 1;
    return function inner() { return x; }
}
```
Inner function captures `x` from outer scope. This is a **different execution plan** - inner function's IR doesn't have access to outer's slots. Slot optimization only applies within a single function's IR.

### 8. Runtime Lookup
At runtime, `FindByScopeId(scopeId)` walks the environment chain to find the right scope. With nested loops creating multiple environments, verify this works correctly:
```
Environment chain: [Scope3] → [Scope2] → [Scope1] → [Scope0]
Reading variable with ScopeId=1 should find [Scope1]
```

### 9. ScopeDepth Field
`IdentifierExpression` has `ScopeDepth` (how many scopes up). With slot-based lookup using `ScopeId`, is `ScopeDepth` still needed? Or is `FindByScopeId` sufficient?

### 10. `var` Hoisting
```javascript
function f() {
    console.log(x);  // undefined, not ReferenceError
    if (true) {
        var x = 1;   // Hoisted to function scope
    }
}
```
`var` declarations are hoisted to function scope (ScopeId=0), not block scope. The `SimpleVariableDeclarationInstruction` for `var` should always go to scope 0, regardless of current scope stack.

### 11. Assignment Targets
`AssignmentExpression` has a `Target` symbol that also needs slot stamping. The rewriter must handle both:
- `IdentifierExpression` (reads)
- `AssignmentExpression.Target` (writes)

Current `SlotAssignmentRewriter.RewriteAssignment` already does this, but needs scope-aware resolution.

## Verification Criteria

### 1. All Tests Pass
```bash
dotnet test tests/Asynkron.JsEngine.Tests
```
No regressions. The naive fix broke 12 ForLoop tests - a correct fix must pass all.

### 2. Slot Info Is Assigned
After building IR + rewriting, inspect identifiers:
```csharp
// In a test, after Evaluate:
var funcDecl = (FunctionDeclaration)program.Body[0];
var forStmt = (ForStatement)funcDecl.Function.Body.Statements[1];
var condition = (BinaryExpression)forStmt.Condition;
var leftId = (IdentifierExpression)condition.Left;

// BEFORE fix: SlotIndex=-1, ScopeId=-1
// AFTER fix:  SlotIndex>=0, ScopeId>=0
Assert.True(leftId.SlotIndex >= 0, "i should have slot assigned");
Assert.True(leftId.ScopeId >= 0, "i should have scope assigned");
```

### 3. Correct Scope Assignment
For the example `for (let i...) { s += i }`:
- `i` references should have ScopeId = iteration scope (e.g., 1)
- `s` references should have ScopeId = function scope (0)

Verify with a test that checks shadowing works:
```javascript
let x = 1;
let result;
for (let x = 0; x < 1; x++) {
    result = x;  // Should be 0, not 1
}
// result should be 0
```

### 4. Fast Paths Trigger
Enable debug logging and verify no "slot read miss" messages for loop variables:
```csharp
var logger = new FakeLogger();
var engine = new JsEngine(new JsEngineOptions { DebugMode = true, Logger = logger });
await engine.Evaluate(forLoopCode);

// Should NOT see slot misses for s, i
Assert.DoesNotContain(logger.Messages, m => m.Contains("slot read miss name=s"));
Assert.DoesNotContain(logger.Messages, m => m.Contains("slot read miss name=i"));
```

### 5. Performance Improvement
```bash
./tools/profile forloop --cpu
```
Compare before/after:
- **Before**: ExecutePlan ~13,000ms (slow scope chain lookup)
- **After**: ExecutePlan should be ~10-100ms (direct slot access)

Look for:
- `EvaluateExpression` call count should drop dramatically
- `FindByScopeId` should appear instead of `TryGetIdentifier` chain walking

### 6. Closure Variables Still Work
Variables captured from outer functions should NOT get slots (they need dynamic lookup):
```javascript
function outer() {
    let x = 1;
    function inner() { return x; }  // x has SlotIndex=-1
    return inner();
}
// Should return 1
```

### 7. Edge Cases Pass
Create specific tests for each gap:
- [ ] Shadowing: inner scope `x` doesn't affect outer `x`
- [ ] Nested loops: each loop has its own iteration scope
- [ ] `var` hoisting: var in block goes to function scope
- [ ] Block scopes: `{ let x }` creates separate scope

## Expected Impact

Profiling showed ~1000x speedup for tight loops when slot-based lookup is used (though the naive implementation broke tests due to wrong scope assignment).
