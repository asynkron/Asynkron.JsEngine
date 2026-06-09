# Failed functioncalls dynamic symbol cache

## Selection

Issue `autrun-dj48yh3dmb4w-2ea9565242` initially investigated `promise`, but the
required fresh full benchmark table did not keep it as a top loss:

```text
promise                            432      381  Jint 1.13x faster
```

The same table showed `functioncalls` as the largest absolute Asynkron-side
gap, so this run pivoted to that measured owner instead of repeating the
Promise slice:

```text
functioncalls                     5129     2063  Jint 2.49x faster
```

Baseline signal: `functioncalls` full-table Asynkron row = 5129 ms.

## Profile owner

The requested CPU profile was run three times:

```bash
rtk ./tools/profile functioncalls --cpu --calltree-depth 40 --calltree-width 40
```

All three runs repeated the same residual owner already left after the prior
plan-dependency scan cache:

```text
UnifiedBytecodeVirtualMachine.ExecutePreparedCall
  TypedAstEvaluator.SyncFunctionInvoker.InvokeWithContextSlow
    TypedAstEvaluator.SyncFunctionInvoker.CanUseProductionUnifiedBytecodeFastPath
      UnifiedBytecodeProductionEligibility.TryGetExpressionProgram
UnifiedBytecodeVirtualMachine.PrepareDynamicIdentifierCallTarget
  JsEnvironment.TryGetIdentifierJsValueAfterWithMiss
```

The current profile still spends most sampled time in production call dispatch
and eligibility. A smaller repeated subtree comes from preparing dynamic
identifier call targets for script-level function calls such as `add(...)`,
`mul(...)`, `sub(...)`, and `div(...)`.

## Trial

The runtime trial added a lazy per-`UnifiedBytecodeProgram` cache from string
constant index to interned `Symbol`, then used that cache in the sync and
resumable `PrepareDynamicIdentifierCallTarget` handlers. The goal was to remove
per-call `Symbol.Intern(name)` work from the repeated free-function call target
path without changing dynamic binding lookup semantics.

The edit built cleanly:

```text
rtk dotnet build -c Release
ok dotnet build: 11 projects, 0 errors, 7 warnings
```

Focused patched rows were:

```text
functioncalls                     5040     2132  Jint 2.36x faster
functioncalls                     5012     2147  Jint 2.33x faster
functioncalls                     5029     2147  Jint 2.34x faster
```

Final signal: `functioncalls` Asynkron focused rows = 5040, 5012, 5029 ms
(average 5027 ms).

Signal delta: 5129 ms -> 5027 ms, 102 ms faster, about 2.0% improvement.

## Outcome

The trial missed the required 10% retained-performance threshold, so the runtime
edit was reverted. No source optimization remains from this attempt.

Post-revert focused row:

```text
functioncalls                     5035     2128  Jint 2.37x faster
```

The result is still useful: caching interned symbols for dynamic call target
names is not enough to move the current `functioncalls` benchmark. Future work
should avoid retrying this exact cache unless a fresh profile shows
`PrepareDynamicIdentifierCallTarget` has become a much larger share of the
selected workload. The larger residual remains production call dispatch and
eligibility around `ExecutePreparedCall` / `InvokeWithContextSlow`.

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
rtk ./benchmark.sh --no-build functioncalls
rtk dotnet test tests/Asynkron.JsEngine.Tests -c Release --filter "FullyQualifiedName~CallTargetAdmissionTests|FullyQualifiedName~UnifiedBytecodeResumableDynamicIdentifierTests|FullyQualifiedName~PromiseTests|FullyQualifiedName~MicrotaskDrainingTests"
```
