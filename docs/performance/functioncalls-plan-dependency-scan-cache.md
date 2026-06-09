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

## Post-PR #3547 route-cache reprofile

On 2026-06-09, local task `agentmanual1780998418927155000` reprofiled current
`origin/main` at `2864b309d`, after PR #3547 / ADR 0379 cached the
invoker-owned production route-admission decision on `SyncFunctionInvoker`.
This is current-main evidence, not a branch-local optimization result.

Focused comparison rows stayed below parity and were timing-noisy, so the
current run treats them as residual-owner evidence rather than a speedup claim:

```text
command                                      functioncalls     classdef
rtk ./benchmark.sh functioncalls classdef    4578 vs 2017 ms   850 vs 483 ms
rtk ./benchmark.sh --no-build ...            4643 vs 2133 ms   798 vs 279 ms
rtk ./benchmark.sh --no-build ...            6639 vs 2231 ms   828 vs 263 ms
```

Route-hit probes confirmed both workloads still enter the production
unified-bytecode fast path:

```text
rtk ./tools/profile functioncalls --route-hits
Route hits: unified-bytecode-production-fast-path=8000010

rtk dotnet tools/ProfileRunner/bin/Release/net10.0/ProfileRunner.dll \
  --route-hits --fresh-engine-per-iteration --wrap-iife classdef
Route hits: unified-bytecode-production-fast-path=120020
```

The direct `ProfileRunner` command is intentional for `classdef`: `benchmark.sh`
wraps `classdef` in an IIFE through `tools/compare-jint-profiles`, while
`tools/profile` only forwards that wrapper for `simplearithmetic`.

The current `functioncalls` CPU profile no longer names the plan-pure
dependency scans or the full invoker route-admission check as the main owner.
With `UnifiedBytecodeVirtualMachine.Execute` as the call-tree root, the sampled
residual was:

```text
UnifiedBytecodeVirtualMachine.Execute                 5743.26 ms
  Buffer.BulkMoveWithWriteBarrier                     5315.97 ms  92.6%
  PrepareDynamicIdentifierCallTarget                   369.23 ms   6.4%
    JsEnvironment.TryGetIdentifierJsValueAfterWithMiss 369.23 ms
      Dictionary<...,ResolvedIdentifierBinding>.Resize 338.37 ms
```

`UnifiedBytecodeProductionEligibility.EvaluateCore` remained visible only as a
small setup/top-function sample (`58.34 ms` in the same run). That keeps the
post-#3547 owner split clear: the retained invoker route cache removed the
route-admission scan as the large repeat owner, while the remaining
`functioncalls` work sits in production VM execution, runtime data movement,
dynamic identifier lookup/cache growth, and still-unseparated call-dispatch or
call-argument surfaces. The dynamic identifier call-target subtree was below
the retry threshold recorded in
`docs/performance/failed-functioncalls-dynamic-symbol-cache.md`.

The wrapped `classdef` CPU profile stayed in the existing constructor/super and
callback/runner families, not in the `functioncalls` route-cache owner:

```text
TypedAstEvaluator.SyncFunctionInvoker.InvokeWithContextSlow 3814.99 ms
UnifiedBytecodeVirtualMachine.Execute                       3063.63 ms
UnifiedBytecodeVirtualMachine.ExecutePreparedCall           2305.24 ms
TypedAstEvaluator.ExecutionPlanRunner.ExecuteInstructionLoop 1904.22 ms

ExecuteInstructionLoop root:
  ExecuteProgramConstructNoSpread -> ReflectHelper.Construct 818.63 ms 44.9%
  ExecutePreparedSuperConstruct -> ConstructNoSpread         476.89 ms 26.2%
  CreateSimpleBaseClassConstructorEnvironment                 92.65 ms  5.1%
  TryGetProductionUnifiedBytecodeProgram / eligibility        19.24 ms  1.1%
```

This preserves the no-parity boundary: PR #3547 is evidence for the invoker
route-cache slice only. Future `functioncalls` and `classdef` performance work
should continue to split descriptor/runtime dispatch, call-argument,
dynamic-identifier lookup, constructor/super dispatch, property-store, and
callback owners before retaining another runtime change.

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
