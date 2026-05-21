# Profiling

## Profiler Script (Preferred)
Use `./tools/profile` (builds ProfileRunner, runs asynkron-profiler, converts traces, prints hot functions and allocation call graphs).

Examples:
```bash
./tools/profile fib            # separate CPU-only + memory-only passes, scoped to Asynkron.JsEngine
./tools/profile forloop        # separate CPU-only + memory-only passes, scoped to Asynkron.JsEngine
./tools/profile all            # run all benchmarks
./tools/profile fib --cpu      # CPU only
./tools/profile fib --memory   # memory only
./tools/profile fib --exception # exception profiling
./tools/profile fib --heap     # heap snapshot only
./tools/profile --compare      # Jint comparison benchmarks
```

Default scoping contract:
- If no explicit mode is provided, `./tools/profile <profile>` runs CPU and memory as two separate profiler invocations.
- CPU defaults to `--root Asynkron.JsEngine --filter Asynkron.JsEngine`.
- Memory defaults to `--root Asynkron.JsEngine`.
- `--cpu` / `--memory` keep mode-only behavior but still add the same default scope when caller does not provide `--root` / `--filter`.
- Explicit `--cpu`, `--memory`, `--root`, and `--filter` flags are preserved and are not overridden.

Output example:
```
=== JSENGINE HOT FUNCTIONS ===
   Time (ms)      Calls  Function
-------------------------------------------------
    38805.39      19533  Asynkron.JsEngine.Ast.TypedAstEvaluator.EvaluateExpression...
    19769.23       9897  Asynkron.JsEngine.Ast.TypedAstEvaluator+SyncFunctionInvoker.Invoke...
    19753.25       9961  Asynkron.JsEngine.Ast.TypedAstEvaluator.EvaluateCall...

JsEngine time: 166928.10 ms (91.8% of total)
```
Allocation call graph example:
```
CreateNextIterationEnvironment
  Calls: 1048
  Allocated by:
    <- EvaluateLoopPlanJsValue (1048x, 100%)
         <- EvaluateForJsValue (4x)
```

## Manual Profiling

### BenchmarkDotNet quick start
```bash
cd benchmarks/Asynkron.JsEngine.Benchmarks
dotnet run -c Release -- --filter "*Fibonacci*"
```

### Detailed allocation trace
```bash
dotnet-trace collect \
  --profile gc-verbose \
  --format NetTrace \
  -o trace.nettrace \
  -- dotnet run -c Release \
     --project benchmarks/Asynkron.JsEngine.Benchmarks \
     --filter "JintComparisonBenchmarks.Asynkron_ForLoop"

dotnet-trace report trace.nettrace topN -n 30
# or
dotnet-trace convert trace.nettrace --format Speedscope
```
See `docs/memory-profiling.md` for deeper guidance.

## Known Allocation Hotspots (Fibonacci benchmark, Dec 2024)
- Before: 322 MB, 172 ms, Gen0 53k
- After round 1: 173.25 MB, ~150 ms, Gen0 28k
- After round 2 (lazy init, lock-free pools): 168.62 MB, 134.51 ms
- After round 3 (NumericResult + fast paths): 107.49 MB, 116.84 ms
- Remaining gap vs Jint: ~2.2x time, ~2.1x allocations
