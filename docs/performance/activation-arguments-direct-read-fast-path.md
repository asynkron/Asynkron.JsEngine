# Activation Arguments Direct Read Fast Path

Date: 2026-05-28

## Selected Profile

`activation-arguments-lite` remained the bounded optimizer slice from the
investigation handoff. The workload is strict code that repeatedly reads
`arguments.length` and direct numeric `arguments[i]` values:

```text
activation-arguments-lite          683      260  Jint 2.63x faster
activation-arguments-lite          688      277  Jint 2.48x faster
activation-arguments-lite          629      278  Jint 2.26x faster
```

Baseline timestamp: 2026-05-28T23:49:09Z
Baseline signal: activation-arguments-lite Asynkron focused median = 683 ms

## Change

`JsArgumentsObject.TryGetIndex` now returns the stored argument value directly
when no observable index descriptors have been materialized. This avoids the
index-name lookup and descriptor dictionary branch on the hot strict/unmapped
path.

The same slice also keeps an untouched `length` value available for direct
reads. Mutating, deleting, or redefining `length` disables that fast path and
falls back to the backing object, preserving observable descriptor semantics.
`JsOps.TryGetPropertyValueJsValue` can now consume that untouched length value
before it pays the generic property-name conversion and object lookup path.

The change is limited to arguments-object reads and does not alter recurrence
infrastructure, benchmark scripts, or loop-scope lowering.

## Final Signal

Repeated focused comparison rows after the change were:

```text
activation-arguments-lite          593      267  Jint 2.22x faster
activation-arguments-lite          702      270  Jint 2.60x faster
activation-arguments-lite          603      274  Jint 2.20x faster
```

The middle final row is recorded as benchmark noise; the median is the stable
signal for this short profile.

Final timestamp: 2026-05-28T23:52:49Z
Final signal: activation-arguments-lite Asynkron focused median = 603 ms
Signal delta: -80 ms, 11.7% faster

## Verification

Completed locally:

```bash
rtk ./benchmark.sh activation-arguments-lite
rtk ./benchmark.sh activation-arguments-lite
rtk ./benchmark.sh activation-arguments-lite
rtk dotnet test tests/Asynkron.JsEngine.Tests -c Release --filter "FullyQualifiedName~ActivationSemanticsProofPackTests" -- xUnit.MaxParallelThreads=1 -timeout 20000
rtk ./benchmark.sh activation-arguments-lite
rtk ./benchmark.sh activation-arguments-lite
rtk ./benchmark.sh activation-arguments-lite
```

The focused activation proof pack passed 48 tests in Release. The Release run
emitted existing nullable warnings from unrelated test files. The canonical
internal quality gate remains `rtk make quality` and is delegated to the
orchestrator-run verification stage.
