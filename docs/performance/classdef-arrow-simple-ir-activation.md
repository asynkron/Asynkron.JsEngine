# Class Definition Arrow Simple IR Activation

Date: 2026-05-26

## Selected Profile

The required baseline was captured with:

```bash
rtk ./benchmark.sh
```

`classdef` was selected as the bounded slice because it was a clear
Asynkron-vs-Jint loss in the default table and the CPU profile pointed at a
small callback invocation path:

```text
classdef                       1414      301  Jint 4.70x faster
```

## Profile Finding

The required CPU profile was:

```bash
rtk ./tools/profile classdef --cpu --calltree-depth 40 --calltree-width 40
```

The pre-change profile showed the final `dogs.map(d => d.speak())` callback
entering `SyncFunctionInvoker.InvokeWithContextSlow` and then falling through to
the full `ExecutionPlanRunner` path. That arrow callback is a simple one
parameter, simple return expression. Previous slices already reduced callback
argument shape to one argument, but the arrow still could not use the simple IR
activation fast path because all arrow functions were rejected by the guard.

## Change

`SyncFunctionInvoker.CanUseSimpleIrActivationFastPath` now permits simple arrow
functions only when their simple return expression does not depend on lexical
`this`, lexical `new.target`, or `super` operations. Those dependencies still
take the existing full invocation path.

The change keeps the existing activation-slot and plan-shape checks. It only
removes the unconditional arrow rejection for the subset whose expression
program can be evaluated without arrow-specific lexical binding semantics.

## Final Signal

After the change, repeated focused `classdef` comparison runs were:

```text
classdef                        904      296  Jint 3.05x faster
classdef                        947      282  Jint 3.36x faster
classdef                        842      285  Jint 2.95x faster
```

The post-change Asynkron timings averaged about 898 ms. Compared with the
1414 ms baseline, that is about a 36% Asynkron-side improvement.

The follow-up CPU profile showed the array callback subtree taking the simple
IR activation path through `TryInvokeIrFast`,
`CreateSimpleIrActivationEnvironment`, and
`EvaluateStandaloneExpressionProgram`; the remaining sampled cost is mostly in
constructors, `super(...)`, method calls, and dense array growth.

## Verification

```bash
rtk dotnet build src/Asynkron.JsEngine/Asynkron.JsEngine.csproj -c Release
rtk dotnet test tests/Asynkron.JsEngine.Tests --filter "FullyQualifiedName~ArrayBuiltinsSpecTests.ArrayIterationCallbacks" -- xUnit.MaxParallelThreads=1 -timeout 20000
rtk ./tools/profile classdef --cpu --calltree-depth 40 --calltree-width 40
rtk ./tools/compare-jint-profiles --no-build classdef
rtk rg "EvaluateExpression\\(|ProfileEvaluateExpression\\(" src/Asynkron.JsEngine/Ast/TypedAstEvaluator.ExecutionPlanRunner*
rtk ./tools/profile forloop --memory
```

Results:

- Release project build passed.
- Focused array callback tests passed: 3 tests.
- Repeated selected-profile timings cleared the requested 10% threshold.
- The AST-eval seam scan returned no matches in the execution-plan runner files.
- `forloop --memory` completed with 5.85 MB total allocated.
- The canonical internal quality gate remains `rtk make quality` and is
  delegated to the orchestrator-run verification stage.
