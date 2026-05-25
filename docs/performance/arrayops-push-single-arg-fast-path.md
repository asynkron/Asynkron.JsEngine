# Arrayops Push Single-Arg Fast Path

## Selected benchmark

`arrayops` was selected from the required pre-edit `rtk ./benchmark.sh` table
because it was the largest current Asynkron-vs-Jint loss in this run.

Baseline signal:

```text
arrayops  asynkron_ms=6295  jint_ms=1319  Jint 4.77x faster
```

## Profile finding

The required CPU profile command was:

```bash
rtk ./tools/profile arrayops --cpu --calltree-depth 40 --calltree-width 40
```

The profile showed the remaining hot path under the initial `arr.push(i)` loop,
after the earlier dense-array length-storage optimization had already made
`map` and `filter` element writes cheap. The selected call tree still spent a
large slice of `ExecuteInstructionLoop` in single-argument host invocation:

```text
ExecuteInstructionLoop                                  405.73 ms
HandleEvaluateAndDiscard / ExecuteProgramCall           120.28 ms
InvokeCallableSingleArg / InvokeCallableJsValueGeneric  120.28 ms
CastHelpers.Box under single-arg push path              117.25 ms
```

The issue was not array storage itself this time. `arr.push(i)` resolved to the
generated native `Array.prototype.push` host function, but the generic single
argument invocation path still had to pass `SingleValueArgs` through an
`IReadOnlyList<JsValue>` host-function boundary, boxing the struct before the
push implementation could run.

## Change

The implementation keeps the shortcut at the call/runtime boundary:

- `ExecutionPlanRunner` recognizes generated native `push` host functions for
  one-argument calls with an explicit `this` receiver.
- `JsArray.TryPushSingleFast` appends directly only for plain arrays where Set
  semantics cannot observe indexed descriptors, prototype index overrides or
  proxies, non-extensibility, or non-writable length.
- All other receivers and modified arrays fall back to the existing
  `Array.prototype.push` implementation.

This avoids the single-argument host-call boxing in the benchmark while keeping
observable JavaScript edge cases on the existing slow path.

## Final signal

Repeated focused comparison after the change:

```text
arrayops  asynkron_ms=3059  jint_ms=1036  Jint 2.95x faster
arrayops  asynkron_ms=2673  jint_ms=880   Jint 3.04x faster
arrayops  asynkron_ms=1892  jint_ms=652   Jint 2.90x faster
```

The repeated final Asynkron runs averaged about 2541 ms versus the 6295 ms
baseline signal, about a 60% improvement. Every repeated final run stayed above
the requested 10% threshold despite normal local timing noise.

## Verification

Focused semantic verification:

```text
rtk dotnet test tests/Asynkron.JsEngine.Tests --filter "FullyQualifiedName~ArrayBuiltinsSpecTests"
ok dotnet test: 27 tests passed
```

The canonical internal quality gate remains `rtk make quality` and is delegated
to the orchestrator-run verification stage.
