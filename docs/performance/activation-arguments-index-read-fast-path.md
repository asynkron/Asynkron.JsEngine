# Activation Arguments Index Read Fast Path

Date: 2026-05-27

## Selected Profile

`activation-arguments-lite` was selected from the required fresh
`rtk ./benchmark.sh` matrix because it was still one of the largest current
Asynkron-vs-Jint losses with a narrow activation and arguments-object owner
surface:

```text
activation-arguments-lite  asynkron_ms=1269  jint_ms=279  Jint 4.55x faster
```

Repeated focused baseline rows for the selected profile were noisy, but kept the
same loss shape:

```text
activation-arguments-lite  asynkron_ms=761   jint_ms=279   Jint 2.73x faster
activation-arguments-lite  asynkron_ms=1842  jint_ms=246   Jint 7.49x faster
activation-arguments-lite  asynkron_ms=783   jint_ms=1095  Asynkron 1.40x faster
```

## Profile Finding

The required CPU profile command was run three times:

```bash
rtk ./tools/profile activation-arguments-lite --cpu --calltree-depth 40 --calltree-width 40
```

The stable owner remained the called function activation path:

```text
InvokeWithContextSlow
  RunSync
    EnsureExecutionEnvironment
      CreateExecutionEnvironment
    ExecutePlan
      ExecuteInstructionLoop
        HandlePushEnvironment
        GetProgramComputedPropertyValue
          JsOps.TryGetPropertyValueJsValue
            JsArgumentsObject.TryGetProperty
```

The workload is strict code that reads `arguments[i]` in a loop. The arguments
object is observable and cannot be skipped, but numeric computed access was
still flowing through property-name conversion and the generic object property
path.

## Change

The bounded runtime slice adds a direct numeric-index path for arguments-object
reads:

- `JsOps.TryGetArrayLikeValueJsValue` now recognizes `JsArgumentsObject` when
  the property key is a numeric index.
- `JsArgumentsObject.TryGetIndex` reads mapped arguments from the activation
  binding and unmapped data descriptors directly, while preserving accessor
  descriptors and prototype fallback.
- `ExecutionPlanRunner.CreateExecutionEnvironment` now stores immutable
  body-lexical templates on `JsEnvironment` instead of allocating a new
  `HashSet<Symbol>` for every runner activation.

The slice does not alter recurrence infrastructure, benchmark scripts, or the
arguments-object materialization contract.

## Final Signal

After the change, repeated focused comparison runs were:

```text
activation-arguments-lite  asynkron_ms=745  jint_ms=276  Jint 2.70x faster
activation-arguments-lite  asynkron_ms=751  jint_ms=272  Jint 2.76x faster
activation-arguments-lite  asynkron_ms=744  jint_ms=270  Jint 2.76x faster
```

The final Asynkron average was 746.7 ms versus the repeated baseline average of
1128.7 ms, a 382.0 ms reduction, about 33.8% faster. The post-change samples
are also materially below the initial matrix row of 1269 ms.

Final allocation comparison:

```text
activation-arguments-lite  asynkron_ms=754  asynkron_kb=1019549.5  jint_ms=276  jint_kb=275794.2
```

## Verification

Completed locally:

```bash
rtk ./benchmark.sh
rtk ./benchmark.sh activation-arguments-lite
rtk ./benchmark.sh activation-arguments-lite
rtk ./benchmark.sh activation-arguments-lite
rtk ./tools/profile activation-arguments-lite --cpu --calltree-depth 40 --calltree-width 40
rtk ./tools/profile activation-arguments-lite --cpu --calltree-depth 40 --calltree-width 40
rtk ./tools/profile activation-arguments-lite --cpu --calltree-depth 40 --calltree-width 40
rtk dotnet build -c Release src/Asynkron.JsEngine/Asynkron.JsEngine.csproj
rtk dotnet test tests/Asynkron.JsEngine.Tests --filter "FullyQualifiedName~ActivationSemanticsProofPackTests" -- xUnit.MaxParallelThreads=1 -timeout 20000
rtk dotnet test tests/Asynkron.JsEngine.Tests -c Release --filter "FullyQualifiedName~ActivationSemanticsProofPackTests" -- xUnit.MaxParallelThreads=1 -timeout 20000
rtk ./benchmark.sh --allocations activation-arguments-lite
```

The focused activation proof pack passed 37 tests in Debug and Release. The
Release run emitted existing nullable warnings from unrelated test files. The
canonical internal quality gate remains `rtk make quality` and is delegated to
the orchestrator-run verification stage.
