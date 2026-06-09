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

## Retry gate

The #3537 dynamic call-target symbol cache remains off-limits unless fresh
profile evidence satisfies this guard before any runtime edit touches
`PrepareDynamicIdentifierCallTarget`, `UnifiedBytecodeProgram` string/name
storage, or dynamic identifier lookup through `JsEnvironment`.

Required profile command:

```bash
rtk ./tools/profile functioncalls --cpu --calltree-depth 40 --calltree-width 40
```

Retry threshold: at least two fresh selected-workload CPU profiles must show
`UnifiedBytecodeVirtualMachine.PrepareDynamicIdentifierCallTarget` as a
repeated top owner at or above 10% inclusive CPU/sample share for the selected
`functioncalls` workload. If the profiler output does not print percentages,
use the same profile's call-tree timing or sample totals to record the
equivalent inclusive share calculation.

Owner signal: the repeated cost must implicate dynamic call-target name
preparation or string-to-symbol materialization. A subtree that only shows
`JsEnvironment.TryGetIdentifierJsValueAfterWithMiss` lookup cost does not
satisfy this gate, and neither does a broad `functioncalls` loss with no
separate `PrepareDynamicIdentifierCallTarget` owner.

Keep this symbol-cache retry separate from the larger production call dispatch
residual around `ExecutePreparedCall` / `InvokeWithContextSlow`. Passing this
gate would only permit a new symbol-cache experiment; it would not justify
claims that the cache addresses those dispatch and eligibility residuals.

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
