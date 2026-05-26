# Activation Arguments Breakable Stack Pre-Sizing

Date: 2026-05-26

## Selected Profile

`activation-arguments-lite` was selected from the required fresh baseline because
it was the largest current Asynkron-vs-Jint loss in the default comparison
matrix:

```text
activation-arguments-lite  asynkron_ms=2801  jint_ms=529  Jint 5.29x faster
```

## Profile Finding

The required CPU profile was:

```bash
rtk ./tools/profile activation-arguments-lite --cpu --calltree-depth 40 --calltree-width 40
```

The hot subtree under `InvokeWithContextSlow` showed first-push growth for the
runtime breakable-frame stack on every `score(...)` invocation:

```text
ExecuteInstructionLoop
  HandleBreakableEnter
    Stack<BreakableFrame>.PushWithResize
      Stack<BreakableFrame>.Grow
```

The benchmark body contains a `for` loop inside the called function, so each
invocation creates breakable state and immediately pushes the loop frame. The
default `Stack<T>` starts at zero capacity, making that first push allocate and
copy even though the common loop/switch nesting depth is small.

## Change

`ExecutionPlanRunner.BreakableState` now creates its stack with capacity 4.
This keeps the semantics unchanged while removing the per-invocation first-grow
cost for common loop-heavy activation paths.

The slice is intentionally narrow: no recurrence infrastructure, benchmark
harness, argument-object behavior, lexical binding semantics, or unrelated
runtime surfaces were changed.

## Final Signal

After the change, repeated focused comparison runs were:

```text
activation-arguments-lite  asynkron_ms=1297  jint_ms=329  Jint 3.94x faster
activation-arguments-lite  asynkron_ms=910   jint_ms=295  Jint 3.08x faster
activation-arguments-lite  asynkron_ms=1381  jint_ms=437  Jint 3.16x faster
```

The Asynkron samples are materially faster than the 2801 ms baseline row, with
the slowest post-change sample still about 50.7% faster than baseline.

A follow-up CPU profile with the same command no longer showed
`HandleBreakableEnter` or `Stack<BreakableFrame>.PushWithResize` in the
`InvokeWithContextSlow` call tree. The remaining hot subtree is now dominated by
lexical slot setup and `HandlePushEnvironment`.

## Verification

Completed locally:

```bash
rtk dotnet build -c Release src/Asynkron.JsEngine/Asynkron.JsEngine.csproj
rtk ./benchmark.sh --no-build activation-arguments-lite
rtk ./benchmark.sh --no-build activation-arguments-lite
rtk ./benchmark.sh --no-build activation-arguments-lite
rtk ./tools/profile activation-arguments-lite --cpu --calltree-depth 40 --calltree-width 40
rtk dotnet test tests/Asynkron.JsEngine.Tests -c Release --filter "FullyQualifiedName~ActivationSemanticsProofPackTests|FullyQualifiedName~EvalFunctionTests|FullyQualifiedName~HoistingTests" -- xUnit.MaxParallelThreads=1 -timeout 20000
```

The focused test run passed 82 tests. The canonical internal quality gate
remains `rtk make quality` and is delegated to the orchestrator-run verification
stage.
