# Activation Arguments Generator State Gating

Date: 2026-05-26

## Selected Profile

`activation-arguments-lite` was selected from the required fresh baseline because
it remained one of the largest Asynkron-vs-Jint losses in the default comparison
matrix after earlier activation wins:

```text
activation-arguments-lite  asynkron_ms=1377  jint_ms=274  Jint 5.03x faster
```

## Profile Finding

The required CPU profile was:

```bash
rtk ./tools/profile activation-arguments-lite --cpu --calltree-depth 40 --calltree-width 40
```

The profile still pointed at activation setup under `InvokeWithContextSlow`:

```text
CreateExecutionEnvironment
  CastHelpers.Box
  JsEnvironment.ResetSlotLayoutForPlan
    JsEnvironment.MarkSlotsLexicalUninitialized
      ImmutableHashSet<T>.IEnumerable<T>.GetEnumerator
ExecuteInstructionLoop
  HandlePushEnvironment
    Buffer.BulkMoveWithWriteBarrier
```

The workload is a strict ordinary function that reads `arguments` in a `for`
loop. It cannot skip the arguments object, but it also does not execute as a
generator, so generator-only activation state was unnecessary for this path.

## Change

The bounded runtime slice keeps activation semantics unchanged while removing
two setup costs from ordinary function calls:

- `ActivationSlotShape` now carries precomputed lexical slot indices for root
  activation slots, letting `ResetSlotLayoutForPlan` mark TDZ slots directly by
  index instead of enumerating root lexical symbol sets during each invocation.
- `ExecutionPlanRunner.CreateExecutionEnvironment` now creates and stores
  `YieldResumeContext` / generator back-reference bindings only for generator
  runners. Ordinary sync functions no longer allocate or define those internal
  generator-only bindings, and their function environment capacity excludes
  those two slots.

The slice does not change recurrence infrastructure, benchmark harness behavior,
argument-object behavior, or unrelated profiles.

## Final Signal

After the change, repeated focused comparison runs were:

```text
activation-arguments-lite  asynkron_ms=1126  jint_ms=350  Jint 3.22x faster
activation-arguments-lite  asynkron_ms=888   jint_ms=377  Jint 2.36x faster
```

The slower post-change sample is about 18.2% faster than the 1377 ms baseline,
and the second sample is about 35.5% faster, clearing the requested 10% threshold
despite local benchmark noise.

The follow-up CPU profile with the same command no longer showed
`CreateExecutionEnvironment`, `CastHelpers.Box`, or
`MarkSlotsLexicalUninitialized` in the `InvokeWithContextSlow` call tree. The
remaining activation hotspot is now loop-scope setup:

```text
InvokeWithContextSlow
  RunSync
    ExecutePlan
      ExecuteInstructionLoop
        HandlePushEnvironment
          Buffer.BulkMoveWithWriteBarrier
```

## Verification

Completed locally:

```bash
rtk dotnet build -c Release src/Asynkron.JsEngine/Asynkron.JsEngine.csproj
rtk ./benchmark.sh --no-build activation-arguments-lite
rtk ./benchmark.sh --no-build activation-arguments-lite
rtk ./tools/profile activation-arguments-lite --cpu --calltree-depth 40 --calltree-width 40
rtk dotnet test tests/Asynkron.JsEngine.Tests -c Release --filter "FullyQualifiedName~ActivationSemanticsProofPackTests|FullyQualifiedName~AsyncGeneratorActivation_PreservesCapturedParameterAcrossAwaitAndYield|FullyQualifiedName~Generator_YieldsMultipleValues|FullyQualifiedName~Generator_NextValueIsDeliveredToYield|FullyQualifiedName~Generator_ForOfYieldsValuesIr|FullyQualifiedName~GeneratorYieldSendTests" -- xUnit.MaxParallelThreads=1 -timeout 20000
```

The focused test run passed 51 tests. It emitted existing nullable warnings in
test files, with no source warnings from the runtime change.

The canonical internal quality gate remains `rtk make quality` and is delegated
to the orchestrator-run verification stage.
