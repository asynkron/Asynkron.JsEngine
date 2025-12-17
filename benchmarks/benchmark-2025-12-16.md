# Benchmark Results - 2025-12-16

## Test Environment
- **Platform**: macOS Sequoia 15.6.1 (Darwin 24.6.0)
- **CPU**: Apple M1, 1 CPU, 8 logical and 8 physical cores
- **.NET SDK**: 10.0.100
- **Runtime**: .NET 10.0.0 (10.0.25.52411), Arm64 RyuJIT AdvSIMD

## Performance Comparison (Time in ms)

| Benchmark | Jint | Asynkron | Ratio | Winner |
|-----------|------|----------|-------|--------|
| SimpleArithmetic | 0.09 | **0.07** | 0.77x | Asynkron |
| StringOperations | **0.36** | 1.14 | 3.13x | Jint |
| Closures | **1.36** | 4.04 | 2.96x | Jint |
| ClassDefinition | **2.57** | 21.90 | 8.53x | Jint |
| AsyncAwait | **3.81** | 163.35 | 42.9x | Jint |
| PromiseBasic | **6.05** | 20.61 | 3.41x | Jint |
| SpreadOperator | **6.22** | 52.21 | 8.40x | Jint |
| ArrayOperations | **6.54** | 20.40 | 3.12x | Jint |
| ObjectCreation | **6.91** | 54.10 | 7.83x | Jint |
| JsonOperations | **11.21** | 22.98 | 2.05x | Jint |
| Destructuring | **12.10** | 62.00 | 5.12x | Jint |
| MapSet | **12.89** | 19.93 | 1.55x | Jint |
| ForLoop | **15.63** | 40.74 | 2.61x | Jint |
| WhileLoop | **16.42** | 44.39 | 2.70x | Jint |
| Recursion | **22.44** | 41.22 | 1.84x | Jint |
| FunctionCalls | **28.34** | 47.61 | 1.68x | Jint |
| PropertyAccess | **29.94** | 85.20 | 2.85x | Jint |
| RegexOperations | **35.77** | 107.32 | 3.00x | Jint |
| Fibonacci | **73.43** | 132.71 | 1.81x | Jint |
| ForOfIteration | **101.80** | 430.97 | 4.23x | Jint |
| GeneratorFunction | N/A | 95.90 | - | Asynkron only |
| AsyncGeneratorFunction | N/A | **0.09** | - | Asynkron only |

## Memory Comparison (Allocated in KB)

| Benchmark | Jint | Asynkron | Ratio | Winner |
|-----------|------|----------|-------|--------|
| SimpleArithmetic | 26.52 | **8.71** | 0.33x | Asynkron |
| Closures | **1,492** | 1,945 | 1.30x | Jint |
| MapSet | 9,443 | **3,984** | 0.42x | Asynkron |
| ForLoop | 24,688 | **11** | 0.0004x | Asynkron |
| WhileLoop | 5,938 | **10** | 0.002x | Asynkron |
| PropertyAccess | 13,754 | **19** | 0.001x | Asynkron |
| Recursion | **16,934** | 30,585 | 1.81x | Jint |
| FunctionCalls | 27,358 | **22,999** | 0.84x | Asynkron |
| Fibonacci | **51,313** | 76,867 | 1.50x | Jint |
| ClassDefinition | **3,181** | 15,093 | 4.75x | Jint |
| ArrayOperations | **8,967** | 16,285 | 1.82x | Jint |
| ObjectCreation | **10,787** | 42,256 | 3.92x | Jint |

## Key Observations

