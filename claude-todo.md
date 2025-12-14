# JsValue Unification Plan

## Goal
Replace `object?` with a unified `JsValue` struct throughout the engine to eliminate boxing for primitives (especially doubles and booleans).

## Expected Benefits
- **Eliminate double boxing** - the #1 allocation source
- **Eliminate bool boxing** - common in conditionals
- **Reduce GC pressure** - fewer small allocations
- **Better cache locality** - struct stored inline

## The JsValue Struct

```csharp
internal enum JsValueKind : byte
{
    Undefined,
    Null,
    Boolean,
    Number,
    BigInt,
    String,
    Symbol,
    Object
}

internal readonly struct JsValue
{
    public readonly JsValueKind Kind;
    public readonly double NumberValue;   // doubles direct, bool as 0/1
    public readonly object? ObjectValue;  // string, BigInt, Symbol, JsObject, etc.
}
```

Size: 1 (enum) + 8 (double) + 8 (reference) + padding = 24 bytes

---

## Phase 1: Foundation (Non-Breaking)

### 1.1 Create JsValue struct
- [ ] Create `JsTypes/JsValue.cs` with the struct definition
- [ ] Add constructors for each type
- [ ] Add static singletons: `Undefined`, `Null`, `True`, `False`, `Zero`, `One`, `NaN`
- [ ] Add type check properties: `IsNumber`, `IsBoolean`, `IsString`, etc.
- [ ] Add `AsDouble()`, `AsBoolean()`, `AsString()`, `AsObject()` accessors
- [ ] Add `ToObject()` method for interop with existing code (boxes if needed)
- [ ] Add `FromObject(object?)` factory method for gradual migration

### 1.2 Add conversion utilities
- [ ] `JsValue ToJsValue(object?)` - convert from old representation
- [ ] `object? ToObject(JsValue)` - convert back (for gradual migration)
- [ ] Add implicit/explicit operators where safe

### 1.3 Unit tests
- [ ] Test all constructors
- [ ] Test type checks
- [ ] Test round-trip conversions

---

## Phase 2: Hot Path Integration (Incremental)

### 2.1 Numeric operations in JsOps
- [ ] Add `JsValue` overloads to `Add`, `Subtract`, `Multiply`, `Divide`
- [ ] Keep existing `object?` methods, have them delegate to JsValue versions
- [ ] Update `ToNumericResult` to work with `JsValue` input

### 2.2 TypedAstEvaluator arithmetic
- [ ] Update `Add()` to use JsValue internally
- [ ] Update `PerformBigIntOrNumericOperation()` to use JsValue
- [ ] Update comparison operators

### 2.3 Benchmark checkpoint
- [ ] Run Fibonacci benchmark
- [ ] Measure allocation reduction
- [ ] Document results

---

## Phase 3: Expression Evaluation

### 3.1 Create parallel evaluation methods
- [ ] Add `EvaluateExpressionValue(expr, env, ctx) -> JsValue` methods
- [ ] Start with `LiteralExpression` - easiest, returns constants
- [ ] Add `IdentifierExpression` - reads from bindings
- [ ] Add `BinaryExpression` - uses arithmetic ops

### 3.2 Gradual conversion of expression types
- [ ] `UnaryExpression`
- [ ] `CallExpression` (complex - function returns)
- [ ] `MemberExpression` (needs property access changes)
- [ ] `AssignmentExpression`
- [ ] `ConditionalExpression`
- [ ] `LogicalExpression`
- [ ] `ArrayExpression`
- [ ] `ObjectExpression`

### 3.3 Update EvaluateExpression signature
- [ ] Change return type from `object?` to `JsValue`
- [ ] Update all callers
- [ ] This is the BIG commit

---

## Phase 4: Bindings and Environment

### 4.1 Update Binding struct
- [ ] Change `Value` from `object?` to `JsValue`
- [ ] Update `JsEnvironment.Set()` and `Get()` methods
- [ ] Update `SymbolHybridDictionary` if needed

### 4.2 Update variable declaration/assignment
- [ ] `VariableDeclaration` evaluation
- [ ] `AssignmentExpression` evaluation
- [ ] Parameter binding in function calls

---

## Phase 5: Object Properties

### 5.1 JsObject property storage
- [ ] Consider: Keep `object?` for properties or convert?
- [ ] Properties often hold objects/functions, less benefit from JsValue
- [ ] Decision: Maybe keep `object?` for properties, convert at boundaries

### 5.2 Property access
- [ ] `GetProperty()` returns JsValue
- [ ] `SetProperty()` takes JsValue
- [ ] Prototype chain lookup

---

## Phase 6: Function Calls

### 6.1 Function return values
- [ ] `IJsCallable.Invoke()` returns JsValue
- [ ] Built-in functions return JsValue
- [ ] User functions return JsValue

