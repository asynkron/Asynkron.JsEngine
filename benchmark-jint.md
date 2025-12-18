# Jint Comparison Benchmarks

This document describes how to run the Jint comparison benchmarks and the expected results.

## Running the Benchmarks

```bash
# Run full comparison with short job (recommended for quick iteration)
dotnet run -c Release --project benchmarks/Asynkron.JsEngine.Benchmarks -- \
  --filter "*JintComparison*" \
  --job short \
  --exporters github markdown html

# Run full comparison with default job (more accurate, takes longer)
dotnet run -c Release --project benchmarks/Asynkron.JsEngine.Benchmarks -- \
  --filter "*JintComparison*" \
  --exporters github markdown html
```

## Output Files

Results are written to `BenchmarkDotNet.Artifacts/results/`:

| File | Description |
|------|-------------|
| `*-report-github.md` | GitHub-flavored markdown table (best for README/docs) |
| `*-report.md` | Standard markdown report |
| `*-report.html` | Interactive HTML report |
| `*-report.csv` | CSV for spreadsheet analysis |
| `*-report-full.json` | Complete JSON data |

To view the markdown results:
```bash
cat BenchmarkDotNet.Artifacts/results/Asynkron.JsEngine.Benchmarks.JintComparisonBenchmarks-report-github.md
```

## Latest Results (2025-12-18)

| Benchmark | Asynkron Time | Jint Time | Speedup | Asynkron Mem | Jint Mem | Mem Ratio |
|-----------|---------------|-----------|---------|--------------|----------|-----------|
| SimpleArithmetic | 94.36 μs | 96.22 μs | ~same | 0 B | 23 KB | ∞ |
| AsyncGeneratorFunction | 162.47 μs | NA | - | 48 KB | NA | - |
| GeneratorFunction | 119 ms | NA | - | 243 MB | NA | - |
| WhileLoop | 3.93 ms | 175.73 ms | **45x faster** | 15 KB | 64 MB | **4,253x less** |
| ForLoop | 3.72 ms | 210.30 ms | **57x faster** | 17 KB | 256 MB | **15,046x less** |
| StringOperations | 7.89 ms | 5.66 ms | 1.4x slower | 968 KB | 446 KB | 2.2x more |
| AsyncAwait | 226.51 ms | 13.37 ms | 17x slower | 218 MB | 9.8 MB | 22x more |
| ClassDefinition | 105.10 ms | 14.12 ms | 7.4x slower | 39 MB | 3.3 MB | 12x more |
| AsyncAwaitPending | 300.29 ms | 15.27 ms | 20x slower | 174 MB | 9 MB | 19x more |
| AsyncAwaitResolved | 332.04 ms | 17.32 ms | 19x slower | 289 MB | 16 MB | 18x more |
| AsyncForOf | 1,932.51 ms | 50.70 ms | 38x slower | 1.99 GB | 26 MB | 76x more |
| ArrayOperations | 75.13 ms | 20.67 ms | 3.6x slower | 15 MB | 9 MB | 1.6x more |
| PromiseBasic | 76.16 ms | 23.35 ms | 3.3x slower | 25 MB | 18 MB | 1.4x more |
| Closures | 112.81 ms | 28.65 ms | 3.9x slower | 17 MB | 17 MB | ~same |
| ObjectCreation | 90.63 ms | 24.96 ms | 3.6x slower | 43 MB | 11 MB | 3.9x more |
| SpreadOperator | 93.13 ms | 30.30 ms | 3.1x slower | 142 MB | 11 MB | 13x more |
| JsonOperations | 90.20 ms | 49.85 ms | 1.8x slower | 37 MB | 16 MB | 2.3x more |
| Fibonacci | 113.27 ms | 51.68 ms | 2.2x slower | 17 MB | 52 MB | **3x less** |
| MapSet | 78.84 ms | 68.15 ms | 1.2x slower | 4 MB | 9.6 MB | **2.4x less** |
| Destructuring | 125.87 ms | 28.85 ms | 4.4x slower | 118 MB | 18 MB | 6.5x more |
| ForOfIteration | 248 ms | 96.54 ms | 2.6x slower | 602 MB | 265 MB | 2.3x more |
| RegexOperations | 192.46 ms | 94.07 ms | 2x slower | 319 MB | 41 MB | 7.8x more |
| FunctionCalls | 216.62 ms | 127.14 ms | 1.7x slower | 57 MB | 143 MB | **2.5x less** |
| Recursion | 346.29 ms | 169.31 ms | 2x slower | 46 MB | 173 MB | **3.8x less** |
| PropertyAccess | 677.91 ms | 233.09 ms | 2.9x slower | 24 KB | 144 MB | **5,890x less** |

## Summary

### Where Asynkron Wins

