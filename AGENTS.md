# Agent Guidelines for Asynkron.JsEngine

## Coding Standards

### Invariant Culture for Number/String Conversions

**CRITICAL RULE**: All floating-point and double-precision number to/from string conversions **MUST** use `InvariantCulture`.

This ensures consistent behavior across different locales and prevents issues with decimal separators, thousands separators, and number formatting.

#### Examples

**✅ CORRECT:**
```csharp
// Number to string
double value = 3.14;
string str = value.ToString(System.Globalization.CultureInfo.InvariantCulture);

// Integer to string (when culture matters)
long intValue = 1000;
string intStr = intValue.ToString(System.Globalization.CultureInfo.InvariantCulture);

// Formatted numbers
double num = 42.123;
string formatted = num.ToString("F2", System.Globalization.CultureInfo.InvariantCulture); // "42.12"
string exponential = num.ToString("e", System.Globalization.CultureInfo.InvariantCulture); // "4.212300e+001"
```

**❌ INCORRECT:**
```csharp
// DO NOT use default culture
double value = 3.14;
string str = value.ToString(); // BAD: Uses current culture

long intValue = 1000;
string intStr = intValue.ToString(); // BAD: Uses current culture for formatting
```

#### Where This Applies

- All Number.prototype methods (toString, toFixed, toExponential, toPrecision)
- String constructor conversions
- Any Math operations that produce string output
- JSON serialization of numbers
- Console output of numeric values
- Date/time formatting when dealing with numeric components

#### Why This Matters

Different cultures format numbers differently:
- US: `3.14` (period as decimal separator)
- Germany: `3,14` (comma as decimal separator)
- France: `3,14` with thousands separator

JavaScript expects consistent number formatting (US/Invariant style with periods), so we must always use InvariantCulture to match JavaScript behavior.

## Memory Profiling

> **Detailed Guide**: See [docs/memory-profiling.md](docs/memory-profiling.md) for comprehensive profiling techniques including dotnet-trace, GC dumps, and trace analysis.

### Quick Start

```bash
cd benchmarks/Asynkron.JsEngine.Benchmarks
dotnet run -c Release -- --filter "*Fibonacci*"
```

### Capture Detailed Allocation Trace

```bash
# Trace what's being allocated (with call stacks)
dotnet-trace collect \
  --profile gc-verbose \
  --format NetTrace \
  -o trace.nettrace \
  -- dotnet run -c Release \
     --project benchmarks/Asynkron.JsEngine.Benchmarks \
     --filter "JintComparisonBenchmarks.Asynkron_ForLoop"

# Analyze the trace
dotnet-trace report trace.nettrace topN -n 30

# Or convert to Speedscope/Chromium format for visualization
dotnet-trace convert trace.nettrace --format Speedscope
```

### Known Allocation Hotspots

**Fibonacci Benchmark Results (as of Dec 2024):**

*Before optimizations:*
| Engine | Time | Allocated | Gen0 Collections |
|--------|------|-----------|------------------|
| Jint | 56ms | 50.11 MB | 8,000 |
| Asynkron | 172ms | 322.37 MB | 53,000 |

*After optimizations (Round 1):*
| Engine | Time | Allocated | Gen0 Collections |
|--------|------|-----------|------------------|
| Jint | ~55ms | 50.11 MB | 8,000 |
| Asynkron | ~150ms | **173.25 MB** | **28,000** |

*After optimizations (Round 2 - lazy init & lock-free pools):*
| Engine | Time | Allocated | Gen0 Collections |
|--------|------|-----------|------------------|
| Jint | 52.58 ms | 50.11 MB | 8,000 |
| Asynkron | 134.51 ms | 168.62 MB | 28,000 |

*After optimizations (Round 3 - NumericResult struct & fast paths):*
| Engine | Time | Allocated | Gen0 Collections |
|--------|------|-----------|------------------|
| Jint | 53.30 ms | 50.11 MB | 8,000 |
| Asynkron | **116.84 ms** | **107.49 MB** | **17,000** |

