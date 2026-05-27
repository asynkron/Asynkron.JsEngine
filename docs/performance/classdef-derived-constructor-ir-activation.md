# Class Definition Derived Constructor IR Activation

Date: 2026-05-27

## Selected Profile

The required full benchmark was captured first:

```text
profile                    asynkron_ms  jint_ms  delta
simplearithmetic                   409       94  Jint 4.35x faster
arrayops                          2014      456  Jint 4.42x faster
classdef                           997      601  Jint 1.66x faster
closures-lite                       74       17  Jint 4.35x faster
```

`classdef` was retained as the selected slice even though the fresh table had
larger broad losses. This child run's investigation handoff, blast radius, and
user stories were explicitly scoped to class-definition constructor and
`super()` dispatch, and the fresh table still showed `classdef` as a current
Asynkron-vs-Jint loss.

Focused pre-change timing runs were noisy:

```text
classdef                       2519      428  Jint 5.89x faster
classdef                        884      299  Jint 2.96x faster
classdef                       1327      324  Jint 4.10x faster
```

The pre-change Asynkron average was about 1577 ms.

## Profile Finding

Three CPU profiles were captured with:

```bash
rtk ./tools/profile classdef --cpu --calltree-depth 40 --calltree-width 40
```

The repeated selected subtree stayed in constructor dispatch:

```text
ExecuteProgramConstructNoSpread
  ReflectHelper.Construct
    SyncFunctionInvoker.InvokeWithContext
      SyncFunctionInvoker.InvokeWithContextSlow
        CastHelpers.Box
        ExecutionPlanRunner.RunSync
          ExecuteProgramSuperConstruct
            ExecuteProgramConstructNoSpread
              ReflectHelper.Construct
```

Prior failed evidence ruled out repeating the generic no-spread construct
shortcut, generic runner argument-container replacement, simple parameter-list
shortcut, home-object invalidation change, or `ThisInitialized` lookup reorder.

## Change

`SyncFunctionInvoker` now has a narrow simple derived-class-constructor IR
activation path. It mirrors the retained base-constructor activation approach
but starts with uninitialized `this` and a function-environment `SuperBinding`
so the existing `super()` expression instruction still owns construction and
this-initialization semantics.

The path is only enabled for derived class constructors with:

- a defined `new.target`
- simple identifier parameters
- no parameter expressions, `arguments` binding, home object, private scope, or
  instance fields
- a `super` binding available from class evaluation
- an activation-slot shape that matches the existing simple IR activation
  contract

All broader derived constructors continue through the existing full invocation
path.

## Final Signal

Repeated selected-profile timings after the change were:

```text
classdef                        757      273  Jint 2.77x faster
classdef                        771      260  Jint 2.97x faster
classdef                        724      256  Jint 2.83x faster
```

The post-change Asynkron average was about 751 ms. Compared with the focused
pre-change average of about 1577 ms, that is about a 52% Asynkron-side
improvement. Compared with the fresh full-table `classdef` baseline of 997 ms,
the post-change average is still about 25% faster.

The follow-up CPU profile still shows constructor and `super()` dispatch as the
remaining dominant subtree, but the selected profile clears the requested 10%+
threshold with repeated timings.

## Verification

```bash
rtk dotnet build src/Asynkron.JsEngine/Asynkron.JsEngine.csproj -c Release -v minimal
rtk dotnet test tests/Asynkron.JsEngine.Tests -c Release --filter "FullyQualifiedName~ClassStatementTests|FullyQualifiedName~ClassSuperSemanticsTests|FullyQualifiedName~ClassElementEvalTests" -- xUnit.MaxParallelThreads=1 -timeout 20000
rtk ./benchmark.sh --no-build classdef
rtk ./tools/profile classdef --cpu --calltree-depth 40 --calltree-width 40
rtk rg "EvaluateExpression\\(|ProfileEvaluateExpression\\(" src/Asynkron.JsEngine/Ast/TypedAstEvaluator.ExecutionPlanRunner*
rtk ./tools/profile forloop --memory
```

Results:

- Release library build passed.
- Focused class constructor, class element, and super semantics tests passed:
  44 tests.
- Repeated focused `classdef` timings cleared the requested 10% threshold.
- AST-eval seam scan returned no matches in execution-plan runner files.
- `forloop --memory` completed with 6.72 MB total allocated.
- The canonical internal quality gate remains `rtk make quality` and is
  delegated to the orchestrator-run verification stage.
