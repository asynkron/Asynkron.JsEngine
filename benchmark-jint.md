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

## Expected Results

| Benchmark | Asynkron Time | Jint Time | Speedup | Asynkron Mem | Jint Mem | Mem Ratio |
|-----------|---------------|-----------|---------|--------------|----------|-----------|
| SimpleArithmetic | 52.53 μs | 82.32 μs | **1.6x faster** | 0 B | 23 KB | ∞ |
| AsyncGeneratorFunction | 141.06 μs | NA | - | 48 KB | NA | - |
| GeneratorFunction | 116 ms | NA | - | 243 MB | NA | - |
| WhileLoop | 3.73 ms | 162.88 ms | **44x faster** | 15 KB | 62 MB | **4,233x less** |
| ForLoop | 3.74 ms | 198.65 ms | **53x faster** | 17 KB | 250 MB | **14,680x less** |
| StringOperations | 7.29 ms | 5.34 ms | 1.4x slower | 4.6 MB | 447 KB | 10x more |
| AsyncAwait | 219.20 ms | 11.90 ms | 18x slower | 218 MB | 9.8 MB | 22x more |
| ClassDefinition | 101.77 ms | 12.65 ms | 8x slower | 39 MB | 3.3 MB | 12x more |
| AsyncAwaitPending | 204.36 ms | 15.40 ms | 13x slower | 174 MB | 9 MB | 19x more |
| AsyncAwaitResolved | 334.74 ms | 16.24 ms | 21x slower | 289 MB | 16 MB | 18x more |
| AsyncForOf | 1,695.19 ms | 18.61 ms | 91x slower | 1.99 GB | 26 MB | 76x more |
| ArrayOperations | 56.07 ms | 20.38 ms | 2.8x slower | 15 MB | 9 MB | 1.6x more |
| PromiseBasic | 70.81 ms | 22.27 ms | 3.2x slower | 25 MB | 18 MB | 1.4x more |
| Closures | 90.14 ms | 23.01 ms | 3.9x slower | 17 MB | 17 MB | ~same |
| ObjectCreation | 117.99 ms | 23.40 ms | 5x slower | 43 MB | 11 MB | 3.9x more |
| SpreadOperator | 96.53 ms | 27.81 ms | 3.5x slower | 142 MB | 11 MB | 13x more |
| JsonOperations | 104.97 ms | 44.31 ms | 2.4x slower | 37 MB | 16 MB | 2.3x more |
| Fibonacci | 121.95 ms | 51.93 ms | 2.3x slower | 17 MB | 52 MB | **3x less** |
| MapSet | 56.42 ms | 53.70 ms | ~same | 4 MB | 9.6 MB | **2.4x less** |
| Destructuring | 113.19 ms | 81.51 ms | 1.4x slower | 119 MB | 18 MB | 6.4x more |
| ForOfIteration | 435.04 ms | 96.95 ms | 4.5x slower | 1.46 GB | 265 MB | 5.5x more |
| RegexOperations | 162.65 ms | 108.34 ms | 1.5x slower | 319 MB | 41 MB | 7.8x more |
| FunctionCalls | 218.32 ms | 123.66 ms | 1.8x slower | 57 MB | 143 MB | **2.5x less** |
| Recursion | 335.32 ms | 162.76 ms | 2.1x slower | 46 MB | 173 MB | **3.8x less** |
| PropertyAccess | 732.80 ms | 233.04 ms | 3.1x slower | 24 KB | 144 MB | **5,890x less** |

## Summary

### Where Asynkron Wins

| Benchmark | Speedup | Notes |
|-----------|---------|-------|
| ForLoop | **53x faster** | Fast path optimization for numeric loops |
| WhileLoop | **44x faster** | Fast path optimization for numeric loops |
| SimpleArithmetic | **1.6x faster** | Basic operations |

### Where Jint Wins

| Benchmark | Slowdown | Notes |
|-----------|----------|-------|
| AsyncForOf | 91x slower | Async iteration overhead |
| AsyncAwaitResolved | 21x slower | Promise resolution overhead |
| AsyncAwait | 18x slower | Async/await overhead |
| AsyncAwaitPending | 13x slower | Pending promise handling |
| ClassDefinition | 8x slower | Class instantiation |
| ObjectCreation | 5x slower | Object allocation |

### Memory Efficiency (Asynkron)

| Benchmark | Memory Ratio | Notes |
|-----------|--------------|-------|
| ForLoop | **14,680x less** | Zero GC during fast path |
| PropertyAccess | **5,890x less** | Minimal allocations |
| WhileLoop | **4,233x less** | Zero GC during fast path |
| Recursion | **3.8x less** | Efficient call stack |
| Fibonacci | **3x less** | Efficient recursion |
| FunctionCalls | **2.5x less** | Lower per-call overhead |
| MapSet | **2.4x less** | Efficient collection handling |

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