**Cumulative Improvement:**
- Allocations: 322 MB → 107.49 MB = **~67% reduction**
- Speed: ~172 ms → 116.84 ms = **~32% faster**
- Gap with Jint: 2.2x time, 2.1x allocations (down from 3.1x and 6.4x)

### Implemented Optimizations

#### Round 3 (Dec 2024)

7. **NumericResult struct to avoid boxing** (`Runtime/JsOps.cs`)
   - Replaced `(NumericKind, object?)` tuple with `NumericResult` struct
   - Stores `double` directly in struct field (no boxing)
   - Added `ToNumericResult()` for internal use that returns struct directly
   - Added fast paths for already-double values (most common case)

8. **Fast paths for double arithmetic** (`Ast/TypedAstEvaluator.cs`)
   - `Add()` checks if both operands are doubles first, skips full conversion
   - `PerformBigIntOrNumericOperation()` uses `NumericResult` internally
   - Only boxes result at the very end via `JsValueCache.GetNumber()`

9. **Fast paths for increment/decrement** (`Ast/UnaryExpressionExtensions.cs`)
   - `++` and `--` operators check if operand is already double
   - Avoids `ToNumeric()` call and boxing for the common case

10. **Fast paths for unary and bitwise operations** (`Ast/TypedAstEvaluator.cs`)
    - `BitwiseNot()`, `UnaryMinus()` - fast path when operand is double
    - `LeftShift()`, `RightShift()`, `UnsignedRightShift()` - fast path for double operands
    - `PerformBigIntOrInt32Operation()` - fast path for double operands

#### Round 2 (Dec 2024)

4. **Lock-free JsEnvironmentPool** (`JsEnvironmentPool.cs`)
   - Replaced `ConcurrentBag` with fixed-size array using `Interlocked.CompareExchange`
   - 32 pool slots for JsEnvironment reuse
   - Reduces pool access contention in hot loops

5. **Lazy `_values` dictionary in JsEnvironment** (`JsEnvironment.cs`)
   - `SymbolHybridDictionary<Binding>` now allocated only when first binding is added
   - Environments without bindings (some block scopes) skip allocation entirely
   - All read paths check `_values is not null` before access

6. **Lock-free argument array pooling** (`JsValueCache.cs`)
   - Replaced `ConcurrentBag` pools with lock-free `ObjectPool<T>`
   - 15 slots per size (1-4 element arrays)
   - Uses `Interlocked.CompareExchange` for thread-safe, contention-free pooling

#### Round 1

1. **JsEnvironment pooling** (`TypedAstEvaluator.TypedFunction.cs`)
   - Added `ContainsInnerFunctionExpression` to `ScopeDynamicnessAnalyzer.cs` to detect functions that create closures
   - Added `_canPoolInvocationEnvironment` flag - true when function is simple AND has no inner functions
   - Modified `InvokeSimpleFast` to use `RentEnvironment`/`ReturnEnvironment` when safe
   - Note: Cannot pool environments for functions with inner closures (they capture the environment reference)

2. **SymbolHybridDictionary** (`Collections/SymbolHybridDictionary.cs`)
   - Array-based storage for small binding counts (< 8 bindings)
   - Uses reference equality for fast Symbol lookups
   - Switches to full Dictionary only when > 8 bindings
   - `JsEnvironment._values` now uses this instead of `Dictionary<Symbol, Binding>`

3. **Cached function description string** (`TypedAstEvaluator.TypedFunction.cs`)
   - `_functionDescription` field cached in constructor
   - Eliminates string allocation (`$"function {name.Name}"`) per function call

### Already Optimized Areas

- **Argument array pooling** - Small argument arrays (1-4 elements) pooled via lock-free `ObjectPool<T>` in `JsValueCache` (15 slots per size, using `Interlocked.CompareExchange`)
- **Number boxing cache** - Integers 0-10239 cached in `JsValueCache.CachedIntegers`
- **Identifier binding cache** - `ResolvedIdentifierBinding` (struct) cached per environment
- **EvaluationContext pooling** - Pooled via `RentContext`/`ReturnContext`
- **ToPrimitive fast path** - Primitives return immediately without object checks

