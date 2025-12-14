# Memory Profiling Guide

This guide covers techniques for profiling memory allocations in Asynkron.JsEngine to identify optimization opportunities.

## Quick Start: BenchmarkDotNet Memory Diagnostics

The fastest way to see allocation totals is via BenchmarkDotNet's built-in `MemoryDiagnoser`:

```bash
cd benchmarks/Asynkron.JsEngine.Benchmarks
dotnet run -c Release -- --filter "*Fibonacci*"
```

This outputs:
- **Allocated**: Total bytes allocated per operation
- **Gen0/Gen1/Gen2**: GC collections per 1000 operations

## Detailed Allocation Profiling with dotnet-trace

To find *what* is being allocated (with call stacks), use `dotnet-trace` with the `gc-verbose` profile.

### Install Required Tools

```bash
dotnet tool install -g dotnet-trace
dotnet tool install -g dotnet-gcdump
```

### Capture Allocation Trace

```bash
cd /Users/rogerjohansson/git/asynkron/JsEngine2

# Trace a specific benchmark
dotnet-trace collect \
  --profile gc-verbose \
  --format NetTrace \
  -o forloop_alloc.nettrace \
  -- dotnet run -c Release \
     --project benchmarks/Asynkron.JsEngine.Benchmarks \
     --filter "JintComparisonBenchmarks.Asynkron_ForLoop"
```

### Provider Keywords Reference

The `gc-verbose` profile enables these providers:
- `Microsoft-Windows-DotNETRuntime:0x80000:5` - GC allocation tick events

For manual provider specification:
```bash
dotnet-trace collect \
  --providers Microsoft-Windows-DotNETRuntime:0x80000:5 \
  --format NetTrace \
  -o trace.nettrace \
  -- <command>
```

**Keywords (hex flags):**
- `0x1` - GC events
- `0x80000` - GC allocation tick (sampling)
- `0x200000` - GC heap allocation (verbose)

### Analyze the Trace

**Option 1: Built-in report (top CPU consumers)**
```bash
dotnet-trace report trace.nettrace topN -n 30
```

**Option 2: Convert to Chromium format for analysis**
```bash
dotnet-trace convert trace.nettrace --format Chromium
# Opens in Chrome at chrome://tracing or https://ui.perfetto.dev
```

**Option 3: Convert to Speedscope**
```bash
dotnet-trace convert trace.nettrace --format Speedscope
# Open at https://speedscope.app
```

**Option 4: Visual Studio / PerfView (Windows)**
- Open `.nettrace` files directly in Visual Studio's Diagnostic Tools
- Or use PerfView for detailed GC analysis

## GC Dump Analysis

For a snapshot of what's on the heap:

```bash
# Find the process ID
dotnet-trace ps

# Capture heap dump
dotnet-gcdump collect -p <PID> -o heap.gcdump
```

Analyze with Visual Studio or `dotnet-gcdump report`.

## Custom Profiling Script

Create a standalone program for profiling specific scenarios:

```csharp
// profile_allocations.cs
using Asynkron.JsEngine;

var code = @"
function fibonacci(n) {
    if (n <= 1) return n;
    return fibonacci(n - 1) + fibonacci(n - 2);
}
fibonacci(25);
";

Console.WriteLine("Starting profiling...");
Console.WriteLine("PID: " + Environment.ProcessId);
Console.WriteLine("Press Enter to start evaluation...");
Console.ReadLine();

var engine = new JsEngine();
for (int i = 0; i < 5; i++) {
    engine.EvaluateSync(code);
}

Console.WriteLine("Done. Press Enter to exit...");
Console.ReadLine();
```

This allows you to:
1. Start the process
2. Attach profiler before work begins
3. Trigger work manually
4. Capture clean traces

## Interpreting Results

### Common Allocation Sources

| Source | Typical Cause | Optimization |
|--------|---------------|--------------|
| `JsEnvironment` | Per-function-call scope | Environment pooling |
| `object[]` | Function arguments | Argument array pooling |
| `Dictionary<Symbol, Binding>` | Environment bindings | SymbolHybridDictionary |
| `Binding` struct boxing | Storing in collections | Avoid boxing paths |
| `string` | Property names, concatenation | String interning |
| `double` boxing | Numeric operations | JsValueCache.GetNumber() |

### Allocation Reduction Strategies

1. **Object Pooling**: Reuse objects instead of allocating new ones
   - `JsEnvironmentPool` for function environments
   - `JsValueCache.RentArgumentArray()` for argument arrays
   - `EvaluationContext` pooling

2. **Value Caching**: Pre-allocate common values
   - `JsValueCache.CachedIntegers` (0-10239)
   - `JsValueCache.BoxedTrue/BoxedFalse`
   - `JsValueCache.CachedIndexStrings` (0-9999)

3. **Struct over Class**: Use structs for small, short-lived data
   - `ResolvedIdentifierBinding` (cached identifier lookups)
   - Note: Can't use for `EvaluationContext` due to async requirements

4. **Lazy Initialization**: Don't allocate until needed
   - `SymbolHybridDictionary` uses array for < 8 items

## Comparison with Jint

Run side-by-side benchmarks to compare:

```bash
dotnet run -c Release -- --filter "*Fibonacci*"
```

Current gap (Fibonacci 25):
| Engine | Allocated | Gen0 |
|--------|-----------|------|
| Jint | 50.11 MB | 8,000 |
| Asynkron | 173.25 MB | 28,000 |

Key Jint optimizations to study:
- `readonly struct ExecutionContext` (stack allocated)
- `HybridDictionary` with `ListDictionary` for small collections
- `PlainObject` fast path for property access
- Flag-based `PropertyDescriptor`

## Continuous Monitoring

Add allocation tracking to CI by parsing BenchmarkDotNet JSON output:

```bash
dotnet run -c Release -- --filter "*Fibonacci*" --exporters json

# Parse results from:
# BenchmarkDotNet.Artifacts/results/*-report.json
```

Track the `Allocated` field over time to catch allocation regressions.