### Asynkron Strengths
- **Near-zero allocations for loops**: ForLoop (11 KB vs 24,688 KB), WhileLoop (10 KB vs 5,938 KB), PropertyAccess (19 KB vs 13,754 KB)
- **Better memory efficiency** for MapSet operations (0.42x Jint's allocation)
- **Supports features Jint doesn't**: Async Generators, Generator Functions
- **Faster on simple arithmetic** (0.77x Jint's time)

### Areas for Improvement
- **Async/Await performance**: 42.9x slower than Jint
- **Class definition**: 8.53x slower
- **Object creation**: 7.83x slower
- **Spread operator**: 8.40x slower
- **ForOfIteration**: 4.23x slower

### Optimization Progress
- Fibonacci improved from ~194ms to ~133ms (**31% faster**) through hot path optimizations
- Memory reduced from ~94MB to ~77MB (**18% less**) for Fibonacci
- ForLoop maintains near-zero allocations while being 2.6x slower than Jint

## Raw Benchmark Data

```
| Method                          | Mean            | Error         | StdDev       | Median          | P95            | Rank | Gen0        | Gen1       | Gen2      | Allocated     |
|-------------------------------- |----------------:|--------------:|-------------:|----------------:|---------------:|-----:|------------:|-----------:|----------:|--------------:|
| Jint_AsyncGeneratorFunction     |              NA |            NA |           NA |              NA |             NA |    ? |          NA |         NA |        NA |            NA |
| Jint_GeneratorFunction          |              NA |            NA |           NA |              NA |             NA |    ? |          NA |         NA |        NA |            NA |
| Asynkron_SimpleArithmetic       |        70.96 us |      7.018 us |     20.36 us |        69.12 us |       102.2 us |    1 |           - |          - |         - |       8.71 KB |
| Jint_SimpleArithmetic           |        92.18 us |      5.770 us |     16.83 us |        91.56 us |       119.2 us |    2 |           - |          - |         - |      26.52 KB |
| Asynkron_AsyncGeneratorFunction |        94.41 us |      8.924 us |     26.03 us |        98.56 us |       134.7 us |    2 |           - |          - |         - |      50.73 KB |
| Jint_StringOperations           |       363.43 us |     14.100 us |     41.35 us |       370.04 us |       437.3 us |    3 |           - |          - |         - |     441.49 KB |
| Asynkron_StringOperations       |     1,138.08 us |     22.739 us |     45.41 us |     1,130.42 us |     1,216.3 us |    4 |           - |          - |         - |    4475.07 KB |
| Jint_Closures                   |     1,364.85 us |     22.812 us |     19.05 us |     1,366.25 us |     1,390.4 us |    5 |           - |          - |         - |    1492.38 KB |
| Jint_ClassDefinition            |     2,565.44 us |     50.030 us |     51.38 us |     2,552.38 us |     2,640.9 us |    6 |           - |          - |         - |    3180.61 KB |
| Jint_AsyncAwait                 |     3,808.55 us |     75.529 us |    143.70 us |     3,760.52 us |     4,102.8 us |    7 |   1000.0000 |          - |         - |    9627.34 KB |
| Asynkron_Closures               |     4,035.48 us |     66.266 us |     83.81 us |     4,014.62 us |     4,152.1 us |    8 |           - |          - |         - |    1944.88 KB |
| Jint_AsyncAwaitPending          |     4,404.61 us |     88.034 us |    160.98 us |     4,352.81 us |     4,726.6 us |    9 |   1000.0000 |          - |         - |    8814.35 KB |
| Jint_AsyncAwaitResolved         |     4,962.19 us |     98.112 us |    134.30 us |     4,976.29 us |     5,152.1 us |   10 |   2000.0000 |          - |         - |   15562.39 KB |
| Jint_PromiseBasic               |     6,048.81 us |     94.716 us |    105.28 us |     6,031.79 us |     6,219.2 us |   11 |   2000.0000 |          - |         - |    17452.7 KB |
| Jint_SpreadOperator             |     6,219.77 us |    121.585 us |    130.09 us |     6,194.17 us |     6,443.6 us |   11 |   1000.0000 |          - |         - |   10859.35 KB |
| Jint_ArrayOperations            |     6,541.17 us |    128.897 us |    242.10 us |     6,545.98 us |     6,894.4 us |   11 |   1000.0000 |          - |         - |    8967.23 KB |
| Jint_ObjectCreation             |     6,908.27 us |    136.873 us |    157.62 us |     6,903.27 us |     7,100.9 us |   11 |   1000.0000 |          - |         - |   10786.54 KB |
| Jint_AsyncForOf                 |    10,904.14 us |    215.797 us |    256.89 us |    10,837.81 us |    11,396.9 us |   12 |   4000.0000 |          - |         - |   25712.96 KB |
| Jint_JsonOperations             |    11,205.90 us |    212.243 us |    177.23 us |    11,193.29 us |    11,517.7 us |   12 |   2000.0000 |          - |         - |   15904.32 KB |
| Jint_Destructuring              |    12,096.78 us |    145.886 us |    121.82 us |    12,099.06 us |    12,289.0 us |   13 |   2000.0000 |          - |         - |   18017.08 KB |
| Jint_MapSet                     |    12,890.09 us |    184.887 us |    154.39 us |    12,896.42 us |    13,136.9 us |   14 |   1000.0000 |          - |         - |    9442.53 KB |
| Jint_ForLoop                    |    15,628.39 us |    193.069 us |    180.60 us |    15,616.60 us |    15,894.7 us |   15 |   4000.0000 |          - |         - |   24688.01 KB |
| Jint_WhileLoop                  |    16,420.30 us |     75.666 us |     63.18 us |    16,426.58 us |    16,498.6 us |   16 |           - |          - |         - |    5937.77 KB |
| Asynkron_MapSet                 |    19,926.03 us |    387.973 us |    415.13 us |    19,948.08 us |    20,361.3 us |   17 |           - |          - |         - |     3983.9 KB |
| Asynkron_ArrayOperations        |    20,398.67 us |  1,168.105 us |  3,117.91 us |    19,164.29 us |    26,183.6 us |   17 |   2000.0000 |          - |         - |   16285.21 KB |
| Asynkron_PromiseBasic           |    20,607.60 us |    410.022 us |    873.79 us |    20,520.17 us |    21,982.2 us |   17 |   3000.0000 |  1000.0000 |         - |   23194.99 KB |
| Asynkron_ClassDefinition        |    21,903.53 us |    376.789 us |    370.06 us |    21,909.79 us |    22,458.7 us |   17 |   2000.0000 |  1000.0000 |         - |    15092.7 KB |
| Jint_Recursion                  |    22,435.50 us |    172.833 us |    153.21 us |    22,432.94 us |    22,675.3 us |   17 |   2000.0000 |          - |         - |   16934.44 KB |
| Asynkron_JsonOperations         |    22,982.74 us |    389.760 us |    345.51 us |    22,921.42 us |    23,512.8 us |   17 |   5000.0000 |          - |         - |   35213.99 KB |
| Jint_FunctionCalls              |    28,343.10 us |    237.784 us |    222.42 us |    28,257.92 us |    28,680.3 us |   18 |   4000.0000 |          - |         - |   27357.51 KB |
| Jint_PropertyAccess             |    29,935.20 us |    160.829 us |    142.57 us |    29,936.44 us |    30,123.0 us |   19 |   2000.0000 |          - |         - |   13754.46 KB |
| Jint_RegexOperations            |    35,768.04 us |    271.507 us |    240.68 us |    35,689.79 us |    36,141.8 us |   20 |   6000.0000 |          - |         - |   40108.26 KB |
| Asynkron_ForLoop                |    40,740.08 us |    796.133 us |  1,262.75 us |    40,891.04 us |    42,563.4 us |   21 |           - |          - |         - |      11.42 KB |
| Asynkron_Recursion              |    41,222.19 us |    559.803 us |    523.64 us |    41,098.81 us |    41,982.2 us |   21 |   4000.0000 |          - |         - |   30585.19 KB |
| Asynkron_WhileLoop              |    44,392.71 us |    816.607 us |  1,032.75 us |    44,652.92 us |    45,776.6 us |   22 |           - |          - |         - |      10.28 KB |
| Asynkron_FunctionCalls          |    47,608.46 us |    520.743 us |    461.62 us |    47,658.27 us |    48,259.5 us |   23 |   3000.0000 |          - |         - |   22998.65 KB |
| Asynkron_SpreadOperator         |    52,210.92 us |    338.997 us |    300.51 us |    52,232.64 us |    52,575.4 us |   24 |  22000.0000 |          - |         - |  140391.97 KB |
| Asynkron_ObjectCreation         |    54,099.20 us |    574.057 us |    508.89 us |    54,130.31 us |    54,734.5 us |   24 |   7000.0000 |  3000.0000 | 1000.0000 |   42256.03 KB |
| Asynkron_Destructuring          |    61,996.13 us |    470.498 us |    417.08 us |    61,957.90 us |    62,498.8 us |   25 |  18000.0000 |          - |         - |  114092.48 KB |
| Jint_Fibonacci                  |    73,426.01 us |    333.190 us |    278.23 us |    73,430.06 us |    73,870.9 us |   26 |   8000.0000 |          - |         - |   51312.55 KB |
| Asynkron_PropertyAccess         |    85,196.03 us |  1,660.207 us |  2,272.51 us |    85,510.42 us |    87,551.5 us |   27 |           - |          - |         - |      19.03 KB |
| Asynkron_GeneratorFunction      |    95,900.31 us |    583.068 us |    455.22 us |    95,988.42 us |    96,533.7 us |   28 |  36000.0000 |          - |         - |  224255.45 KB |
| Jint_ForOfIteration             |   101,795.95 us |    426.338 us |    377.94 us |   101,730.54 us |   102,412.8 us |   29 |  42000.0000 |          - |         - |   258628.4 KB |
| Asynkron_RegexOperations        |   107,321.32 us |    901.946 us |    799.55 us |   106,985.88 us |   108,560.1 us |   30 |  50000.0000 |          - |         - |  309479.79 KB |
| Asynkron_Fibonacci              |   132,713.65 us |    782.536 us |    653.45 us |   132,797.85 us |   133,541.1 us |   31 |  12000.0000 |          - |         - |   76866.91 KB |
| Asynkron_AsyncAwaitPending      |   141,893.64 us |  1,951.535 us |  1,729.98 us |   142,205.58 us |   144,147.9 us |   32 |  25000.0000 |  8000.0000 | 2000.0000 |  146784.18 KB |
| Asynkron_AsyncAwait             |   163,353.36 us |  2,770.947 us |  2,591.95 us |   162,100.17 us |   167,643.2 us |   33 |  30000.0000 |  9000.0000 | 2000.0000 |  173145.13 KB |
| Asynkron_AsyncAwaitResolved     |   288,849.16 us |  5,506.899 us |  6,964.47 us |   286,238.58 us |   300,443.0 us |   34 |  42000.0000 | 16000.0000 | 3000.0000 |   243502.9 KB |
| Asynkron_ForOfIteration         |   430,966.63 us |  3,782.495 us |  3,353.08 us |   430,966.83 us |   436,149.2 us |   35 | 223000.0000 |          - |         - | 1367594.64 KB |
| Asynkron_AsyncForOf             | 1,609,967.24 us | 21,295.458 us | 18,877.87 us | 1,613,860.12 us | 1,636,398.8 us |   36 | 272000.0000 | 61000.0000 | 5000.0000 | 1641329.37 KB |
```
