# Performance: forofiteration repeated compile ownership

## Summary

Fresh `forofiteration` profiling on 2026-06-13 confirms that the benchmark-shaped
repeated IIFE reaches the production unified-bytecode route, but the remaining
compile/eligibility cost is still owned by the fresh `SyncFunctionInvoker` path.

The measured owner is not iterator-driver runtime. In the focused invoker-root
CPU tree, `UnifiedBytecodeProductionEligibility.Evaluate` and
`UnifiedBytecodeCompiler.TryCompile` together account for about 36.8% of
`SyncFunctionInvoker.InvokeWithContextSlow`, while sampled iterator move-next
runtime is about 1.7%. The largest follow-up owner small enough to pursue is
compiler-side builder growth/materialization inside the repeated per-invoker
compile, especially `TryAppendExpressionProgramOps`, `TryCompileBlock`, slot
layout construction, and immutable-array materialization.

Do not solve this by storing accepted `UnifiedBytecodeProgram` instances or
accepted route-admission answers on `ExecutionPlan`. ADR 0385 keeps those
descriptor-sensitive results on `SyncFunctionInvoker`.

## Evidence

Baseline timestamp: 2026-06-13T07:09:04Z
Baseline signal: `rtk ./tools/profile forofiteration --route-hits`

```text
Done in 49169ms (avg 24.58ms per iteration)
Route hits: unified-bytecode-production-fast-path=4000
```

CPU command:

```bash
rtk ./tools/profile forofiteration --cpu --root InvokeWithContextSlow --calltree-depth 50 --calltree-width 45
```

Focused invoker-root excerpt:

```text
Call Tree (Total Time) - root: InvokeWithContextSlow
12082.79 ms 100.0% 181x TypedAstEvaluator.SyncFunctionInvoker.InvokeWithContextSlow
+- 12001.99 ms 99.3% 183x TypedAstEvaluator.SyncFunctionInvoker.TryInvokeIrFast
   +- 11518.84 ms 95.3% 181x TypedAstEvaluator.SyncFunctionInvoker.TryInvokeProductionUnifiedBytecode
      +- 6444.20 ms 53.3% 129x UnifiedBytecodeVirtualMachine.Execute
      |  +- 1219.61 ms 10.1% 54x JsArray.Push
      |  +- 206.14 ms 1.7% 14x UnifiedBytecodeVirtualMachine.TryMoveIteratorNext
      |  +- 114.44 ms 0.9% 11x Stack<__Canon>.PushWithResize
      +- 4442.77 ms 36.8% 148x TypedAstEvaluator.SyncFunctionInvoker.TryGetProductionUnifiedBytecodeProgram
      |  +- 4282.38 ms 35.4% 147x UnifiedBytecodeProductionEligibility.Evaluate
      |     +- 4282.38 ms 35.4% 147x UnifiedBytecodeProductionEligibility.EvaluateCore
      |        +- 3026.33 ms 25.0% 125x UnifiedBytecodeCompiler.TryCompile
      |        |  +- 2108.02 ms 17.4% 96x UnifiedBytecodeCompiler.TryCompileBlock
      |        |  |  +- 1428.49 ms 11.8% 76x UnifiedBytecodeCompiler.TryAppendExpressionProgramOps
      |        |  |  +- 430.58 ms 3.6% 19x UnifiedBytecodeCompiler.TryAppendTryRegion
      |        |  +- 596.89 ms 4.9% 33x UnifiedBytecodeCompiler.BuildSlotLayout
      |        |  +- 141.80 ms 1.2% 11x ImmutableArray.Builder<UnifiedBytecodeInstruction>.ToImmutable
      |        +- 1211.87 ms 10.0% 53x UnifiedBytecodeProductionEligibility.TryFindPlanDecline
      +- 627.85 ms 5.2% 28x TypedAstEvaluator.SyncFunctionInvoker.RentProductionUnifiedBytecodeSlots
```

Allocation command:

```bash
rtk ./tools/profile forofiteration --memory --calltree-depth 40 --calltree-width 40
```

Allocation excerpt:

```text
Metric          Value
Total allocated 137.68 MB

Allocation By Type (Sampled)
Type                              Count     Total
JsValue[]                           691  72.57 MB
UnifiedBytecodeInstruction[]        193  19.62 MB
Int32[]                              66   6.71 MB
UnifiedBytecodeDriverDescriptor[]    24   2.44 MB
String                               18   1.87 MB
```

The sampled allocation tree ties the largest compiler allocations to the same
repeated route path:

```text
20.13 MB ImmutableArray.Builder<JsValue>.Add
  <- UnifiedBytecodeCompiler.TryAppendExpressionProgramOps
  <- UnifiedBytecodeCompiler.TryCompileBlock
  <- UnifiedBytecodeCompiler.TryCompile
  <- UnifiedBytecodeProductionEligibility.EvaluateCore
  <- UnifiedBytecodeProductionEligibility.Evaluate
  <- TypedAstEvaluator.SyncFunctionInvoker.TryGetProductionUnifiedBytecodeProgram

11.39 MB ImmutableArray.Builder<UnifiedBytecodeInstruction>.Add
  <- UnifiedBytecodeCompiler.TryAppendExpressionProgramOps
  <- UnifiedBytecodeCompiler.TryCompileBlock
  <- UnifiedBytecodeCompiler.TryCompile
  <- UnifiedBytecodeProductionEligibility.EvaluateCore
```

## Owner conclusion

- Route entry: proven. The profile reports 4000
  `unified-bytecode-production-fast-path` hits.
- Invocation descriptor/setup: present in
  `TryGetProductionUnifiedBytecodeProgram` and descriptor construction, but not
  a separately dominant sampled owner in the focused tree.
- Eligibility scan: visible at about 10.0% under
  `UnifiedBytecodeProductionEligibility.TryFindPlanDecline`.
- Compile: visible at about 25.0% under `UnifiedBytecodeCompiler.TryCompile`,
  with builder growth/materialization as the clearest retained owner.
- Iterator-driver runtime: not the repeated-compile owner in this workload;
  sampled `TryMoveIteratorNext` is about 1.7%.

Recommended follow-up: reduce compile-time builder growth and immutable-array
materialization for repeated production UBC compilation, without caching the
accepted `UnifiedBytecodeProgram` or descriptor-sensitive route decision on
`ExecutionPlan`. A safe next slice should stay inside
`UnifiedBytecodeCompiler` and prove progress with the same invoker-root CPU
profile plus the memory call tree above.

## Boundary

ADR 0385 remains binding:

- `ExecutionPlan` may cache plan-pure facts only.
- Accepted production UBC programs and route-admission answers stay on
  `SyncFunctionInvoker`.
- Future sharing must first define a narrower immutable artifact that is not
  itself a route-admission answer.

No runtime code was changed for this evidence pass.
