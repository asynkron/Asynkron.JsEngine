# Investigation Report: Block-Local Closure TDZ Not Enforced in IR Path

## Problem Summary
When a function declared inside a bare block (at script level) accesses a `let` variable before its declaration, the engine returns `undefined` instead of throwing `ReferenceError`. The Temporal Dead Zone (TDZ) is not enforced for block-scoped `let`/`const` bindings in the IR execution path for script-level blocks.

## Affected Components
- `src/Asynkron.JsEngine/Execution/Emitters/BlockEmitter.cs` -- **ROOT CAUSE**: Does not pass `LexicalBindings` to `PushEnvironmentInstruction`
- `src/Asynkron.JsEngine/Execution/ExecutionPlanBuilder.cs:170` -- `SlotAssignmentRewriter` is skipped for script-level IR (`!IsScriptLevel`)
- `src/Asynkron.JsEngine/Ast/TypedAstEvaluator.ExecutionPlanRunner.Handlers.Scope.cs:180-191` -- TDZ marking code depends on `LexicalBindings` being set

## Evidence Collected

### Test Output
```
Test: H5_BlockLocalClosure_ReadBeforeInit
Expected: "ReferenceError|not-called"
Actual:   "not-caught|NaN"

Script executes via IR path (14 instructions).
PushEnv: old.ScopeId=-1 new.ScopeId=1 loopScope.ScopeId=-1 parent=-1
Function f goes through IR (planCache.Succeeded=True, scopeId=2000002)
```

### Code Analysis

#### The Bug: Two Missing Pieces

**Piece 1 (PRIMARY): `BlockEmitter.cs` line 135-142**

```csharp
entryIndex = ctx.Append(new PushEnvironmentInstruction(
    hoistEntry,
    ImmutableArray<Symbol>.Empty,
    scopeId,
    slotCount,
    slotMap,
    allowPooling,
    SourceBlock: block));  // <-- LexicalBindings NOT PASSED, defaults to null
```

The `PushEnvironmentInstruction` is created WITHOUT `LexicalBindings`. Compare with loop emitters (`LoopEmitterHelpers.cs:208`, `ForOfEmitter.cs:65`, `ForInEmitter.cs:78`) which DO pass `LexicalBindings`.

**Piece 2 (CONTRIBUTING): `ExecutionPlanBuilder.cs` line 170**

```csharp
if (!IsScriptLevel && TypedAstEvaluator.AllowsIdentifierCaching(function))
{
    analysis = AssignSlotsToUserVariables(...);  // SlotAssignmentRewriter runs here
}
```

The `SlotAssignmentRewriter` (which would otherwise stamp `LexicalBindings` onto the `PushEnvironmentInstruction` during rewriting at `SlotAssignmentRewriter.cs:238-244`) is skipped entirely for script-level IR plans. This means the missing `LexicalBindings` from `BlockEmitter` is never corrected.

#### Why TDZ Fails

In `HandlePushEnvironment` (`Handlers.Scope.cs:180-191`):

```csharp
if (instruction.LexicalBindings is { Count: > 0 } && !instruction.SlotMap.IsEmpty)
{
    foreach (var binding in instruction.LexicalBindings)
    {
        if (instruction.SlotMap.TryGetValue(binding, out var slotIndex))
        {
            newIterationEnv.SetSlotLexicalUninitialized(slotIndex);
        }
    }
}
```

Since `instruction.LexicalBindings` is `null`, this entire block is skipped. The slots for `x` are initialized with `JsValue.Undefined` and `SlotFlags.None` (from `InitializeSlots`), making `x` appear as a normal `undefined` variable instead of being in TDZ.

#### Execution Flow

1. Script IR executes `PushEnvironmentInstruction` for the block `{ function f() {...}; ...; let x; }`
2. Block environment is created with slots for `f` and `x`
3. Slots initialized to `Undefined` with `SlotFlags.None` -- **NO TDZ MARKING**
4. `FunctionDeclarationInstruction` creates `f` in block scope (closure captures block env)
5. `f()` is called, resolves `x` from closure (block scope)
6. Finds `x = Undefined` with `SlotFlags.None` -- no TDZ check triggered
7. Returns `undefined + 1 = NaN` instead of throwing `ReferenceError`

#### Why Function-Local TDZ Works

The function-local case (`(function() { function f() { return x; } f(); let x; }())`) works because:

1. `SetupFunctionEnvironment` in `ExecutionPlanRunner.Environment.cs:143-153` explicitly marks top-level lexical names as `Uninitialized`:

```csharp
var topLevelLexicalNames = hoistPlan.TopLevelLexicalNames;
foreach (var lexicalName in topLevelLexicalNames)
{
    if (!executionEnvironment.HasBinding(lexicalName))
    {
        executionEnvironment.DefineJsValue(lexicalName, JsValue.Uninitialized, ...);
    }
}
```

This code runs during function environment setup and correctly marks function-level `let`/`const` as TDZ. Block-level `let`/`const` does NOT have an equivalent path.

## Root Cause Analysis

### Hypothesis 1 (Confirmed): Missing `LexicalBindings` on Block `PushEnvironmentInstruction`

`BlockEmitter.TryEmitBlockWithEnvironment` does not compute or pass `LexicalBindings` when creating the `PushEnvironmentInstruction`. For function-level IR, the `SlotAssignmentRewriter` would correct this by stamping `LexicalBindings` during instruction rewriting. But for script-level IR, the rewriter is skipped (`!IsScriptLevel` guard), so the block scope never gets TDZ marking.

