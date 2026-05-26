# Class Definition Home Object Simple IR Activation

Date: 2026-05-26

## Selected Profile

The required full benchmark baseline selected `classdef` as the bounded slice.
It remained a large top-loss profile and matched the investigation handoff's
class-definition owner surface:

```text
profile                    asynkron_ms  jint_ms  delta
classdef                          1807      682  Jint 2.65x faster
```

## Profile Finding

The required CPU profile was:

```bash
rtk ./tools/profile classdef --cpu --calltree-depth 40 --calltree-width 40
```

The sampled call tree still showed constructor and `super()` dispatch as the
largest cost, but the `dogs.map(d => d.speak())` tail also repeatedly entered
typed function invocation for a plain class method. The existing simple IR
activation path was disabled whenever a function had a home object. That is
required for methods whose return expression can execute `super`, but plain
methods such as `speak()` only need their receiver and parameters.

## Change

`SyncFunctionInvoker` now allows the existing simple IR activation path for
home-object methods only when the plan is a simple return program and that
program contains no `super` operations. Methods that need `super`, private
scope state, explicit super bindings, or other existing guardrails stay on the
previous full invocation path.

This keeps the semantic guard tied to the lowered expression program instead
of adding a method-name or benchmark-specific shortcut.

## Final Signal

Repeated selected-profile timings after the change were:

```text
classdef                         873      286  Jint 3.05x faster
classdef                         766      259  Jint 2.96x faster
classdef                         723      254  Jint 2.85x faster
classdef                         707      259  Jint 2.73x faster
```

The post-change Asynkron timings averaged about 767 ms. Compared with the
1807 ms full-table baseline, that is roughly a 58% Asynkron-side improvement.
The repeated Asynkron timings stayed well past the requested 10% threshold.
A temporary A/B check with the home-object guard restored measured 908 ms on
the same focused runner, so the final guard remained enabled.

## Verification

```bash
rtk dotnet build src/Asynkron.JsEngine/Asynkron.JsEngine.csproj -c Release -v q --nologo
rtk dotnet test tests/Asynkron.JsEngine.Tests -c Release --filter "FullyQualifiedName~ClassSuperSemanticsTests|FullyQualifiedName~ClassStatementTests|FullyQualifiedName~ArrayIterationCallbacks" -- xUnit.MaxParallelThreads=1 -timeout 20000
rtk rg "EvaluateExpression\\(|ProfileEvaluateExpression\\(" src/Asynkron.JsEngine/Ast/TypedAstEvaluator.ExecutionPlanRunner*
rtk ./tools/profile classdef --cpu --calltree-depth 40 --calltree-width 40
rtk ./benchmark.sh classdef
rtk ./benchmark.sh --no-build classdef
```

Results:

- Release library build passed.
- Focused class and array-callback tests passed: 37 tests.
- AST-eval seam scan returned no matches in the execution-plan runner files.
- Follow-up CPU profile completed and kept the remaining dominant cost in
  constructor and `super()` dispatch rather than widening this slice.
- The canonical internal quality gate remains `rtk make quality` and is
  delegated to the orchestrator-run verification stage.

## Follow-through (Issue #2183)

Date: 2026-05-26

### Narrow change

`ExecutionPlanRunner.ExecuteProgramSuperConstruct` now treats the
`Symbol.ThisInitialized` double-super guard as a boolean fast path when the
binding is already a boolean `JsValue`:

- Boolean values use `AsBoolean()` directly.
- Non-boolean values preserve the existing `JsOps.ToBoolean(...)` behavior.

This keeps semantics unchanged while trimming one coercion step from the
`super()` constructor path.

### Evidence

Baseline and final signals were both captured with:

```bash
rtk ./tools/profile classdef --cpu --calltree-depth 40 --calltree-width 40
```

Both runs continued to show constructor and `super()` dispatch as dominant
sampled work (`ExecuteProgramSuperConstruct` -> `ExecuteProgramConstructNoSpread`
-> `ReflectHelper.Construct`). This slice is intentionally bounded and does not
claim broad classdef parity or end-to-end constructor wins.
