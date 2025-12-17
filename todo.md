# Performance Optimization TODO

## Current State (After Operator Enum Optimization)

Profiling results from ForLoopProfile benchmark (7,343ms total):

| Method | Time | % of Total | Gap from Children |
|--------|------|------------|-------------------|
| EvaluateAssignment | 3,171ms | 43.2% | ~3,000ms (95%) |
| EvaluateUnary | 1,338ms | 18.2% | ~846ms (63%) |
| ExecuteCondition | 846ms | 11.5% | ~707ms (84%) |
| SetIdentifierJsValue | 409ms | 5.6% | - |
| EvaluateExpression (in assignment) | 169ms | 2.3% | - |

The large gaps between parent methods and their children indicate overhead in:
- Type dispatch / pattern matching
- Unnecessary checks on every iteration
- Method call overhead

## Completed Optimizations

- [x] Convert `BinaryOperator` from string to enum (integer switch dispatch)
- [x] Convert `UnaryOperator` from string to enum
- [x] Add `IsTruthy` fast path for Boolean and Object types
- [x] JsValue-based arithmetic operations (`AddValue`, `SubtractValue`, etc.)
- [x] Direct identifier access paths (`SetIdentifierJsValue`, `ReadJsValue`)

## Remaining Issues

### 1. Function Name Hint Overhead (High Priority)

**Location:** `AssignmentExpressionExtensions.cs:346-378`

Every assignment calls:
```csharp
ShouldApplyAssignmentNameHint(assignment, rhs)
  → IsAnonymousFunctionDefinitionNode(rhs)  // Pattern matching on FunctionExpression/ClassExpression
  → IsParenthesizedIdentifierAssignment(assignment)  // String indexing
```

For numeric assignments like `sum = sum + i`, this is completely wasted work.

**Fix:** Add fast path that skips function name checks for non-function RHS expressions.

### 2. Variable Declaration Boxing (Medium Priority)

**Location:** `VariableKindExtensions.cs:48`

```csharp
: EvaluateExpression(declarator.Initializer, environment, context).ToObject();
```

This boxes the result unnecessarily. Should use JsValue throughout.

### 3. Expression Dispatch Overhead (High Priority)

**Location:** `ExpressionNodeExtensions.cs` - `EvaluateExpression`

Every expression evaluation does pattern matching:
```csharp
return expression switch
{
    LiteralExpression literal => ...,
    IdentifierExpression identifier => ...,
    BinaryExpression binary => ...,
    // ... many more cases
};
```

For hot loops, the repeated type checks are expensive.

**Potential Fixes:**
- Cache expression type discriminators
- Specialize loop body evaluation for common patterns
- Consider expression "compilation" to avoid repeated dispatch

### 4. Loop Plan Overhead (Medium Priority)

**Location:** `ForStatementExtensions.cs` - `EvaluateLoopPlanJsValue`

Each iteration:
- Calls `ExecuteCondition` → `EvaluateExpression` → pattern matching
- Calls `ExecutePostIteration` → `EvaluateUnary` → pattern matching
- Calls `EvaluateStatementJsValue` → pattern matching

**Potential Fix:** Specialize common loop patterns:
- `for (var i = 0; i < n; i++)` - numeric counter loops
- `for (var i = 0; i < arr.length; i++)` - array iteration

### 5. Identifier Resolution Overhead (Low Priority)

**Location:** `JsEnvironment.cs`

`TryGetCachedDeclarativeBinding` and `TryLocateBinding` are called frequently.
The caching helps, but cache lookup still has overhead.

**Potential Fix:** Pre-resolve identifiers at parse time for simple cases.

## Proposed Optimizations (Priority Order)

### Phase 1: Quick Wins

1. **Skip function name checks for non-function RHS**
   - Add `rhs is not (FunctionExpression or ClassExpression)` early exit
   - Expected improvement: 5-10%

2. **Fix VariableKindExtensions boxing**
   - Remove `.ToObject()` call, use JsValue path
   - Expected improvement: 2-5%

### Phase 2: Loop Specialization

3. **Specialize numeric for-loops**
   - Detect pattern: `for (var i = X; i OP Y; i++/--)`
   - Inline condition check and increment
   - Bypass expression dispatch for body
   - Expected improvement: 20-40%

### Phase 3: Expression Compilation

4. **Pre-analyze expressions**
   - At parse time, annotate expressions with type hints
   - Use discriminator field instead of pattern matching
   - Expected improvement: 10-20%

## Benchmarking

Current ForLoop benchmark: ~35.7ms (1M iterations)

Target after optimizations: <20ms

Run benchmark:
```bash
cd benchmarks/Asynkron.JsEngine.Benchmarks
dotnet run -c Release -- --filter "*ForLoop*"
```

Run profiler:
```bash
cd examples/ForLoopProfile
dotnet run -c Release
```