### 6.2 Function arguments
- [ ] Change from `object?[]` to `JsValue[]`
- [ ] Update argument pooling in JsValueCache
- [ ] Update all built-in function implementations

---

## Phase 7: Cleanup

### 7.1 Remove legacy code
- [ ] Remove `object?` overloads that are no longer used
- [ ] Remove `ToObject()` calls where possible
- [ ] Remove `FromObject()` calls where possible

### 7.2 Final optimization pass
- [ ] Profile for remaining boxing
- [ ] Optimize any remaining hot spots

### 7.3 Documentation
- [ ] Update agents.md with final results
- [ ] Document the JsValue architecture

---

## Migration Strategy

### Approach: Inside-Out
1. Start with innermost hot paths (arithmetic)
2. Work outward to expression evaluation
3. Then to bindings/environment
4. Finally to function boundaries

### Compatibility Layer
During migration, maintain both APIs:
```csharp
// Old API (delegates to new)
public object? Add(object? left, object? right, EvaluationContext ctx)
{
    return AddValue(JsValue.FromObject(left), JsValue.FromObject(right), ctx).ToObject();
}

// New API (actual implementation)
public JsValue AddValue(JsValue left, JsValue right, EvaluationContext ctx)
{
    if (left.IsNumber && right.IsNumber)
        return new JsValue(left.NumberValue + right.NumberValue);
    // ...
}
```

---

## Risk Mitigation

### Testing
- Run full test suite after each phase
- Run Test262 conformance tests
- Benchmark after each phase

### Rollback Points
- Each phase should be a separate PR/commit
- Can stop at any phase and still have improvements

### Known Challenges
1. **Async/await** - EvaluationContext flows through, may need JsValue support
2. **Generators** - Yield values need to be JsValue
3. **Promises** - Resolution values
4. **Interop** - External .NET code expects `object?`

---

## Success Metrics

| Metric | Current | Target |
|--------|---------|--------|
| Fibonacci allocations | 107.49 MB | < 60 MB |
| Fibonacci time | 115.73 ms | < 80 ms |
| Gap vs Jint (alloc) | 2.1x | < 1.5x |
| Gap vs Jint (time) | 2.1x | < 1.5x |

---

## Current Status

**Phase 1: COMPLETE** - JsValue struct created with all constructors, type checks, and conversion methods.

**Phase 2: COMPLETE** - JsValue-based arithmetic methods created in `TypedAstEvaluator.JsValue.cs`.

**Phase 4: ATTEMPTED AND REVERTED** - Tried storing JsValue in Binding struct, but it actually made things SLOWER (131ms vs 113ms) because:
- The existing code still accesses `binding.Value` (returns object?)
- Every access now pays conversion cost: `_jsValue.ToObject()` boxes doubles
- We're adding overhead without reducing allocations

**Current Results (unchanged):**
| Engine | Time | Allocated |
|--------|------|-----------|
| Jint | 53.01 ms | 50.11 MB |
| Asynkron | 113.10 ms | 107.49 MB |

## Key Learnings

1. **Piecemeal JsValue adoption adds overhead** - You can't just change storage without changing the entire flow. Converting object? -> JsValue -> object? at boundaries is worse than no change.

2. **All-or-nothing refactor required** - To benefit from JsValue, ALL of these must change together:
   - EvaluateExpression returns JsValue (not object?)
   - Bindings store JsValue
   - All operations work with JsValue
   - Only convert at external boundaries (interop with .NET code)

3. **The boxing happens at READ time** - When `binding.Value` returns object?, doubles get boxed. Storing JsValue internally doesn't help if we still return object?.

## Revised Plan

The only way to get JsValue benefits is a coordinated refactor:

### Option A: Big Bang Refactor
Change everything at once:
1. Change `EvaluateExpression` signature to return `JsValue`
2. Change all callers
3. Change `Binding.Value` to return `JsValue`
4. Update all operations

Pros: Clean, optimal performance
Cons: Massive change, high risk, all-or-nothing

### Option B: Gradual with Dual APIs
Keep object? APIs, add parallel JsValue APIs:
1. Add `EvaluateExpressionValue` returning JsValue
2. Add `Binding.JsValueValue` property
3. Gradually migrate callers to use JsValue variants
4. Eventually deprecate object? APIs

Pros: Lower risk, can be done incrementally
Cons: Dual maintenance, some paths may never migrate

### Option C: Specialized Optimizer
Don't change the general path, but add a specialized fast path for numeric-heavy code:
1. Detect pure numeric expressions at parse time
2. Use a specialized evaluator that works entirely with doubles
3. Skip the general object? machinery for these paths

Pros: Focused optimization, doesn't affect existing code
Cons: Limited scope, complexity in detecting applicable code

**Recommendation:** Option C is the most pragmatic. The Fibonacci benchmark is purely numeric - we could detect this and use a specialized fast path that never boxes.

**Next step:** Investigate Option C - creating a specialized numeric evaluator.
