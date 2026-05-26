# Failed Class Definition Runner Argument Container Attempt

Date: 2026-05-26

## Selected Profile

The required full baseline was captured with:

```bash
rtk ./benchmark.sh
```

`classdef` was kept as the selected profile from the investigation handoff
because it remained a top non-overlapping loss in the fresh table:

```text
profile                    asynkron_ms  jint_ms  delta
classdef                          1137      336  Jint 3.38x faster
```

Other losses in the same table included `regex` at 2308 ms vs 1237 ms,
`activation-arguments-lite` at 1058 ms vs 332 ms, `stringops` at 713 ms vs
229 ms, and `propertyaccess` at 1344 ms vs 570 ms. `classdef` was still a
large current loss and matched the bounded owner surface requested for this
run.

## Profile Finding

The required CPU profile was:

```bash
rtk ./tools/profile classdef --cpu --calltree-depth 40 --calltree-width 40
```

The pre-change call tree showed constructor and `super(...)` invocation under
`ExecuteProgramConstructNoSpread` as the largest selected engine-owned subtree.
Inside `SyncFunctionInvoker.InvokeWithContextSlow`, sampled cost included
repeated `CastHelpers.Box` while small struct argument lists flowed into the
IR runner:

```text
ExecuteProgramConstructNoSpread
  ReflectHelper.Construct
    SyncFunctionInvoker.InvokeWithContext
      SyncFunctionInvoker.InvokeWithContextSlow
        CastHelpers.Box
```

That looked like a narrow follow-up to the recent classdef argument-shape work:
keep `SingleValueArgs` and `TwoValueArgs` unboxed until parameter binding in
`ExecutionPlanRunner`.

## Attempted Change

The attempted change replaced the runner's stored `IReadOnlyList<JsValue>`
argument field with a small value container that copied up to four arguments
into fields and only allocated an overflow array for larger argument lists.
`CreateArgumentsObject` and `BindFunctionParameters` were made generic during
the attempt so the runner could consume that value container without boxing.

The code built successfully:

```bash
rtk dotnet build src/Asynkron.JsEngine/Asynkron.JsEngine.csproj -c Release -v q --nologo
```

Result:

```text
ok dotnet build: 2 projects, 0 errors, 0 warnings
```

## Final Signal

The attempted change did remove the selected `CastHelpers.Box` subtree from the
follow-up CPU profile, but it did not produce a valid benchmark win. Repeated
focused timings regressed instead:

```text
rtk ./benchmark.sh classdef
classdef                       1939      608  Jint 3.19x faster

rtk ./benchmark.sh --no-build classdef
classdef                       2143      552  Jint 3.88x faster
```

Because the result failed the required 10% Asynkron-side improvement threshold,
the runtime code change was reverted. After reverting, focused `classdef`
timings were still noisy and did not establish a new win:

```text
rtk ./benchmark.sh classdef
classdef                       1542      486  Jint 3.17x faster

rtk ./benchmark.sh --no-build classdef
classdef                       1583      358  Jint 4.42x faster
```

## Verification

```bash
rtk dotnet build src/Asynkron.JsEngine/Asynkron.JsEngine.csproj -c Release -v q --nologo
rtk ./tools/profile classdef --cpu --calltree-depth 40 --calltree-width 40
rtk ./benchmark.sh classdef
rtk ./benchmark.sh --no-build classdef
```

Results:

- Release project build passed during the attempted runtime change.
- The attempted change removed the sampled boxing subtree but regressed
  selected-profile timings.
- All attempted runtime code was reverted.
- This run is evidence-only: no >=10% classdef win survived repeated
  measurement.
- The canonical internal quality gate remains `rtk make quality` and is
  delegated to the orchestrator-run verification stage.
