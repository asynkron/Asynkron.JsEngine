# JsValue Migration Plan

## Goal
Replace `object?` with `JsValue` throughout the engine to eliminate boxing for primitives.

## Current State

The migration is **mostly complete through Phase 3**:
- ✅ `EvaluateExpression` returns `JsValue` (Phase 1)
- ✅ All expression types return native `JsValue` (Phase 1)
- ✅ `Binding` struct stores values in `JsValue _jsValue` field (Phase 2)
- ✅ `IdentifierExpressionExtensions` uses `GetIdentifierJsValue` for unboxed reads (Phase 2)
- ✅ Completion signals store `JsValue` directly (Phase 3)
- ✅ `SetReturnJsValue`, `SetThrowJsValue`, `SetYieldJsValue` in EvaluationContext (Phase 3)
- ✅ `DefineJsValue` for unboxed variable definitions (Phase 3)
- ✅ Most statement extensions optimized (Phase 3)
- ⏳ `IJsCallable.Invoke` still uses `object?` (Phase 4 pending)
- ⏳ Remaining ToObject calls at interface boundaries (Phase 4/5)

## Migration Strategy

Work **inside-out**: Convert internal methods first, then interfaces, then storage.

---

## Phase 1: Expression Extensions (No Interface Changes)

Convert each `Evaluate*` method to return `JsValue` directly instead of `object?`.
After each file, run tests to ensure nothing breaks.

### 1.1 Simple Expressions (Low Risk) ✅ COMPLETE
- [x] `ConditionalExpressionExtensions.cs` - EvaluateConditional → JsValue
- [x] `SequenceExpressionExtensions.cs` - EvaluateSequence → JsValue
- [x] `ArrayExpressionExtensions.cs` - EvaluateArray → JsValue
- [x] `ObjectExpressionExtensions.cs` - EvaluateObject → JsValue
- [x] `TemplateLiteralExpressionExtensions.cs` - EvaluateTemplateLiteral → JsValue
- [x] `TaggedTemplateExpressionExtensions.cs` - EvaluateTaggedTemplate → JsValue

### 1.2 Member Access (Medium Risk) ✅ COMPLETE
- [x] `MemberExpressionExtensions.cs` - EvaluateMember → JsValue
- [x] `NewExpressionExtensions.cs` - EvaluateNew → JsValue

### 1.3 Assignment Expressions (Medium Risk) ✅ COMPLETE
- [x] `AssignmentExpressionExtensions.cs` - EvaluateAssignment → JsValue
- [x] `PropertyAssignmentExpressionExtensions.cs` - EvaluatePropertyAssignment → JsValue
- [x] `IndexAssignmentExpressionExtensions.cs` - EvaluateIndexAssignment → JsValue
- [x] `DestructuringAssignmentExpressionExtensions.cs` - EvaluateDestructuringAssignment → JsValue

### 1.4 Function/Class Expressions (Medium Risk) ✅ COMPLETE
- [x] `FunctionExpressionExtensions.cs` - CreateFunctionValue → IJsCallable (kept typed - used by 7 callers)
- [x] `ClassExpressionExtensions.cs` - EvaluateClassExpression → JsValue

### 1.5 Async/Generator (Higher Risk) ✅ COMPLETE
- [x] `AwaitExpressionExtensions.cs` - EvaluateAwait → JsValue
- [x] `YieldExpressionExtensions.cs` - EvaluateYield → JsValue

### 1.6 Call Expression (Highest Risk - Largest File) ✅ COMPLETE
- [x] `CallExpressionExtensions.cs` - EvaluateCall → JsValue
  - This is 750+ lines and touches IJsCallable

### 1.7 Update ExpressionNodeExtensions.cs ✅ COMPLETE
- [x] Remove all `JsValue.FromObject()` wrappers from the switch statement
  - FunctionExpression wrapper kept (returns IJsCallable, used by 7 callers)
  - NewTargetExpression wrapper kept (requires Phase 3 - Binding/Environment)
- [x] Update helper methods that still return `object?`
  - `ResolveThisValue` → JsValue
  - `EvaluateImportMeta` → JsValue

---