### Remaining Gap Analysis

The remaining gap (113 MB vs Jint's 50 MB ≈ 2.3x allocations, 123 ms vs 52 ms ≈ 2.4x time) likely comes from:
- Architectural differences in environment/scope management
- `EvaluationContext` as class (required for async/await) vs Jint's `readonly struct ExecutionContext`
- AST node caching strategies
- Per-invocation Binding struct creation (even though structs, they go into collections)

## Other Guidelines

(Add additional coding guidelines here as needed)

## System.Object to JsValue

Many bugs are a result of untyped `object` values being passed around instead of `JsValue`.
Always ensure proper conversion when interfacing with JavaScript values.

If a method receives `object`, do not add guards casting or checking for JsObject, update the method to accept `JsValue` directly.

## JsValue Overload Pattern for Evaluators

When optimizing evaluator methods to avoid boxing, follow this pattern:

### Problem
Methods like `EvaluateBlock`, `EvaluateStatement`, `EvaluateIf` return `object?` which causes boxing when the result is a primitive (double, bool, etc.). In hot loops, this creates massive memory allocations.

### Solution
Add `JsValue`-returning overloads to evaluator methods:

1. **Keep the original method** for compatibility:
```csharp
private object? EvaluateBlock(JsEnvironment environment, EvaluationContext context)
{
    var (jsResult, hasJsResult, objResult) = EvaluateBlockCore(block, environment, context);
    return hasJsResult ? jsResult.ToObject() : objResult;
}
```

2. **Add a JsValue overload** for hot paths:
```csharp
private JsValue EvaluateBlockJsValue(JsEnvironment environment, EvaluationContext context)
{
    var (jsResult, hasJsResult, objResult) = EvaluateBlockCore(block, environment, context);
    return hasJsResult ? jsResult : JsValue.FromObject(objResult);
}
```

3. **Extract core logic** that returns both forms without boxing:
```csharp
private (JsValue jsResult, bool hasJsResult, object? objResult) EvaluateBlockCore(
    JsEnvironment environment, EvaluationContext context)
{
    // Implementation that tracks JsValue separately from object results
}
```

### Where to Apply

Apply this pattern to evaluators called in hot loops:

| Method | File | Priority |
|--------|------|----------|
| `EvaluateStatement` | StatementNodeExtensions.cs | High |
| `EvaluateBlock` | BlockStatementExtensions.cs | High |
| `EvaluateIf` | IfStatementExtensions.cs | High |
| `EvaluateExpression` | Already returns JsValue | Done |

### Usage in Loops

In `LoopPlanExtensions.cs`, use the JsValue versions:

```csharp
// Track loop result as JsValue to avoid boxing on each iteration
var lastValueJs = JsValue.Undefined;

while (true)
{
    // Use JsValue version - no boxing per iteration
    lastValueJs = EvaluateStatementJsValue(plan.Body, iterationEnvironment, context, loopLabel);
    // ...
}

// Only box at the final return
return lastValueJs.ToObject();
```

### Fast Path in EvaluateStatementJsValue

Handle the common cases without boxing:

```csharp
private JsValue EvaluateStatementJsValue(JsEnvironment environment, EvaluationContext context, Symbol? activeLabel = null)
{
    // Fast path for hot loop cases - avoid boxing
    switch (statement)
    {
        case BlockStatement block:
            return EvaluateBlockJsValue(block, environment, context);
        case ExpressionStatement expr:
            return EvaluateExpression(expr.Expression, environment, context);
        case IfStatement ifStmt:
            return EvaluateIfJsValue(ifStmt, environment, context);
    }

    // Slow path for other statements - box the result
    var result = EvaluateStatement(statement, environment, context, activeLabel);
    return JsValue.FromObject(result);
}
```

### Results

This optimization reduced memory allocation in the ForLoop benchmark:
- `let` loops (50k iterations): 4.99 MB → 3.84 MB (23% reduction)
- `var` loops (100k iterations): 29.52 MB → 27.24 MB (8% reduction)
- Execution time also improved ~19% for `let` loops
