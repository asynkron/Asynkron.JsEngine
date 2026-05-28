# Activation Arguments Lazy Index Descriptors

Date: 2026-05-28

## Selected Profile

`activation-arguments-lite` remained the bounded optimizer slice from the fresh
pre-edit comparison matrix:

```text
activation-arguments-lite          702      237  Jint 2.96x faster
```

Repeated focused baseline rows were:

```text
activation-arguments-lite          699      271  Jint 2.58x faster
activation-arguments-lite          858      303  Jint 2.83x faster
activation-arguments-lite          709      436  Jint 1.63x faster
```

Baseline timestamp: 2026-05-28T12:54:33Z
Baseline signal: activation-arguments-lite Asynkron focused average = 755.3 ms

## Profile Finding

The required CPU profile command was run three times before editing:

```bash
rtk ./tools/profile activation-arguments-lite --cpu --calltree-depth 40 --calltree-width 40
```

All three captures kept `JsArgumentsObject.ctor` visible under
`CreateArgumentsObject`, with eager index descriptor setup showing up as
`JsObject.DefinePropertyDirect`, `JsObject.DefinePropertyInternalDirect`,
`JsObject.AssignDescriptorStorage`, and descriptor tracking work. The hot direct
`arguments[i]` read path also still paid a tracked-descriptor dictionary lookup
inside `JsArgumentsObject.TryGetIndex`.

## Change

`JsArgumentsObject` now keeps initial numeric arguments properties virtual until
an observable slow API needs them:

- direct numeric `arguments[i]` reads return mapped parameter values or stored
  argument values without pre-created index descriptors or index-name arrays;
- descriptor, enumeration, delete, defineProperty, assignment, and
  extensibility APIs synthesize or materialize the affected index properties
  before delegating to the ordinary backing object;
- mapped sloppy arguments still update from parameter binding observers, while
  accessor/non-writable descriptor changes still unmap the parameter slot.

The change is limited to arguments-object storage and focused activation proof
coverage. It does not change recurrence infrastructure, benchmark scripts, or
the existing `JsOps` numeric read dispatch shape.

## Final Signal

Repeated focused comparison rows after the change were:

```text
activation-arguments-lite          626      278  Jint 2.25x faster
activation-arguments-lite          620      272  Jint 2.28x faster
activation-arguments-lite          653      287  Jint 2.28x faster
```

Final timestamp: 2026-05-28T13:03:51Z
Final signal: activation-arguments-lite Asynkron focused average = 633.0 ms
Signal delta: -122.3 ms, 16.2% faster

Final allocation comparison:

```text
activation-arguments-lite          610       776971.2      265    275795.6  Jint 2.30x faster      Jint 2.82x lower alloc
```

This is lower than the 2026-05-28 checked-in allocation evidence row of
`asynkron_kb=969158.7` for the same profile.

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
rtk ./benchmark.sh activation-arguments-lite
rtk ./benchmark.sh activation-arguments-lite
rtk ./benchmark.sh activation-arguments-lite
rtk ./benchmark.sh --allocations activation-arguments-lite
```

The focused activation proof pack passed 44 tests in Release. The Release run
emitted existing nullable warnings from unrelated test files. The canonical
internal quality gate remains `rtk make quality` and is delegated to the
orchestrator-run verification stage.
