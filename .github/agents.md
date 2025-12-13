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

### Running Memory Benchmarks

The benchmark project includes memory diagnostics via BenchmarkDotNet. Run with:

```bash
cd benchmarks/Asynkron.JsEngine.Benchmarks
dotnet run -c Release -- operations --filter "*Fibonacci*"
```

### Allocation Tracing with dotnet-trace

For detailed allocation analysis, use dotnet-trace with GC events:

```bash
# Install tools if needed
dotnet tool install -g dotnet-trace
dotnet tool install -g dotnet-gcdump

# Capture allocation events
dotnet-trace collect --providers Microsoft-Windows-DotNETRuntime:0x80000:5 -- dotnet run -c Release -- [benchmark]

# Analyze the trace
dotnet-trace report <trace-file>.nettrace topN -n 30
```

### Known Allocation Hotspots

**Fibonacci Benchmark Results (as of Dec 2024):**

*Before optimizations:*
| Engine | Time | Allocated | Gen0 Collections |
|--------|------|-----------|------------------|
| Jint | 56ms | 50.11 MB | 8,000 |
| Asynkron | 172ms | 322.37 MB | 53,000 |

*After optimizations:*
| Engine | Time | Allocated | Gen0 Collections |
|--------|------|-----------|------------------|
| Jint | ~55ms | 50.11 MB | 8,000 |
| Asynkron | ~150ms | **173.25 MB** | **28,000** |

**Improvement: 46% reduction in allocations** (322 MB → 173 MB)

### Implemented Optimizations

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

- **Argument array pooling** - Small argument arrays (1-4 elements) pooled via `JsValueCache.RentArgumentArray`
- **Number boxing cache** - Integers 0-10239 cached in `JsValueCache.CachedIntegers`
- **Identifier binding cache** - `ResolvedIdentifierBinding` (struct) cached per environment
- **EvaluationContext pooling** - Pooled via `RentContext`/`ReturnContext`
- **ToPrimitive fast path** - Primitives return immediately without object checks

### Remaining Gap Analysis

The remaining gap (173 MB vs Jint's 50 MB ≈ 3.5x) likely comes from:
- Architectural differences in environment/scope management
- AST node caching strategies
- Per-invocation Binding struct creation (even though structs, they go into collections)

## Other Guidelines

(Add additional coding guidelines here as needed)
