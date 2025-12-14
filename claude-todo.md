# JsValue Migration Plan

## Goal
Replace `object?` with `JsValue` throughout the engine to eliminate boxing for primitives.

## Current State

The migration is **partially complete**:
- `EvaluateExpression` already returns `JsValue`
- 4 expression types return native `JsValue`: Literal, Identifier, Binary, Unary
- ~20 expression types still return `object?` and wrap with `JsValue.FromObject()`
- `IJsCallable.Invoke` returns `object?`
- `Binding._value` stores `object?`

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

### 1.5 Async/Generator (Higher Risk)
- [ ] `AwaitExpressionExtensions.cs` - EvaluateAwait → JsValue
- [ ] `YieldExpressionExtensions.cs` - EvaluateYield → JsValue

### 1.6 Call Expression (Highest Risk - Largest File)
- [ ] `CallExpressionExtensions.cs` - EvaluateCall → JsValue
  - This is 750+ lines and touches IJsCallable

### 1.7 Update ExpressionNodeExtensions.cs
- [ ] Remove all `JsValue.FromObject()` wrappers from the switch statement
- [ ] Update helper methods that still return `object?`

---

## Phase 2: Statement Extensions

Convert statement evaluation to work with JsValue internally.

- [ ] `StatementNodeExtensions.cs` - core statement dispatcher
- [ ] `ReturnStatementExtensions.cs`
- [ ] `ThrowStatementExtensions.cs`
- [ ] `IfStatementExtensions.cs`
- [ ] `SwitchStatementExtensions.cs`
- [ ] `ForEachStatementExtensions.cs`
- [ ] `LoopPlanExtensions.cs`
- [ ] `WithStatementExtensions.cs`
- [ ] `VariableKindExtensions.cs`

---

## Phase 3: Binding/Environment

- [ ] Change `Binding._value` from `object?` to `JsValue`
- [ ] Update `JsEnvironment.Set()` to take `JsValue`
- [ ] Update `JsEnvironment.Get()` to return `JsValue`
- [ ] Update `TryGet()` to return `JsValue`
- [ ] Update all callers

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