## Phase 2: Binding/Environment (59 occurrences, 19 files) ✅ PARTIAL

**Implementation:** Chose Option 2 - added separate `JsValue _jsValue` field to Binding struct.
- Regular values stored in `_jsValue` (avoids boxing for primitives)
- Special bindings (async exports, imports) stored in `_specialBinding` with `HasSpecialBinding` flag

### 2.1 Binding Struct ✅ COMPLETE
- [x] Add `JsValue _jsValue` field for regular values
- [x] Rename `_value` to `_specialBinding` for special bindings only
- [x] Add `JsValue` property for direct JsValue access
- [x] Update `Value` property to use `_jsValue.ToObject()` for non-special bindings

### 2.2 JsEnvironment Methods ✅ COMPLETE
- [x] Add `GetJsValue(Symbol name)` - returns JsValue directly
- [x] Add `TryGetJsValue(Symbol name, out JsValue value)` - returns JsValue
- [x] Add `GetIdentifierJsValue(Symbol name, EvaluationContext context)` - cached lookup
- [x] Add `ReadJsValue` to `ResolvedIdentifierBinding` struct
- [x] Add `ReadResolvedBindingJsValue` static method
- [x] Add `IsUninitialized` property to Binding struct (boxing-free check)
- [x] Add `LiveExportBindingOrNull` property to Binding struct (boxing-free check)
- [x] Fix `ReadResolvedBindingJsValue` to use `IsUninitialized` (avoids ToObject boxing)
- [x] Fix `GetJsValue` to use `IsUninitialized` (avoids ToObject boxing)
- [x] Fix `ReadResolvedBindingValue` to use `IsUninitialized` (avoids ToObject boxing)
- [x] Fix `WriteResolvedBindingValue` to use `IsUninitialized` (avoids ToObject boxing)
- [x] Fix all `ReferenceEquals(binding.Value, Uninitialized)` patterns to use `binding.IsUninitialized`:
  - `Get`, `GetDeclarative`, `TryGet`, `TryGetJsValue`, `TryFindBinding`, `AssignInternal`
- [x] Add `GetJsValue()` to `AssignmentReference` struct (avoids boxing in compound assignments/unary ops)

### 2.3 Expression Extensions ✅ COMPLETE
- [x] Update `IdentifierExpressionExtensions.cs` to use `GetIdentifierJsValue`

### 2.4 Remaining Callers (Optional - for future optimization)
- [ ] Update other callers to use JsValue methods where beneficial
- [ ] Original `Get`/`TryGet`/`GetIdentifierValue` methods kept for backward compatibility

---

## Phase 3: Statement Extensions ✅ MOSTLY COMPLETE

Convert statement evaluation to work with JsValue internally.
Depends on Phase 2 (EvaluationContext uses Binding values).

### 3.1 Completion Signals ✅ COMPLETE
- [x] Convert `ReturnCompletionSignal` to class with JsValue property
- [x] Convert `ThrowFlowCompletionSignal` to class with JsValue property
- [x] Convert `YieldCompletionSignal` to class with JsValue property
- [x] Add `FlowJsValue` property to EvaluationContext

### 3.2 EvaluationContext Methods ✅ COMPLETE
- [x] Add `SetReturnJsValue(JsValue value)` - stores JsValue directly
- [x] Add `SetThrowJsValue(JsValue value)` - stores JsValue directly
- [x] Add `SetYieldJsValue(JsValue value, int yieldIndex)` - stores JsValue directly

### 3.3 JsEnvironment Enhancements ✅ COMPLETE
- [x] Add `DefineJsValue` method for unboxed variable definition
- [x] Add `Binding` constructor that takes `JsValue` directly

