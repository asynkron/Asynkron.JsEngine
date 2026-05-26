# Arrayops Dense Iteration Callback Args

## Selected benchmark

`arrayops` was selected from the required pre-edit `rtk ./benchmark.sh` table
because it was the largest array-focused Asynkron-vs-Jint loss in this run.

Baseline signal:

```text
arrayops  asynkron_ms=1480  jint_ms=454  Jint 3.26x faster
```

## Profile finding

The required CPU profile command was:

```bash
rtk ./tools/profile arrayops --cpu --calltree-depth 40 --calltree-width 40
```

The profile showed the selected slice under `Array.prototype.map`,
`Array.prototype.filter`, and `Array.prototype.reduce`. Callback invocation was
still dominant, but dense element lookup also remained visible because every
present array element went through string index conversion plus the generic
`HasProperty`/`TryGetProperty` path.

Relevant profile samples:

```text
ArrayPrototype.Map                         44.54 ms
ArrayPrototype.InvokeArrayIterationCallback 27.85 ms
ArrayPrototype.Filter                      30.67 ms
ArrayPrototype.Reduce / ReduceLike         19.80 ms
StandardLibrary.TryGetExistingElement      11.51 ms combined under map/reduce
```

## Change

The implementation keeps the fast paths narrow:

- `StandardLibrary.TryGetExistingElement(long)` now returns directly from a
  dense `JsArray` own element when the array has no custom indexed descriptors.
  Holes, prototype values, proxies, sparse/custom descriptors, and non-array
  receivers still fall back to the existing `HasProperty` path.
- `Array.prototype.reduce` now passes its four callback arguments through
  `FourValueArgs`, matching the existing allocation-free `SingleValueArgs`,
  `TwoValueArgs`, and `ThreeValueArgs` wrappers instead of allocating a
  temporary `JsValue[]` per callback.
- Regression coverage verifies that `reduce` still exposes all callback
  arguments and that `map` still reads an inherited prototype value for a hole.

## Final signal

Repeated focused comparison after the change:

```text
arrayops  asynkron_ms=912  jint_ms=374  Jint 2.44x faster
arrayops  asynkron_ms=982  jint_ms=369  Jint 2.66x faster
arrayops  asynkron_ms=916  jint_ms=390  Jint 2.35x faster
```

The repeated final Asynkron runs averaged about 937 ms versus the 1480 ms
baseline signal, about a 37% improvement. Each repeated final run stayed above
the requested 10% threshold despite normal local timing noise.

## Follow-up: reducer two-argument callbacks

Issue #2076 / PR #2088 selected `arrayops` again after the full reducer carrier
slice. The baseline allocation run was:

```text
arrayops  asynkron_ms=805  asynkron_kb=925094.5
```

The focused CPU profile still showed callback invocation under `Map`, `Filter`,
and `Reduce`. The memory profile showed repeated `HashSet<Symbol>` allocation
under array iteration callback eligibility.

The accepted follow-up kept the observable callback paths intact and narrowed
only the current owners:

- `SyncFunctionInvoker` caches the simple array-iteration and reducer
  callback-eligibility predicates once per invoker instead of rebuilding shape
  sets inside callback loops.
- `Array.prototype.reduce` / `reduceRight` use `TwoValueArgs` only for guarded
  simple typed arrow callbacks that cannot observe `index`, `array`, or
  callback-local `arguments`.
- Ordinary functions, rest/default/destructured parameters, async/generator
  callbacks, and callbacks with explicit index or array parameters stay on the
  full four-argument reducer path.

Final allocation signal:

```text
arrayops  asynkron_ms=1025  asynkron_kb=856317.6
memory profile total: 44.77 MB -> 41.40 MB
```

The timing sample was noisy and slower than the baseline run, but managed
allocation dropped by about 68.8 MB. The focused semantic proof was
`rtk dotnet test tests/Asynkron.JsEngine.Tests --filter "FullyQualifiedName~FoundationTests.Array_Reduce"`,
which passed 6 tests covering reducer values, prototype holes, accessors,
proxies, and observable callback arguments.

## Verification

Focused semantic verification:

```text
rtk dotnet test tests/Asynkron.JsEngine.Tests --filter "FullyQualifiedName~FoundationTests.Array_Reduce|FullyQualifiedName~FoundationTests.Array_Map|FullyQualifiedName~FoundationTests.Array_Filter" -- xUnit.MaxParallelThreads=1 -timeout 20000
ok dotnet test: 5 tests passed
```

The canonical internal quality gate remains `rtk make quality` and is delegated
to the orchestrator-run verification stage.
