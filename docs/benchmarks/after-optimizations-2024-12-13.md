# Benchmark Results After Optimizations - 2024-12-13

## Configuration
- Machine: Apple M1, macOS Sequoia 15.6.1
- .NET: 10.0.0 (10.0.25.52411), Arm64 RyuJIT AdvSIMD
- Optimizations applied:
  1. HybridDictionary for JsObject property storage
  2. Expanded number cache (0-10239)

## Jint Comparison Results

| Benchmark | Asynkron | Jint | Ratio |
|-----------|----------|------|-------|
| SimpleArithmetic | 168 us | 89 us | 1.89x slower |
| ArrayOperations | 14.8 ms | 2.9 ms | 5.10x slower |
| PromiseBasic | 8.8 ms | 1.8 ms | 4.89x slower |
| AsyncAwait | 18.1 ms | 418 us | 43.3x slower |
| ClassDefinition | 13.9 ms | 1.3 ms | 10.7x slower |
| MapSet | 244 ms | 5.3 ms | 46x slower |
| Destructuring | 27.6 ms | 6.0 ms | 4.60x slower |
| SpreadOperator | 17.6 ms | 3.5 ms | 5.03x slower |
| FunctionCalls | 115 ms | 13.8 ms | 8.3x slower |
| Closures | 12.5 ms | 1.7 ms | 7.4x slower |
| GeneratorFunction | 9.3 ms | - | - (Jint fails) |
| JsonOperations | 8.0 ms | 4.7 ms | 1.70x slower |
| ForLoop | 52.4 ms | 16.2 ms | 3.23x slower |
| WhileLoop | 42.5 ms | 16.7 ms | 2.54x slower |
| ObjectCreation | 19.1 ms | 2.2 ms | 8.68x slower |
| PropertyAccess | 58.7 ms | 29.3 ms | 2.00x slower |
| Fibonacci | 605 ms | 69.6 ms | 8.7x slower |
| Recursion | 168 ms | 20.1 ms | 8.4x slower |
| RegexOperations | 49.7 ms | 18.1 ms | 2.74x slower |
| StringOperations | 713 us | 237 us | 3.01x slower |

## Improvements from Baseline

| Benchmark | Baseline | After Opts | Improvement |
|-----------|----------|------------|-------------|
| SimpleArithmetic | 208 us | 168 us | **19% faster** |
| ObjectCreation | 24.2 ms | 19.1 ms | **21% faster** |
| Recursion | 199 ms | 168 ms | **16% faster** |
| Fibonacci | 634 ms | 605 ms | **5% faster** |
| JsonOperations | 10 ms | 8.0 ms | **20% faster** |

## Summary

The optimizations provided modest improvements (5-21%) in several benchmarks, particularly those involving:
- Object creation (21% faster)
- Simple arithmetic (19% faster)
- Recursion (16% faster)
- JSON operations (20% faster)

However, Asynkron remains significantly slower than Jint in most benchmarks. The primary bottlenecks appear to be:
- Async/await implementation (~43x slower)
- Map/Set operations (~46x slower)
- Class definition (~10x slower)
- Function calls and recursion (~8x slower)

## Remaining Optimization Opportunities
1. Reference pooling for hot path objects
2. Pre-calculated hash codes for property keys
3. Bit-packed PropertyDescriptor flags
4. Consider architectural changes for async/await (CPS transformation overhead)
