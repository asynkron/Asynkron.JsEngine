# Arrayops Dense Array Length Storage

## Selected benchmark

`arrayops` was selected from the required `rtk ./benchmark.sh` baseline because
it was one of the largest current Asynkron-vs-Jint losses and it was not covered
by the existing same-day `classdef` optimization note.

Baseline signal:

```text
arrayops  asynkron_ms=1379  jint_ms=361  Jint 3.82x faster
```

## Profile finding

The required CPU profile command was:

```bash
rtk ./tools/profile arrayops --cpu --calltree-depth 40 --calltree-width 40
```

The hot path was dense array element creation and length bookkeeping inside
`Array.prototype.map`, `Array.prototype.filter`, and `Array.prototype.push`.
The profile showed `CreateDataPropertyOrThrowJsValue` routing ordinary fresh
`JsArray` writes through descriptor/property-definition machinery, while push
and filter length growth repeatedly updated the backing `length` storage through
the object indexer and boxed numeric values.

Relevant baseline samples:

```text
ExecuteInstructionLoop                                    173.59 ms
ArrayPrototype.Map                                         53.07 ms
ArrayPrototype.Filter                                      38.13 ms
ArrayPrototype.Push                                        21.81 ms
StandardLibrary.CreateDataPropertyOrThrowJsValue           28.17 ms
CastHelpers.Box under push length/index writes             21.57 ms
```

## Change

The implementation keeps the optimization at the dense-array storage layer:

- `JsArray.TryCreateDataPropertyFast` creates ordinary dense data properties
  without descriptor allocation when the array is extensible, has no numeric
  descriptors, and length growth is writable.
- `Array.prototype.map` and `Array.prototype.filter` use a numeric-index
  `CreateDataPropertyOrThrowJsValue` overload so fresh default arrays avoid
  index string allocation and descriptor setup in the common case.
- `JsArray.UpdateLengthProperty` writes the cached length slot with `JsValue`
  directly, avoiding boxed numeric writes during repeated length growth.

The fallback path remains the existing descriptor-based implementation for
non-extensible arrays, custom numeric descriptors, non-writable length, proxies,
and non-`JsArray` species results.

## Final signal

Repeated focused comparison after the change:

```text
arrayops  asynkron_ms=906  jint_ms=447  Jint 2.03x faster
arrayops  asynkron_ms=953  jint_ms=434  Jint 2.20x faster
arrayops  asynkron_ms=902  jint_ms=415  Jint 2.17x faster
```

The final Asynkron average was about 920 ms versus the 1379 ms baseline signal,
roughly a 33% improvement. The repeated final runs stayed comfortably above the
requested 10% threshold despite normal local timing noise.

## Verification

Focused semantic verification:

```text
rtk dotnet test tests/Asynkron.JsEngine.Tests --filter "FullyQualifiedName~ArrayBuiltinsSpecTests"
ok dotnet test: 26 tests passed
```

The canonical internal quality gate remains `rtk make quality` and is delegated
to the orchestrator-run verification stage.
