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
| Engine | Time | Allocated | Gen0 Collections |
|--------|------|-----------|------------------|
| Jint | 56ms | 50.11 MB | 8,000 |
| Asynkron | 172ms | 322.37 MB | 53,000 |

**Primary Allocation Sources:**

1. **JsEnvironment per function call** - `InvokeSimpleFast` in `TypedAstEvaluator.TypedFunction.cs:1579` creates a new `JsEnvironment` for every function invocation:
   ```csharp
   var functionEnvironment = new JsEnvironment(_closure, true, _isStrict, _function.Source, description);
   ```
   For Fibonacci(25) this means ~242,000 JsEnvironment allocations.

2. **Dictionary<Symbol, Binding>** - Each JsEnvironment contains a dictionary for variable bindings.

3. **String allocations** - Description strings interpolated per call.

4. **EvaluationContext** - Created/rented per call.

### Optimization Opportunities

- Pool JsEnvironment instances for simple functions
- Use array-backed storage for small binding counts (< 8 bindings)
- Cache function description strings
- Consider stack-based environments for tail-recursive calls

## Other Guidelines

(Add additional coding guidelines here as needed)
