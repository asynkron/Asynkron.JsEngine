# Map/Set IR Plain Method Fast Path

Date: 2026-06-01

## Slice

The `mapset` profile was selected from the recurrence optimizer run because the current benchmark table still showed it as a clear Jint loss:

```text
mapset  asynkron_ms=2227  jint_ms=787  delta=Jint 2.83x faster
```

Three focused CPU profiles with:

```bash
rtk ./tools/profile mapset --cpu --calltree-depth 40 --calltree-width 40
```

showed the hot route under `ExecutionPlanRunner.ExecuteProgramCall`, with repeated `InvokeCallableJsValueGeneric` frames and `CastHelpers.Box` dominating calls into `Map.prototype.set`, `Map.prototype.has`, `Set.prototype.add`, and `Set.prototype.has`.

## Change

`ExecutionPlanRunner.ExecuteProgramCall` now recognizes plain `JsMap` and `JsSet` receiver calls for the native `set`/`get`/`has`/`delete`/`clear` and `add`/`has`/`delete`/`clear` methods when there is no spread argument materialization. The fast path calls the owner storage methods directly and returns the same observable values as the native prototype methods.

The guard intentionally stays narrow:

- receiver must still be a plain `JsMap` or `JsSet`
- callable must be the engine-created native host function stamped with the
  matching Map/Set fast-method identity
- spread calls stay on the normal materialized argument path
- JavaScript method/prototype overrides use the existing callable fallback
- cross-prototype method swaps, such as assigning `Set.prototype.has` to
  `Map.prototype.has`, fall back to ordinary built-in receiver validation

Focused tests cover SameValueZero behavior and prototype override fallback for both Map and Set.

## Result

Repeated post-change selected-profile timings:

```text
mapset  asynkron_ms=931  jint_ms=741  delta=Jint 1.26x faster
mapset  asynkron_ms=921  jint_ms=759  delta=Jint 1.21x faster
mapset  asynkron_ms=886  jint_ms=703  delta=Jint 1.26x faster
```

The median Asynkron time moved from the baseline `2227 ms` to `921 ms`, a `58.6%` reduction. A follow-up CPU profile showed the Map/Set calls under `TryInvokePlainMapSetFast` instead of the previous generic host-call boxing path.

Follow-up build-stage regression coverage tightened the callable guard from
display-name matching to method-identity markers after cross-family method swaps
exposed the broader name-based guard. See
`docs/adrs/0319-keep-mapset-fast-dispatch-method-identity-marked.md`.

## Residual Follow-Up: Issue #2954

Timestamp: 2026-06-01T05:42:25Z

Issue #2954 re-profiled the residual gap after the method-identity-marked fast
path. The fresh selected-profile baseline was:

```text
rtk ./benchmark.sh mapset
mapset  asynkron_ms=1055  jint_ms=796  delta=Jint 1.33x faster
```

The focused CPU profile:

```bash
rtk ./tools/profile mapset --cpu --calltree-depth 40 --calltree-width 40
```

still showed the owned route under `TryInvokePlainMapSetFast`, but the largest
direct child in that route was `JsMap.Set` storage work:

```text
ExecuteInstructionLoop
`- EvaluateExpressionProgram
   `- ExecuteProgramCall
      `- TryInvokePlainMapSetFast
         `- JsMap.Set
            |- Dictionary<JsValue,__Canon>.set_Item
            |- List<__Canon>.AddWithResize
            `- Dictionary<JsValue,JsValue>.TryGetValue
```

Two bounded runtime candidates were tried and reverted because selected-profile
timing did not support retaining them:

```text
JsMap.Set CollectionsMarshal storage probe:
mapset  asynkron_ms=1378  jint_ms=839  delta=Jint 1.64x faster
mapset  asynkron_ms=2442  jint_ms=1134  delta=Jint 2.15x faster

MapSetFastMethodKind-first dispatch:
mapset  asynkron_ms=1461  jint_ms=925  delta=Jint 1.58x faster
```

`rtk dotnet test tests/Asynkron.JsEngine.Tests --filter
"FullyQualifiedName~MapTests|FullyQualifiedName~SetTests"` passed for the
attempted runtime slices, but no runtime change was retained. Future follow-up
should start from a new focused profile and treat `JsMap.Set` storage, string
key construction, and remaining expression-program overhead as separate owners
instead of retrying these two micro-slices without fresh evidence.
