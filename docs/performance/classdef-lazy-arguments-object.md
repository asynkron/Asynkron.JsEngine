# Class Definition Lazy Arguments Object

Date: 2026-05-25

## Selected Profile

`classdef` was selected from the required `rtk ./benchmark.sh` baseline because
it was the largest current Asynkron-vs-Jint loss in this run:

```text
classdef  asynkron_ms=1277  jint_ms=348  Jint 3.67x faster
```

A focused pre-change comparison also confirmed the profile remained a clear
loss, though it was intentionally treated as noisy because it ran while the CPU
profile was collecting:

```text
classdef  asynkron_ms=3955  jint_ms=1490  Jint 2.65x faster
```

## Profile Finding

The required CPU profile was:

```bash
rtk ./tools/profile classdef --cpu --calltree-depth 40 --calltree-width 40
```

The selected hot subtree was constructor and method invocation environment
setup under `ExecuteProgramConstruct`, `ExecuteProgramSuperConstruct`, and
`Array.prototype.map` callback calls. In the pre-change profile, repeated
`ExecutionPlanRunner.CreateExecutionEnvironment` calls materialized an
`arguments` object even for functions that did not reference `arguments` and
had no direct-eval or dynamic-scope path that could observe the binding.

That showed up as repeated:

```text
CreateExecutionEnvironment
  CreateArgumentsObject
    JsArgumentsObject.ctor
      JsObject.DefinePropertyDirect
```

## Change

`ExecutionPlanRunner.CreateExecutionEnvironment` already computed
`NeedsArgumentsBinding(_function)`, which is true when the function body,
parameter defaults, direct eval, or dynamic scope can observe `arguments`.

The IR runner now uses that existing analysis before creating the `arguments`
object. This keeps observable `arguments` semantics on the existing path, while
plain constructors, methods, and callbacks that never touch `arguments` avoid
the allocation and property-definition work entirely.

## Final Signal

After the change, repeated focused `classdef` comparison runs were:

```text
classdef  asynkron_ms=1105  jint_ms=296  Jint 3.73x faster
classdef  asynkron_ms=1103  jint_ms=300  Jint 3.68x faster
classdef  asynkron_ms=1027  jint_ms=294  Jint 3.49x faster
```

The post-change Asynkron timings averaged about 1078 ms. Compared with the
1277 ms full-table baseline signal, that is roughly a 16% Asynkron-side
improvement.

The follow-up CPU profile no longer showed `CreateArgumentsObject` as a
dominant constructor subtree. Remaining sampled cost was in construct dispatch,
parameter binding, property assignment, array map callback invocation, and
single-argument callback boxing.

## Verification

```bash
rtk dotnet test tests/Asynkron.JsEngine.Tests -c Release --filter "FullyQualifiedName~EvalFunctionTests|FullyQualifiedName~ActivationSemanticsProofPackTests|FullyQualifiedName~ClassSuperSemanticsTests" -- xUnit.MaxParallelThreads=1 -timeout 20000
rtk rg "EvaluateExpression\\(|ProfileEvaluateExpression\\(" src/Asynkron.JsEngine/Ast/TypedAstEvaluator.ExecutionPlanRunner*
rtk ./tools/profile classdef --cpu --calltree-depth 40 --calltree-width 40
```

Results:

- Focused internal tests passed: 79 tests.
- AST-eval seam scan returned no matches in the execution-plan runner files.
- Follow-up CPU profile completed and confirmed the selected `arguments` object
  subtree was no longer dominant.
- The canonical internal quality gate remains `rtk make quality` and is
  delegated to the orchestrator-run verification stage.
