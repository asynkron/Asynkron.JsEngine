# Class Definition Inline Expression Buffers

Date: 2026-05-24

## Selected Profile

`classdef` was selected from the required `rtk ./benchmark.sh` baseline because it
was one of the largest current Asynkron-vs-Jint losses:

```text
classdef  asynkron_ms=1279  jint_ms=339  Jint 3.77x faster
```

A second focused pre-change run showed the same benchmark remained a clear
loser, with additional noise in the runner:

```text
classdef  asynkron_ms=1537  jint_ms=276  Jint 5.57x faster
```

## Profile Finding

The required CPU profile was:

```bash
rtk ./tools/profile classdef --cpu --calltree-depth 40 --calltree-width 40
```

The hot call tree rooted at `ExecuteInstructionLoop` spent most of the selected
sample under repeated constructor, `super(...)`, `Array.prototype.map`, and
method-call expression programs. Inside those nested calls, the sampled profile
showed repeated `AcquireExpressionBuffers` / `ReturnCachedExpressionBuffers`
cost, with `SharedArrayPool<JsValue>.Rent` and `SharedArrayPool<JsValue>.Return`
appearing under small `EvaluateExpressionProgram` executions.

The script has only 14 lowered expression programs and the storage diagnostic
showed all max stack depths fit within eight slots:

```text
max_stack_depth_histogram:
  depth=1: 6
  depth=2: 6
  depth=3: 1
  depth=7: 1
```

## Change

`ExecutionPlanRunner.EvaluateExpressionProgram` now uses stack-local inline
buffers for expression programs with max stack depth up to eight slots. Larger
programs keep the existing pooled-array path.

This removes ArrayPool rent/return traffic from common short expression
programs without changing expression semantics, assignment reference handling,
optional-chain flag handling, or the fallback path for larger stack depths.

## Final Signal

After the change, three focused `classdef` comparison runs were:

```text
classdef  asynkron_ms=1210  jint_ms=267  Jint 4.53x faster
classdef  asynkron_ms=1059  jint_ms=268  Jint 3.95x faster
classdef  asynkron_ms=1138  jint_ms=268  Jint 4.25x faster
```

The post-change average was about 1136 ms. Compared with the repeated focused
pre-change signal of 1537 ms, that is roughly a 26% Asynkron-side improvement.
Compared with the first full-table baseline of 1279 ms, two of the three final
runs clear the 10% threshold and the average remains roughly 11% faster.

The follow-up CPU profile no longer showed expression buffer rent/return as a
dominant classdef subtree; the remaining sampled cost shifted to constructor
environment setup, property assignment, function invocation, and array/map
work.

## Verification

```bash
rtk dotnet build src/Asynkron.JsEngine/Asynkron.JsEngine.csproj -c Release -v q --nologo
rtk dotnet test tests/Asynkron.JsEngine.Tests -c Release --filter "FullyQualifiedName~FoundationTests&FullyQualifiedName~Class" -- xUnit.MaxParallelThreads=1 -timeout 20000
rtk dotnet test tests/Asynkron.JsEngine.Tests -c Release --filter "FullyQualifiedName~ExpressionProgram" -- xUnit.MaxParallelThreads=1 -timeout 20000
rtk rg "EvaluateExpression\\(|ProfileEvaluateExpression\\(" src/Asynkron.JsEngine/Ast/TypedAstEvaluator.ExecutionPlanRunner*
rtk ./tools/profile forloop --memory
```

Results:

- Release library build passed.
- Class-focused internal tests passed: 98 tests.
- ExpressionProgram internal tests passed: 173 tests.
- AST-eval seam scan returned no matches in the execution-plan runner files.
- `forloop --memory` completed with 6.04 MB total allocated.
