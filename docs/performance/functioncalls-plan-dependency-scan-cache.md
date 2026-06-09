# Functioncalls plan dependency scan cache

## Selection

The required full benchmark baseline selected `functioncalls` for this run
because it had the largest absolute Asynkron-side gap in the fresh table:

```text
profile                    asynkron_ms  jint_ms  delta
functioncalls                     6031     2150  Jint 2.81x faster
```

`classdef` was still a Jint win (`836 ms` vs `259 ms`), but the current
`origin/main` branch already contains the class constructor slot-storage cache,
so this run stayed on the fresh `functioncalls` owner instead of repeating the
same classdef slice.

Focused unpatched baseline rows were:

```text
functioncalls                  6015     2172  Jint 2.77x faster
functioncalls                  6059     2156  Jint 2.81x faster
functioncalls                  6054     2202  Jint 2.75x faster
```

Baseline signal: `functioncalls` Asynkron focused rows = 6015, 6059, 6054 ms
(average 6043 ms).

## Profile owner

The requested CPU profile was run three times:

```bash
rtk ./tools/profile functioncalls --cpu --calltree-depth 40 --calltree-width 40
```

All three runs named the same owner:

```text
UnifiedBytecodeVirtualMachine.ExecutePreparedCall
  TypedAstEvaluator.SyncFunctionInvoker.InvokeWithContextSlow
    TypedAstEvaluator.SyncFunctionInvoker.CanUseProductionUnifiedBytecodeFastPath
      UnifiedBytecodeProductionEligibility.TryGetExpressionProgram
      UnifiedBytecodeProductionEligibility.ContainsOrdinaryDynamicIdentifierExpression
```

The repeated cost came from re-scanning immutable `ExecutionPlan` instruction
and expression-program state while deciding whether ordinary dynamic identifier
and implicit `arguments` dependencies are present. Those answers depend only on
the plan's instruction stream and activation slot shape, not on call arguments
or runtime environment values.

## Change

`ExecutionPlan` now owns two tri-state caches for those pure dependency scans:

- ordinary dynamic identifier dependency
- only implicit `arguments` object dynamic identifier dependency

The production eligibility helpers read the cached value before scanning and
publish the first computed result with `Volatile.Write`. Benign races are safe
because each writer derives the same boolean from the immutable plan. The
change does not cache descriptor-dependent eligibility, compiled programs, or
runtime environment results.

## Result

Focused patched rows were:

```text
functioncalls                  5296     2196  Jint 2.41x faster
functioncalls                  5123     2213  Jint 2.31x faster
functioncalls                  5135     2179  Jint 2.36x faster
```

Final signal: `functioncalls` Asynkron focused rows = 5296, 5123, 5135 ms
(average 5179 ms).

Signal delta: 6043 ms -> 5179 ms, 864 ms faster, about 14.3% improvement.

A post-change profile no longer listed
`ContainsOrdinaryDynamicIdentifierExpression` in the filtered hot table; the
remaining cost is still under production call dispatch, expression program
lookup, dynamic identifier call target preparation, and fresh-engine setup.

## Commands run

```bash
rtk ./benchmark.sh
rtk ./tools/profile functioncalls --cpu --calltree-depth 40 --calltree-width 40
rtk ./tools/profile functioncalls --cpu --calltree-depth 40 --calltree-width 40
rtk ./tools/profile functioncalls --cpu --calltree-depth 40 --calltree-width 40
rtk dotnet build -c Release
rtk ./benchmark.sh --no-build functioncalls
rtk ./benchmark.sh --no-build functioncalls
rtk ./benchmark.sh --no-build functioncalls
rtk ./tools/profile functioncalls --cpu --calltree-depth 40 --calltree-width 40
```

Focused semantic and allocation-stability checks are recorded in the issue
Build Update for this run.