### 3.4 Statement Extensions ✅ MOSTLY COMPLETE
- [x] `ReturnStatementExtensions.cs` - uses SetReturnJsValue
- [x] `ThrowStatementExtensions.cs` - uses SetThrowJsValue
- [x] `YieldExpressionExtensions.cs` - uses SetYieldJsValue (simple yield path)
- [x] `SwitchStatementExtensions.cs` - uses StrictEqualsValue for case comparison
- [x] `IfStatementExtensions.cs` - already uses JsValue.IsTruthy
- [x] `WhileStatementExtensions.cs` - no ToObject calls
- [x] `DoWhileStatementExtensions.cs` - no ToObject calls
- [x] `ForStatementExtensions.cs` - no ToObject calls
- [x] `TryStatementExtensions.cs` - no ToObject calls
- [x] `LoopPlanExtensions.cs` - uses GetIdentifierJsValue/DefineJsValue for per-iteration bindings
- [x] `ConditionalExpressionExtensions.cs` - uses JsValue.IsTruthy
- [x] `SequenceExpressionExtensions.cs` - no ToObject calls
- [~] `StatementNodeExtensions.cs` - line 24 ExpressionStatement (tied to return type change)
- [~] `ForEachStatementExtensions.cs` - iterator protocols need object?
- [~] `WithStatementExtensions.cs` - with binding needs object?
- [~] `VariableKindExtensions.cs` - ApplyBindingTarget needs object?

### 3.5 Remaining Work (Blocked on Phase 4/5)
- `[~]` items have ToObject calls at interface boundaries
- Full conversion requires changing `EvaluateStatement` return type to `JsValue`
- Or changing `ApplyBindingTarget`, iterator protocols to accept JsValue

---

## Phase 4: IJsCallable Interface

This is the most breaking change - affects all function implementations.

- [ ] Change `IJsCallable.Invoke` signature:
  ```csharp
  // From:
  object? Invoke(IReadOnlyList<object?> arguments, object? thisValue);
  // To:
  JsValue Invoke(ReadOnlySpan<JsValue> arguments, JsValue thisValue);
  ```
- [ ] Update `IJsEnvironmentAwareCallable`
- [ ] Update all callable implementations:
  - [ ] TypedFunction
  - [ ] Built-in functions in StdLib/
  - [ ] Host functions
  - [ ] Bound functions
  - [ ] Proxy call/construct handlers

---

## Phase 5: JsObject Properties

- [ ] Evaluate: Should properties store `JsValue` or keep `object?`?
  - Properties often hold objects/functions (already references)
  - May be less benefit here, consider keeping `object?` for properties
- [ ] If converting: Update `GetProperty`/`SetProperty` signatures
- [ ] Update prototype chain lookups

---

## Phase 6: Cleanup

- [ ] Remove `JsValue.FromObject()` calls (should be zero after Phase 4)
- [ ] Remove `JsValue.ToObject()` calls except at .NET interop boundaries
- [ ] Profile and verify allocation reduction
- [ ] Update documentation

---

## Conversion Pattern

When converting a method from `object?` to `JsValue`:

```csharp
// Before
private object? EvaluateFoo(FooExpression expr, JsEnvironment env, EvaluationContext ctx)
{
    var result = SomeOperation();
    return result;  // returns object?
}

// After
private JsValue EvaluateFoo(FooExpression expr, JsEnvironment env, EvaluationContext ctx)
{
    var result = SomeOperationValue();  // if available, use JsValue version
    return result;  // returns JsValue

    // OR if calling code still returns object?:
    return JsValue.FromObject(result);  // temporary, remove in later phase
}
```

---

## Testing Strategy

After each file conversion:
1. `dotnet build` - must compile
2. `dotnet test` - all tests must pass
3. Optionally run benchmarks to track progress

---

## Files Quick Reference

**Already JsValue (no changes needed):**
- `LiteralExpressionExtensions.cs`
- `IdentifierExpressionExtensions.cs`
- `BinaryExpressionExtensions.cs`
- `UnaryExpressionExtensions.cs`
- `TypedAstEvaluator.JsValue.cs` (arithmetic/comparison operations)

**Key files to change:**
- `src/Asynkron.JsEngine/Ast/ExpressionNodeExtensions.cs` - main dispatcher
- `src/Asynkron.JsEngine/Ast/CallExpressionExtensions.cs` - 750 lines, touches IJsCallable
- `src/Asynkron.JsEngine/Ast/StatementNodeExtensions.cs` - statement evaluation
- `src/Asynkron.JsEngine/JsEnvironment.cs` - Binding struct
- `src/Asynkron.JsEngine/JsTypes/IJsCallable.cs` - function interface
