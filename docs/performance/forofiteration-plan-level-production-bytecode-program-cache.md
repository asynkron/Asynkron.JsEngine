# forofiteration — plan-level production unified-bytecode program cache

## Summary

The `forofiteration` benchmark (a `(function(){ ... for (const n of arr) sum += n; ... })()`
IIFE run on a shared engine for 2000 iterations) lost to Jint by ~2.1x. Profiling showed roughly
**half the execution time was spent recompiling the same function** on every script evaluation:
`UnifiedBytecodeProductionEligibility.Evaluate` / `EvaluateCore` / `UnifiedBytecodeCompiler.TryCompile`
dominated the call tree even though the IIFE is structurally identical on every run.

Caching the accepted, compiled production unified-bytecode program **on the `ExecutionPlan`** (shared
across all `SyncFunctionInvoker` instances for a given function) removed the per-invocation recompile.
forofiteration went from a ~2.1x loss to **beating Jint**, a **~66% reduction in Asynkron time** in a
controlled A/B, with all 7036 internal tests still passing.

## Why it was slow

- Most comparison benchmarks run on a **shared engine** (`RunWithSharedEnginesAsync`): the engine is
  built once and the parsed program is evaluated N times. Engine construction and parsing are therefore
  amortized; only per-evaluation execution cost matters.
- Each script evaluation re-instantiates the inner function literal `(function(){...})`, producing a
  **fresh `SyncFunctionInvoker`** with a cold eligibility cache.
- `SyncFunctionInvoker` already had an **instance-level** cache for the accepted unified-bytecode
  program (`_unifiedBytecodeProductionProgram`, keyed by plan + `newTarget.IsUndefined`), but a fresh
  invoker every iteration meant that cache was always cold. So every one of the 2000 evaluations ran
  the **full eligibility scan + `UnifiedBytecodeCompiler.TryCompile`** to rebuild a byte-for-byte
  identical program.
- The script-level path (`UnifiedBytecodeProductionEligibility.EvaluateScript`) had the same problem at
  the top-level program scope: it recompiled the script's bytecode on every evaluation.

Top functions before the change (`./tools/profile forofiteration --cpu`, ~890 ms execution subtree):

```
 567.63  137 UnifiedBytecodeProductionEligibility.EvaluateCore
 495.54  135 SyncFunctionInvoker.TryGetProductionUnifiedBytecodeProgram
 472.11  136 UnifiedBytecodeProductionEligibility.Evaluate
 462.53  135 UnifiedBytecodeCompiler.TryCompile
```

## The fix

The accepted eligibility result (including the compiled `UnifiedBytecodeProgram`) is **structurally
determined by the plan and the `newTarget.IsUndefined` state** — it does not depend on per-call
runtime arguments, `this`, or captured closure *values*. A plan corresponds to exactly one
`FunctionExpression` source site (it is `IAstCacheable`), so every invoker for that plan computes an
identical program. The compiled program is also immutable and already reused across calls within a
single invoker, so reusing it across invokers is the same safety contract at wider scope.

Two plan-level caches were added on `ExecutionPlan` (stored as opaque `object?` to keep the core type
decoupled from the unified-bytecode program type, using the same volatile first-writer-wins pattern as
the existing `MarkProductionEligibilityPermanentDecline` cache):

1. **Script path** — `UnifiedBytecodeProductionEligibility.EvaluateScript(plan)` caches its result on
   the plan (`ScriptProductionEligibilityResult`). The script activation descriptor is a fixed constant,
   so the result depends only on the plan.

2. **Function-invoker path** — `SyncFunctionInvoker.TryGetProductionUnifiedBytecodeProgram` and the
   `CanUseCachedOrEvaluateProductionUnifiedBytecodeFastPath` gate consult a plan-level accepted-program
   cache keyed by `newTarget.IsUndefined` (`GetCachedAcceptedProductionProgram` /
   `SetCachedAcceptedProductionProgram`). A fresh invoker now reuses the program a sibling invoker
   already compiled, skipping both the structural fast-path re-scan and `TryCompile`.

### Safety gate

Some invoker descriptor inputs are applied **after construction** via setters
(`SetHomeObject`, `SetPrivateNameScope`, `SetCapturedPrivateNameScopes`, `SetSuperBinding`,
`SetIsClassConstructor`, `SetInstanceFields`) for methods, constructors, and private-scoped members.
Although these are role-determined and therefore still invariant per source site, the plan-level cache
is conservatively gated by `CanSharePlanLevelProductionProgram`, which only shares the program when the
invoker carries **none** of that role-specific state (i.e. plain functions and arrows — closures and
IIFEs). Methods/constructors keep using the per-invoker cache only, so behavior for them is unchanged.

## Evidence

Environment was noisy during measurement, so a **controlled A/B** was used: `functioncalls` (which is
unaffected by this change — its inner functions are called many times within a single evaluation, so
its per-invoker cache already warms) served as an environment control, measured in the same windows.

| benchmark        | baseline (clean, stashed) | with change | control? |
|------------------|---------------------------|-------------|----------|
| functioncalls    | ~8000 ms                  | ~8000 ms    | yes — flat |
| forofiteration   | ~1021 ms (median)         | ~350 ms (median) | — |

With the control flat across both windows, forofiteration dropped **~1021 ms → ~350 ms (~66% faster)**.
Against the original quiet-environment baseline (~725 ms), the post-change quiet median (~350 ms) is
still **~52% faster** — comfortably above the 10% acceptance threshold either way. forofiteration moved
from "Jint 2.1x faster" to "Asynkron 1.1–1.3x faster" in the comparison table.

`closures-lite` (also plain re-instantiated closures) improved as a side effect: its ratio went from
~9.7x to ~5.6x slower.

- All internal tests: **7036 passed, 0 failed, 2 skipped**
  (`dotnet test tests/Asynkron.JsEngine.Tests -c Release`).

## Files changed

- `src/Asynkron.JsEngine/Execution/ExecutionPlan.cs` — plan-level caches + accessors.
- `src/Asynkron.JsEngine/Execution/UnifiedBytecode/UnifiedBytecodeProductionEligibility.cs` —
  `EvaluateScript` consults/populates the script-level plan cache.
- `src/Asynkron.JsEngine/Ast/TypedAstEvaluator.SyncFunctionInvoker.cs` — function-invoker path
  consults/populates the plan-level accepted-program cache behind the `CanSharePlanLevelProductionProgram`
  gate.
