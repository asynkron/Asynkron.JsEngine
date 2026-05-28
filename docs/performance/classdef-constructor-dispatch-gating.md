# Class Definition Constructor Dispatch Gating

Date: 2026-05-28

## Selected Profile

The required full benchmark baseline still showed `classdef` as a current
Asynkron-vs-Jint loss:

```text
profile                    asynkron_ms  jint_ms  delta
classdef                          1191      378  Jint 3.15x faster
```

Repeated focused baseline rows were:

```text
classdef                         1054      516  Jint 2.04x faster
classdef                          909      261  Jint 3.48x faster
classdef                          828      275  Jint 3.01x faster
```

The focused baseline average was about 930 ms.

## Profile Finding

Three CPU profiles were captured with:

```bash
rtk ./tools/profile classdef --cpu --calltree-depth 40 --calltree-width 40
```

The retained owner surface was still constructor and `super(...)` dispatch:

```text
ExecuteProgramConstructNoSpread
  ReflectHelper.Construct
    SyncFunctionInvoker.InvokeWithContext
      SyncFunctionInvoker.InvokeWithContextSlow
        ExecutionPlanRunner.RunSync
          ExecuteProgramSuperConstruct
            ExecuteProgramConstructNoSpread
```

The profile also showed repeated simple class-constructor fast-path eligibility
checks on non-constructor calls in the `dogs.map(d => d.speak())` tail. Those
checks were adjacent dispatch overhead from the previously retained
base/derived class constructor fast paths, but non-class functions can never
take either class-constructor path.

## Change

`SyncFunctionInvoker` now gates the two class-constructor IR activation probes
before calling their helper methods:

- derived-constructor fast path is considered only for derived class
  constructors
- base-constructor fast path is considered only for non-derived class
  constructors

The simple derived constructor environment also defines
`Symbol.LexicalThisEnvironment` directly to point at its function environment,
matching the normal derived-constructor owner binding and letting
`super(...)` resolve the this-initialization owner without an extra search.

This preserves the existing semantic eligibility guards. It does not broaden
which constructors can take the fast paths.

## Final Signal

Repeated selected-profile timings after the change were:

```text
classdef                          737      281  Jint 2.62x faster
classdef                          780      283  Jint 2.76x faster
classdef                          834      271  Jint 3.08x faster
```

The post-change Asynkron average was about 784 ms. Compared with the focused
baseline average of about 930 ms, this is about a 16% Asynkron-side
improvement, clearing the requested 10% threshold.

## Verification

```bash
rtk dotnet build src/Asynkron.JsEngine/Asynkron.JsEngine.csproj -c Release -v minimal
rtk dotnet test tests/Asynkron.JsEngine.Tests -c Release --filter "FullyQualifiedName~ClassStatementTests|FullyQualifiedName~ClassSuperSemanticsTests|FullyQualifiedName~ClassElementEvalTests|FullyQualifiedName~ActivationSemanticsProofPackTests" -- xUnit.MaxParallelThreads=1 -timeout 20000
rtk ./benchmark.sh --no-build classdef
rtk ./tools/profile classdef --cpu --calltree-depth 40 --calltree-width 40
rtk rg "EvaluateExpression\(|ProfileEvaluateExpression\(" src/Asynkron.JsEngine/Ast/TypedAstEvaluator.ExecutionPlanRunner*
rtk ./tools/profile forloop --memory
```

Results:

- Release library build passed.
- Focused class, super, class-element, and activation proof tests passed: 84
  tests.
- Repeated focused `classdef` timings cleared the requested 10% threshold.
- Follow-up `classdef` CPU profile still shows constructor and `super()`
  dispatch as the dominant remaining subtree.
- AST-eval seam scan returned no matches in execution-plan runner files.
- `forloop --memory` completed with 6.73 MB total allocated.
- The canonical internal quality gate remains `rtk make quality` and is
  delegated to the orchestrator-run verification stage.