| Benchmark | Speedup | Notes |
|-----------|---------|-------|
| ForLoop | **57x faster** | Fast path optimization for numeric loops |
| WhileLoop | **45x faster** | Fast path optimization for numeric loops |
| SimpleArithmetic | ~same | Basic operations |

### Where Jint Wins

| Benchmark | Slowdown | Notes |
|-----------|----------|-------|
| AsyncForOf | 38x slower | Async iteration overhead (improved from 91x) |
| AsyncAwaitPending | 20x slower | Pending promise handling |
| AsyncAwaitResolved | 19x slower | Promise resolution overhead |
| AsyncAwait | 17x slower | Async/await overhead |
| ClassDefinition | 7.4x slower | Class instantiation |
| ForOfIteration | 4.7x slower | Iterator protocol overhead |

### Memory Efficiency (Asynkron)

| Benchmark | Memory Ratio | Notes |
|-----------|--------------|-------|
| ForLoop | **15,046x less** | Zero GC during fast path |
| PropertyAccess | **5,890x less** | Minimal allocations |
| WhileLoop | **4,253x less** | Zero GC during fast path |
| Recursion | **3.8x less** | Efficient call stack |
| Fibonacci | **3x less** | Efficient recursion |
| FunctionCalls | **2.5x less** | Lower per-call overhead |
| MapSet | **2.4x less** | Efficient collection handling |

### Notable Improvement: StringOperations

The `JsRopeString` optimization reduced memory usage from **4.6 MB → 968 KB** (4.8x improvement) by deferring string concatenation until the final value is needed.

## Optimization Opportunities

The following areas could benefit from similar patterns to `JsRopeString`:

### 1. ✅ Fast Enumerator Path for For-Of (IMPLEMENTED)

**Problem**: Every iterator `next()` call was creating a new `JsObject` for `{done, value}`.

**Solution Implemented**:
- Added `IEnumerable<JsValue>` to `JsArray` for direct enumeration
- Created `TryGetFastEnumeratorForIteration()` to bypass iterator protocol for known types
- For-of loops now use `IEnumerator<JsValue>` directly for arrays, typed arrays, and strings

**Results**:
- ForOfIteration: 456 ms → 248 ms (**1.8x faster**)
- Memory: 1.46 GB → 602 MB (**2.4x less memory**)

### 2. JsEnvironmentPool Expansion (Low Impact)

**Problem**: `JsEnvironmentPool` exists but only used in regular for/while loops.

**Status**: The pool is already used in `LoopPlanExtensions.cs` for for/while loops. Extending to for-of loops would require careful lifecycle management to ensure environments are returned to the pool at the right time. The for-of fast enumerator optimization already provides significant memory savings, so this is lower priority.

### 3. ✅ Array Fast-Path Iteration (IMPLEMENTED - same as #1)

This optimization was implemented as part of the Fast Enumerator Path above.

### 4. Async/Await Microtask Reduction (High Impact - Requires Fix)

**Problem**: Async operations have significant overhead:
- AsyncForOf: ~2500 ms vs Jint's ~76 ms (33x slower)
- AsyncAwait: 226 ms vs Jint's 13 ms (17x slower)
- Uses ~1.9 GB memory for async iteration

**Status**: Fast enumerator path added to `for await...of` for sync iterables (same optimization as regular `for...of`). However, the benchmark has a known bug where async IIFE doesn't complete, making comparison unreliable.

**Root Causes**:
- CPS transformation overhead for async functions
- Promise/continuation object allocations per await
- Microtask queue processing overhead

**Potential Solutions**:
- Fix the async IIFE completion bug first (blocks accurate benchmarking)
- Use .NET async patterns (`IAsyncEnumerator<JsValue>`) with continuation scheduling:
  ```csharp
  // Simpler approach than CPS:
  var task = asyncEnumerator.MoveNextAsync();
  task.ContinueWith(t => engine.QueueMicrotask(() => ProcessNext()));
  ```
- Pool continuation closures and promise handlers
- Optimize already-resolved promise fast path

## Test Environment

```
BenchmarkDotNet v0.14.0, macOS Sequoia 15.6.1 (24G90) [Darwin 24.6.0]
Apple M1, 1 CPU, 8 logical and 8 physical cores
.NET SDK 10.0.100
  [Host]   : .NET 10.0.0 (10.0.25.52411), Arm64 RyuJIT AdvSIMD
  ShortRun : .NET 10.0.0 (10.0.25.52411), Arm64 RyuJIT AdvSIMD
```

## Notes

- Jint does not support `GeneratorFunction` and `AsyncGeneratorFunction` benchmarks (marked as NA)
- The loop benchmarks (ForLoop, WhileLoop) use 1 million iterations to measure steady-state performance
- Memory measurements are managed heap allocations only
- Results may vary based on hardware and .NET version
