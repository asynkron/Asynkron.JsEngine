# Arrayops Reduce Numeric Binary Fast Path

Date: 2026-05-27
Issue: autrun-ditkh3m7fjx4-a2cf88a53d

## Selected benchmark

`arrayops` was selected by the investigation handoff as the bounded array
callback slice. The pre-change focused benchmark in this worktree kept it as a
current Asynkron-vs-Jint loss:

```text
Baseline timestamp: 2026-05-27T15:44:12Z
Baseline signal: arrayops Asynkron focused timing = 1113 ms
arrayops  asynkron_ms=1113  jint_ms=598  Jint 1.86x faster
```

## Profile finding

The pre-change CPU profile command was:

```bash
rtk ./tools/profile arrayops --cpu --calltree-depth 40 --calltree-width 40
```

It was captured once before editing in the build worktree, then twice more
against a temporary `HEAD^` worktree after the implementation commit to complete
the requested three pre-change samples without altering the committed branch.
All three captures still showed reducer callback dispatch under the selected
workload:

```text
ArrayPrototype.Reduce
  StandardLibrary.ReduceLike
    TypedAstEvaluator.SyncFunctionInvoker.InvokeWithContext
      TypedAstEvaluator.SyncFunctionInvoker.InvokeWithContextSlow
```

That matched the investigated owner path: the dense numeric reduce callback
`(a, b) => a + b` still entered the general function invocation path even
though the invoker already had a cached simple parameter-binary plan shape.

## Change

`SyncFunctionInvoker` now exposes a narrow reducer-only numeric fast path for
simple two-argument arrow callbacks whose return expression is a parameter
binary expression. `Array.prototype.reduce` and `reduceRight` try it before the
existing two-argument callback invocation.

The fast path is retained only when both runtime values are already numbers and
the callback remains a simple non-async, non-generator arrow shape. String
coercion, ordinary functions, rest/default/destructured parameters, callbacks
that can observe full callback arguments, and unsupported values fall back to
the existing invocation path.

## Final signal

Repeated focused comparison after the change:

```text
Final timestamp: 2026-05-27T15:46:54Z
Final signal: arrayops Asynkron focused timing average = 790 ms
arrayops  asynkron_ms=692  jint_ms=430  Jint 1.61x faster
arrayops  asynkron_ms=709  jint_ms=620  Jint 1.14x faster
arrayops  asynkron_ms=969  jint_ms=942  Tie
Signal delta: -323 ms, 29.0% faster
```

The repeated final average cleared the requested 10% Asynkron-side improvement
threshold versus the captured baseline.

## Verification

Focused semantic verification:

```bash
rtk dotnet test tests/Asynkron.JsEngine.Tests -c Release --filter "FullyQualifiedName~FoundationTests.Array_Reduce|FullyQualifiedName~FoundationTests.Array_Map|FullyQualifiedName~FoundationTests.Array_Filter" -- xUnit.MaxParallelThreads=1 -timeout 20000
```

Result:

```text
ok dotnet test: 11 tests passed, 7 existing nullable warnings in 2 projects
```

The canonical internal quality gate remains `rtk make quality` and is delegated
to the orchestrator-run verification stage.
