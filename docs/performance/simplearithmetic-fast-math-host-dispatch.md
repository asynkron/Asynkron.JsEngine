# SimpleArithmetic Fast Math Host Dispatch

Date: 2026-05-27

## Selected Profile

The required pre-edit comparison table still showed `simplearithmetic` as a
meaningful top loss:

```text
profile                    asynkron_ms  jint_ms  delta
simplearithmetic                   640      165  Jint 3.88x faster
```

Repeated focused baseline rows were noisy but consistently slower than the
post-change signal:

```text
simplearithmetic               2244      459  Jint 4.89x faster
simplearithmetic               1411      416  Jint 3.39x faster
simplearithmetic               2395      369  Jint 6.49x faster
```

Baseline timestamp: 2026-05-27T11:20:55Z
Baseline signal: `simplearithmetic` Asynkron focused rows = 2244, 1411, 2395 ms

## CPU Profile Evidence

The required profile command was run three times:

```bash
rtk ./tools/profile simplearithmetic --cpu --calltree-depth 40 --calltree-width 40
```

The corrected profile wrapper now passes `--wrap-iife simplearithmetic`, so the
profiles measured the intended top-level `let` workload instead of redeclaration
errors.

The repeated engine-owned shape was:

```text
ExecuteInstructionLoop
  HandleEvaluateAndDiscard
    EvaluateExpressionProgram
      ExecuteProgramCall
        InvokeCallableNoArgs
          SyncFunctionInvoker.InvokeWithContext...
        InvokeCallableSingleArg / InvokeCallableTwoArgs
          InvokeCallableJsValueGeneric
            CastHelpers.Box
```

The outer IIFE call remains the largest cost, but `Math.sqrt` and `Math.pow`
inside the expression program repeatedly used generic host-function argument
dispatch and boxed `SingleValueArgs` / `TwoValueArgs`.

## Change

The retained slice is deliberately narrow:

- `HostFunction` can carry optional direct one-argument and two-argument fast
  handlers.
- `MathPrototype.ConfigurePrototype` marks only the generated `Math.sqrt` and
  `Math.pow` host functions with direct handlers.
- Expression-call dispatch consults those handlers before falling back to the
  generic `IReadOnlyList<JsValue>` host-function path.

This does not change property lookup or replacement behavior. If user code
replaces `Math.sqrt` or `Math.pow`, the replacement callable is not marked and
continues through the existing generic path.

## Final Signal

Repeated focused comparison after the change:

```text
simplearithmetic                415      504  Asynkron 1.21x faster
simplearithmetic                752      136  Jint 5.53x faster
simplearithmetic                429      159  Jint 2.70x faster
simplearithmetic                391      120  Jint 3.26x faster
simplearithmetic                403      121  Jint 3.33x faster
simplearithmetic                389      256  Jint 1.52x faster
```

Final timestamp: 2026-05-27T11:29:39Z
Final signal: `simplearithmetic` Asynkron focused rows = 415, 752, 429, 391, 403, 389 ms
Signal delta: best comparable focused baseline 1411 ms to worst final 752 ms =
659 ms faster, about 47% lower; median final around 409 ms is about 36% lower
than the 640 ms pre-edit full-table row.

## Verification

Focused semantic verification:

```text
rtk dotnet test tests/Asynkron.JsEngine.Tests --filter "FullyQualifiedName~FoundationTests|FullyQualifiedName~JsEvaluatorTests.Math|FullyQualifiedName~JavaScriptComplianceTests" -- xUnit.MaxParallelThreads=1 -timeout 20000
ok dotnet test: 853 tests passed, 7 warnings in 4 projects
```

The canonical internal quality gate remains `rtk make quality` and is delegated
to the orchestrator-run verification stage.
