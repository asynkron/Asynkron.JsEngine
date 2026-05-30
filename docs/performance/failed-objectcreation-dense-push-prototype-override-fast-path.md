# Failed ObjectCreation Dense-Push Prototype-Override Fast Path

Date: 2026-05-30

## Selected Profile

The required fresh `rtk ./benchmark.sh` matrix was run twice. `objectcreation`
was selected as a stable, well-shaped Asynkron-vs-Jint loss (object literal
construction plus `Array.prototype.push` in a tight loop):

```text
profile                    asynkron_ms  jint_ms  delta
objectcreation                   727      466  Jint 1.57x faster
objectcreation                   780      475  Jint 1.64x faster
objectcreation                   837      435  Jint 1.92x faster
objectcreation                   790      433  Jint 1.82x faster
objectcreation                   784      480  Jint 1.63x faster
```

The benchmark is allocation-heavy: Asynkron allocates roughly twice as much as
Jint for the same run.

```text
objectcreation   asynkron_ms=750  asynkron_kb=363853.1  jint_ms=469  jint_kb=181011.4  Jint 2.01x lower alloc
```

Baseline timestamp: 2026-05-30T05:30:00Z
Baseline signal: `objectcreation` Asynkron focused average = 784.4 ms (5 runs),
allocation = 363853.1 KB

## Profile Finding

`./tools/profile objectcreation` only wraps `simplearithmetic` in an IIFE, while
`benchmark.sh` wraps `objectcreation`. The unwrapped profile therefore attributes
loop cost to script-scope identifier resolution that the benchmark never pays.
The accurate hot path was captured by profiling the runner directly with the
same wrap the benchmark uses:

```bash
asynkron-profiler --cpu --calltree-depth 22 --calltree-width 14 \
  --root ExecuteInstructionLoop --filter Asynkron.JsEngine \
  -- dotnet tools/ProfileRunner/bin/Release/net10.0/ProfileRunner.dll \
     --wrap-iife objectcreation
```

Under the IIFE body the per-iteration cost split roughly as:

```text
EvaluateExpressionProgram (loop body)
  DefineObjectLiteralProperty            ~9-13%   (object literal -> properties)
    JsObjectState.ctor / Dictionary.ctor ~1-3%    (per-object state allocation)
    IsInternalKey / CanStore...          ~1-2%    (per-property name checks)
  ExecuteProgramCall -> TryInvokeArrayPushSingleFast ~6-8%
    JsArray.HasIndexedPrototypeOverride  ~3-4%    (per-push index.ToString + lookups)
    List<JsValue>.Grow / Array.Copy      ~2%      (objects array resize)
```

`HasIndexedPrototypeOverride` stood out as a clean, contained candidate: every
`objects.push({...})` converted the push index to a string and walked the
prototype chain calling `GetOwnPropertyDescriptor` on `Array.prototype` and
`Object.prototype`, even though neither owns any indexed property.

## Trial

`HasIndexedPrototypeOverride` was changed to skip the index-string materialization
and the per-index descriptor lookup whenever a prototype-chain entry provably
owns no indexed property. A cheap, allocation-free pre-check was added:

- `PrototypeMayOwnIndexedProperty(IJsPropertyAccessor)` returns false for a
  `JsObject` with no numeric descriptor keys and for a `JsArray` with no dense
  element, sparse element, or numeric descriptor key; unknown accessor types and
  proxies stayed conservative.
- The index string was materialized lazily, only when a prototype could actually
  own an indexed property.

This is behavior-preserving: inherited accessors and non-writable indexed
properties (the only cases that must defeat the fast store) are always recorded
as descriptors, so they keep `HasNumericDescriptorKeys()` true; inherited
*writable* indexed data properties are safe to skip because the fast dense store
creates an own shadowing property with identical observable semantics.

## Outcome

The change is correct but did not clear the 10% gate. A fair back-to-back A/B
(clean rebuild of each side, eight runs each) showed the improvement sat below
the benchmark noise floor:

```text
Baseline (clean):  786 730 727 732 729 739 720 707  -> steady-state avg ~726 ms
After    (clean):  704 753 717 733 776 716          -> avg ~733 ms
```

Final timestamp: 2026-05-30T06:00:00Z
Final signal: `objectcreation` Asynkron focused average = ~730 ms (no measurable
delta vs baseline), allocation = 357789.4 KB
Signal delta: timing ~0% (within noise); allocation -6063.7 KB, ~1.7% lower

`HasIndexedPrototypeOverride` is only ~3-4% of the measured loop, so even fully
removing it cannot reach 10% on this benchmark, and the saved `index.ToString()`
allocations are a small fraction of the run's 363 MB. The runtime edit was
reverted per the measurement/revert gate.

## Insight for Future Slices

`objectcreation` is allocation-bound, and its cost is spread across many small
items (per-object `JsObjectState` allocation, per-property define checks, the
push fast path, and the growing result array) with no single contained hot-path
cost large enough to yield a noise-resistant 10%. A real win here likely needs a
structural reduction in per-object allocation (e.g. shape/hidden-class style
property storage, or lazy-allocating the rarely-used `JsObjectState` collections
— `Descriptors`, `PrivateFields`, `PrivateBrands` — which are empty for plain
data objects), not another single hot-path micro-optimization. The
prototype-override pre-check remains a valid idea to fold into such a larger
allocation-focused effort, where its per-push string + lookup savings would
compound with other reductions.

Also recorded: profile `objectcreation` with the IIFE wrap (matching
`benchmark.sh`) — the default `./tools/profile objectcreation` is unwrapped and
mis-attributes loop cost to script-scope identifier resolution.

## Verification

Completed locally:

```bash
rtk ./benchmark.sh
rtk ./benchmark.sh --no-build objectcreation        # repeated baseline rows
rtk ./benchmark.sh --no-build --allocations objectcreation
asynkron-profiler --cpu --calltree-depth 22 --calltree-width 14 \
  --root ExecuteInstructionLoop --filter Asynkron.JsEngine \
  -- dotnet tools/ProfileRunner/bin/Release/net10.0/ProfileRunner.dll --wrap-iife objectcreation
rtk dotnet build -c Release src/Asynkron.JsEngine/Asynkron.JsEngine.csproj
git stash && rtk dotnet build -c Release src/Asynkron.JsEngine/Asynkron.JsEngine.csproj   # clean baseline A/B
git stash pop && rtk dotnet build -c Release src/Asynkron.JsEngine/Asynkron.JsEngine.csproj
git checkout -- src/Asynkron.JsEngine/JsTypes/JsArray.cs   # revert trial
```

The runtime change was fully reverted; only this writeup is retained. The
canonical internal quality gate remains `rtk make quality` and is delegated to
the orchestrator-run verification stage.
