# Failed Activation Arguments Loop-Scope Template Trial

Date: 2026-05-27

## Selected Profile

`activation-arguments-lite` was selected from the required fresh
`rtk ./benchmark.sh` baseline. It remains a meaningful Asynkron-vs-Jint loss,
but the current gap is much smaller after earlier activation and arguments-object
work:

```text
activation-arguments-lite  asynkron_ms=770  jint_ms=267  Jint 2.88x faster
```

Repeated focused baseline rows were:

```text
activation-arguments-lite  asynkron_ms=814  jint_ms=254  Jint 3.20x faster
activation-arguments-lite  asynkron_ms=884  jint_ms=325  Jint 2.72x faster
activation-arguments-lite  asynkron_ms=779  jint_ms=282  Jint 2.76x faster
```

Baseline timestamp: 2026-05-27T14:42:15Z
Baseline signal: activation-arguments-lite Asynkron focused average = 825.7 ms

## Profile Finding

The required CPU profile command was run three times before editing:

```bash
rtk ./tools/profile activation-arguments-lite --cpu --calltree-depth 40 --calltree-width 40
```

All three captures kept `HandlePushEnvironment` as the stable owner under the
`InvokeWithContextSlow` root. The sampled call trees repeatedly attributed most
of that subtree to `Buffer.BulkMoveWithWriteBarrier`, consistent with
per-invocation loop-scope slot clearing before the existing `SlotNames` fast
path stamps the lexical slot names:

```text
HandlePushEnvironment
  Buffer.BulkMoveWithWriteBarrier
```

The workload is strict code that reads `arguments[i]` inside a `for (let i = ...)`
loop. It does not contain closures or dynamic scope, so the attempted slice
targeted only the loop-scope slot setup path rather than arguments-object
materialization or observable lexical-capture semantics.

## Trial

The trial added an ordered slot-template initializer for `PushEnvironment`
instructions whose emit-time `SlotNames` covered every logical slot. The intent
was to overwrite all live slots directly and avoid the clear-then-stamp sequence
for safe loop scopes. A second variant also let pooled environments preserve the
backing slot array when that complete template was available.

Both variants built cleanly, but neither crossed the required 10% improvement
threshold. The best retained-looking focused rows after the second variant were:

```text
activation-arguments-lite  asynkron_ms=789  jint_ms=271  Jint 2.91x faster
activation-arguments-lite  asynkron_ms=771  jint_ms=279  Jint 2.76x faster
activation-arguments-lite  asynkron_ms=766  jint_ms=256  Jint 2.99x faster
```

Final timestamp: 2026-05-27T14:48:41Z
Final signal: activation-arguments-lite Asynkron trial average = 775.3 ms
Signal delta: -50.4 ms, 6.1% faster, below the 10% acceptance threshold

## Outcome

The runtime edits were reverted because the measured improvement did not clear
the performance gate. The failed result is still useful: `HandlePushEnvironment`
is real in the profile, but slot-template clearing alone is not large enough now
that earlier activation slices have reduced the benchmark to the 750-900 ms
range.

A future successful slice likely needs to remove the non-capturing loop
environment itself from this hot function path, or make the loop variable live
in an existing flat slot when closure, direct eval, with, generator resume, and
TDZ constraints prove that a per-iteration environment object is unobservable.
That is a larger lowering/runner contract change than this bounded child run
should retain without a dedicated semantic proof pack.

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
rtk dotnet test tests/Asynkron.JsEngine.Tests -c Release --filter "FullyQualifiedName~ActivationSemanticsProofPackTests" -- xUnit.MaxParallelThreads=1 -timeout 20000
rtk git diff --check
```

The canonical internal quality gate remains `rtk make quality` and is delegated
to the orchestrator-run verification stage.
