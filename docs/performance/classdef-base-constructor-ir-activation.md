# Class Definition Base Constructor IR Activation

Date: 2026-05-27

## Selected Profile

The required full benchmark baseline selected `classdef` as the bounded slice.
It was the largest current non-trivial Asynkron loss with a class-definition
owner surface:

```text
profile                    asynkron_ms  jint_ms  delta
classdef                          2390      419  Jint 5.70x faster
```

## Profile Finding

The required CPU profile was run three times:

```bash
rtk ./tools/profile classdef --cpu --calltree-depth 40 --calltree-width 40
```

All three sampled call trees kept constructor dispatch as the dominant selected
engine-owned subtree:

```text
ExecuteProgramConstructNoSpread
  ReflectHelper.Construct
    SyncFunctionInvoker.InvokeWithContext
      SyncFunctionInvoker.InvokeWithContextSlow
        ExecutionPlanRunner.RunSync
        CastHelpers.Box
```

For the selected workload, every `new Dog(...)` enters the derived constructor
and then runs `super(name)`. The derived constructor must keep the existing
path because `super()` owns `this` initialization, but the base constructor is a
simple class constructor with no fields, private scope, arguments binding, or
dynamic lookup dependency.

## Change

`SyncFunctionInvoker` now has a narrow base-class-constructor IR activation
fast path. It is only enabled when the constructor is a non-derived class
constructor with:

- a defined `new.target`
- simple identifier parameters
- no parameter expressions or `arguments` binding
- no home object, captured private scopes, super binding, or dynamic lookup
- an activation-slot shape that matches the existing simple IR activation
  contract

The fast path builds the same function/body environment pair used by the
existing simple IR activation path, but also defines constructor-specific
bindings for `this`, `new.target`, and the active function. Derived
constructors and broader class shapes remain on the previous full path.

The run also fixed `tools/profile` so an empty `runner_args` array does not
trip `set -u` on the local Bash version. That repair was necessary to execute
the issue's required profiling command.

## Final Signal

Repeated selected-profile timings after the change were:

```text
classdef                         798      268  Jint 2.98x faster
classdef                         687      261  Jint 2.63x faster
classdef                         727      258  Jint 2.82x faster
```

The post-change Asynkron timings averaged about 737 ms. Compared with the
2390 ms full-table baseline, that is roughly a 69% Asynkron-side improvement.
The repeated measurements stayed well beyond the requested 10% threshold.

## Verification

```bash
rtk dotnet build src/Asynkron.JsEngine/Asynkron.JsEngine.csproj -c Release -v minimal
rtk dotnet test tests/Asynkron.JsEngine.Tests -c Release --filter "FullyQualifiedName~ClassStatementTests|FullyQualifiedName~ClassSuperSemanticsTests|FullyQualifiedName~ClassElementEvalTests" -- xUnit.MaxParallelThreads=1 -timeout 20000
rtk ./tools/profile classdef --cpu --calltree-depth 40 --calltree-width 40
rtk ./benchmark.sh --no-build classdef
```

Results:

- Release library build passed.
- Focused class constructor, class element, and super semantics tests passed:
  43 tests.
- The profile wrapper now runs successfully for the selected profile.
- Repeated focused `classdef` timings showed the required speedup.
- The canonical internal quality gate remains `rtk make quality` and is
  delegated to the orchestrator-run verification stage.
