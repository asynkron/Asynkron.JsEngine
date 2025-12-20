# Jint Comparison Benchmarks

This document describes how to run the Jint comparison benchmarks and the expected results.

## Running the Benchmarks

```bash
# Run full comparison with short job (recommended for quick iteration)
dotnet run -c Release --project benchmarks/Asynkron.JsEngine.Benchmarks/Asynkron.JsEngine.Benchmarks.csproj -- jint --job short

# Run full comparison with default job (more accurate, takes longer)
dotnet run -c Release --project benchmarks/Asynkron.JsEngine.Benchmarks/Asynkron.JsEngine.Benchmarks.csproj -- jint
```

## Output Files

Results are written to `BenchmarkDotNet.Artifacts/results/`:

| File                 | Description                                           |
|----------------------|-------------------------------------------------------|
| `*-report-github.md` | GitHub-flavored markdown table (best for README/docs) |
| `*-report.md`        | Standard markdown report                              |
| `*-report.html`      | Interactive HTML report                               |
| `*-report.csv`       | CSV for spreadsheet analysis                          |
| `*-report-full.json` | Complete JSON data                                    |

To view the Markdown results:
```bash
cat BenchmarkDotNet.Artifacts/results/Asynkron.JsEngine.Benchmarks.JintComparisonBenchmarks-report-github.md
```

## Latest Results (2025-12-18)

| Benchmark | Asynkron Time | Jint Time | Asynkron/Jint | Asynkron Mem | Jint Mem | Asynkron/Jint |
|---|---:|---:|---:|---:|---:|---:|
| ArrayOperations | 79,954.6 μs | 33,483.8 μs | 2.39x | 15012200 B | 9178840 B | 1.64x |
| AsyncAwait | 1,445,276.4 μs | 175,538.9 μs | 8.23x | 1602477848 B | 199686064 B | 8.02x |
| AsyncAwaitPending | 1,234,920.9 μs | 389,243.8 μs | 3.17x | 497692984 B | 185766336 B | 2.68x |
| AsyncAwaitResolved | 659,917.6 μs | 186,792.1 μs | 3.53x | 171647336 B | 243283496 B | 0.71x |
| AsyncAwaitResolvedReused | 405.1 μs | 1,208,840.7 μs | <0.01x | 27840 B | 639686480 B | <0.01x |
| AsyncForOf | 851,771.2 μs | 107,803.7 μs | 7.90x | 286716456 B | 26325256 B | 10.9x |
| AsyncGeneratorFunction | 479.8 μs | NA | NA | 45016 B | NA | NA |
| ClassDefinition | 196,735.1 μs | 101,993.6 μs | 1.93x | 37882344 B | 3252392 B | 11.6x |
| Closures | 207,906.6 μs | 43,499.6 μs | 4.78x | 14728600 B | 17009560 B | 0.87x |
| Destructuring | 365,311.5 μs | 155,451.3 μs | 2.35x | 110097208 B | 18445000 B | 5.97x |
| Fibonacci | 305,026.0 μs | 140,785.7 μs | 2.17x | 17489592 B | 52538600 B | 0.33x |
| ForLoop | 6,721.2 μs | 627,840.6 μs | 0.01x | 16248 B | 255678424 B | <0.01x |
| ForOfIteration | 651,883.9 μs | 386,106.9 μs | 1.69x | 602012760 B | 264830944 B | 2.27x |
| FunctionCalls | 640,387.5 μs | 403,991.7 μs | 1.59x | 56834320 B | 143205304 B | 0.40x |
| GeneratorFunction | 480,591.5 μs | NA | NA | 250160768 B | NA | NA |
| JsonOperations | 189,294.1 μs | 133,108.9 μs | 1.42x | 41016152 B | 16281824 B | 2.52x |
| MapSet | 134,371.4 μs | 78,622.2 μs | 1.71x | 4073808 B | 9664280 B | 0.42x |
| ObjectCreation | 205,942.4 μs | 96,915.3 μs | 2.12x | 48454840 B | 11040208 B | 4.39x |
| PromiseBasic | 85,924.4 μs | 38,945.9 μs | 2.21x | 13743792 B | 17867632 B | 0.77x |
| PropertyAccess | 2,199,883.0 μs | 719,306.9 μs | 3.06x | 23944 B | 143683032 B | <0.01x |
| Recursion | 838,061.8 μs | 504,593.6 μs | 1.66x | 46185680 B | 173074664 B | 0.27x |
| RegexOperations | 403,286.8 μs | 117,334.7 μs | 3.44x | 343942864 B | 41065648 B | 8.38x |
| SimpleArithmetic | 136.7 μs | 221.9 μs | 0.62x | 0 B | 23176 B | 0.00x |
| SpreadOperator | 224,097.2 μs | 39,551.8 μs | 5.67x | 128718736 B | 11115776 B | 11.6x |
| StringOperations | 16,833.1 μs | 20,302.7 μs | 0.83x | 983272 B | 446256 B | 2.20x |
| WhileLoop | 7,805.2 μs | 577,979.2 μs | 0.01x | 13512 B | 63678736 B | <0.01x |

**Enumerator behavior**:
- JsArray: checks `_length` on each iteration (handles array modification during iteration)
- String: iterates by Unicode code points (handles surrogate pairs for astral plane characters)
- TypedArray: uses iterator protocol for proper error propagation on buffer resize

### 2. JsEnvironmentPool Expansion (Low Impact)

**Problem**: `JsEnvironmentPool` exists but only used in regular for/while loops.

**Status**: The pool is already used in `LoopPlanExtensions.cs` for/while loops. Extending to for-of loops would require careful lifecycle management.

### 4. Async/Await Microtask Reduction (High Impact – Requires Fix)

**Problem**: Async operations have significant overhead:
- AsyncForOf: ~2500 ms vs Jint's ~76 ms (33x slower)
- AsyncAwait: 226 ms vs. Jint's 13 ms (17x slower)
- Uses ~1.9 GB memory for async iteration

**Status**: Fast enumerator path added to `for await...of` for sync iterables (same optimization as regular `for...of`). Async IIFE completion works correctly – the performance difference is due to the CPS transformation overhead, not a bug.

**Root Causes**:
- CPS transformation overhead for async functions
- Promise/continuation object allocations per await
- Microtask queue processing overhead

**Potential Solutions**:
- Use .NET async patterns (`IAsyncEnumerator<JsValue>`) with continuation scheduling:
  ```csharp
  // Simpler approach than CPS:
  var task = asyncEnumerator.MoveNextAsync();
  task.ContinueWith(t => engine.QueueMicrotask(() => ProcessNext()));
  ```
- Pool continuation closures and promise handlers
- Optimize an already-resolved promise fast path

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
- The loop benchmarks (ForLoop, WhileLoop) use one million iterations to measure steady-state performance
- Memory measurements are managed heap allocations only
- Results may vary based on hardware and .NET version
