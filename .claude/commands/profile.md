---
description: Run profiler on benchmarks (CPU, memory, exceptions, heap)
allowed-tools: Bash(./tools/profile:*), Bash(dotnet:*)
argument-hint: <benchmark> [--cpu|--memory|--exception|--heap|--compare]
---

Run the profiler script on the specified benchmark.

For full methodology and details, see [agents/how-to-profiling.md](../../agents/how-to-profiling.md).

## Quick Usage

Execute the profiler:

```bash
./tools/profile $ARGUMENTS
```

## Examples

CPU + memory profiling (default):
```
/profile fib
/profile forloop
/profile all
```

Specific profiling modes:
```
/profile fib --cpu        # CPU only
/profile fib --memory     # Memory only
/profile fib --exception  # Exception profiling
/profile fib --heap       # Heap snapshot only
```

Compare with Jint:
```
/profile --compare
```

## Output Interpretation

The profiler outputs:
1. **Hot functions** - Time spent and call counts per function
2. **Allocation call graph** - Which methods allocate and their callers
3. **JsEngine time percentage** - How much time is in our code vs runtime

Example output:
```
=== JSENGINE HOT FUNCTIONS ===
   Time (ms)      Calls  Function
-------------------------------------------------
    38805.39      19533  TypedAstEvaluator.EvaluateExpression...

JsEngine time: 166928.10 ms (91.8% of total)
```

## Manual BenchmarkDotNet

For more detailed benchmarks:
```bash
cd benchmarks/Asynkron.JsEngine.Benchmarks
dotnet run -c Release -- --filter "*Fibonacci*"
```