- Evidence supporting: Test output confirms `x` is `undefined` (not `Uninitialized`); code trace confirms `LexicalBindings` is `null` on the instruction; script IR skips `SlotAssignmentRewriter`
- Evidence against: None

## Recommended Fix

### Option A: Set `LexicalBindings` in `BlockEmitter` at IR-build Time (Recommended)

**File**: `src/Asynkron.JsEngine/Execution/Emitters/BlockEmitter.cs`
**Method**: `TryEmitBlockWithEnvironment`

Compute the `LexicalBindings` from `hoistPlan.TopLevelLexicalNames` and pass them when creating the `PushEnvironmentInstruction`:

```csharp
// After building slotMap, compute lexical bindings for TDZ
var lexicalBindings = ImmutableHashSet<Symbol>.Empty
    .WithComparer(ReferenceEqualityComparer<Symbol>.Instance);

foreach (var name in hoistPlan.TopLevelLexicalNames)
{
    // Only mark let/const as TDZ, not function declarations
    // Function declarations are initialized immediately and should not be in TDZ
    if (hoistPlan.LexicalDeclarationKinds.TryGetValue(name, out _))
    {
        // All entries in TopLevelLexicalNames from VariableDeclaration are lexical
        // FunctionDeclarations are also in TopLevelLexicalNames but should be excluded
        // since they are initialized by FunctionDeclarationInstruction before any user code runs
    }
    lexicalBindings = lexicalBindings.Add(name);
}

entryIndex = ctx.Append(new PushEnvironmentInstruction(
    hoistEntry,
    ImmutableArray<Symbol>.Empty,
    scopeId,
    slotCount,
    slotMap,
    allowPooling,
    LexicalBindings: lexicalBindings,   // <-- ADD THIS
    SourceBlock: block));
```

However, note that `TopLevelLexicalNames` includes function declaration names (see `HoistPlan.cs:430-435`). Block-scoped function declarations should NOT be in TDZ because they are initialized by `FunctionDeclarationInstruction` BEFORE other statements execute. We need to filter out function declaration names:

```csharp
// Compute lexical bindings that need TDZ (let/const only, NOT function declarations)
ImmutableHashSet<Symbol>? lexicalBindings = null;
var functionDeclNames = new HashSet<Symbol>(ReferenceEqualityComparer<Symbol>.Instance);
foreach (var stmt in block.Statements)
{
    if (stmt is FunctionDeclaration fd)
    {
        functionDeclNames.Add(fd.Name);
    }
}

var tdzNames = new HashSet<Symbol>(ReferenceEqualityComparer<Symbol>.Instance);
foreach (var name in hoistPlan.TopLevelLexicalNames)
{
    if (!functionDeclNames.Contains(name))
    {
        tdzNames.Add(name);
    }
}

if (tdzNames.Count > 0)
{
    lexicalBindings = tdzNames.ToImmutableHashSet(ReferenceEqualityComparer<Symbol>.Instance);
}
```

- Pros: Fix is localized to one file; works for both script-level and function-level IR; consistent with how loop emitters handle TDZ
- Cons: Need to be careful about function declaration names

### Option B: Run `SlotAssignmentRewriter` for Script-Level IR

Modify `ExecutionPlanBuilder.cs:170` to also run the rewriter for script-level code.

- Pros: More general fix; script-level code gets all the benefits of slot assignment
- Cons: Risky change; the comment at line 158-167 explains why it's skipped (var hoisting, `with` statements); could break many things; not a targeted fix

**Recommendation: Option A** is the correct and targeted fix.

## Test Plan
- [ ] Fix `BlockEmitter.TryEmitBlockWithEnvironment` to pass `LexicalBindings`
- [ ] Verify fix resolves `H5_BlockLocalClosure_ReadBeforeInit` test
- [ ] Add test for block-local closure TDZ with `const` (similar pattern)
- [ ] Add test for block-local closure TDZ inside a function (not just script level)
- [ ] Verify function declarations in blocks still work (not affected by TDZ)
- [ ] Run related test suite: `dotnet test --filter "Category~ScopeAnalysis"`
- [ ] Run full test suite: `dotnet test tests/Asynkron.JsEngine.Tests`
- [ ] Run the 4 failing Test262 tests:
  - `block-local-closure-get-before-initialization.js`
  - `block-local-closure-set-before-initialization.js`
  - `block-local-closure-get-before-initialization-strict.js` (if applicable)
  - `block-local-closure-set-before-initialization-strict.js` (if applicable)

## Additional Notes

1. The AST evaluation path (`BlockStatementExtensions.EvaluateBlockSlowCore`) already handles TDZ correctly via explicit `HoistLexicalBindingTargetForTdz` calls at lines 111-136. The bug is IR-only.

2. The `SlotAssignmentRewriter` correctly stamps `LexicalBindings` onto `PushEnvironmentInstruction` for function-level IR (line 238-244), but this path is never reached for script-level IR.

3. `TopLevelLexicalNames` includes `FunctionDeclaration` names (HoistPlan.cs:430-435). These must be excluded from TDZ because block-scoped function declarations are initialized eagerly by `FunctionDeclarationInstruction` before any user code in the block runs. Including them would incorrectly put the function binding in TDZ state.

4. The `SlotMap` on the `PushEnvironmentInstruction` IS populated by `BlockEmitter` (line 60: `BuildSlotMap(hoistPlan.TopLevelLexicalNames)`). The slots exist, they just lack TDZ flags.
