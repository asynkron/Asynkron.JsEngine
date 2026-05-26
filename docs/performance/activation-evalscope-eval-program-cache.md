# Activation EvalScope Eval Program Cache

Date: 2026-05-26

## Selected Profile

`activation-evalscope-lite` was selected from the required `rtk ./benchmark.sh`
baseline because it was the largest fresh Asynkron-vs-Jint loss and avoided the
recent classdef and array-callback work:

```text
activation-evalscope-lite  asynkron_ms=3541  jint_ms=559  Jint 6.33x faster
```

The workload repeatedly calls strict closures that read caller activation
bindings through a stable direct-eval source:

```js
return eval('y + shared');
```

## Profile Finding

The required CPU profile was:

```bash
rtk ./tools/profile activation-evalscope-lite --cpu --calltree-depth 40 --calltree-width 40
```

The hot subtree under `InvokeWithContextSlow` showed `EvalHostFunction.Invoke`
reparsing and rebuilding the same eval program on each call:

```text
ExecuteProgramCall
  EvalHostFunction.Invoke
    JsEngine.ParseProgram
      JsAstParser.DirectParser.ParseProgram
    ScriptPlanCache.Build
      ExecutionPlanBuilder.TryBuildInternal
```

In that pre-change profile, about 75% of the sampled root time was under
`ExecutePlan -> EvaluateExpressionProgram -> ExecuteProgramCall ->
EvalHostFunction.Invoke`, with parse and plan-build frames dominating the eval
call.

## Change

`EvalHostFunction` now keeps a small per-engine LRU cache of parsed eval
`ProgramNode` instances keyed by source text and forced strictness. Reusing the
same immutable program also reuses its warmed AST caches and `ScriptPlanCache`,
while execution still runs against the current eval environment so direct eval
continues to observe the caller's current activation bindings.

The cache is intentionally bounded to 64 entries and local to the host eval
function. Parse errors are not cached, and private-name validation still runs
against the current caller context after retrieving the program.

A focused regression test covers repeated strict direct eval across different
closures and changing caller/global bindings.

## Final Signal

After the change, repeated focused comparison runs were:

```text
activation-evalscope-lite  asynkron_ms=875  jint_ms=338  Jint 2.59x faster
activation-evalscope-lite  asynkron_ms=421  jint_ms=252  Jint 1.67x faster
activation-evalscope-lite  asynkron_ms=618  jint_ms=313  Jint 1.97x faster
activation-evalscope-lite  asynkron_ms=521  jint_ms=323  Jint 1.61x faster
```

Against the 3541 ms full-table baseline, the first post-change run improved
Asynkron time by about 75%, and the repeated no-build runs improved it by about
83-88%. This clears the required 10% improvement threshold despite local timing
noise.

A follow-up CPU profile confirmed the targeted parse/build subtree no longer
dominates eval execution. `EvalHostFunction.Invoke` was sampled at about 4-5%
of the profiled root, and `GetOrParseProgram` was a dictionary lookup path
rather than a parse/plan-build path.

## Verification

Completed locally:

```bash
rtk ./benchmark.sh
rtk ./tools/profile activation-evalscope-lite --cpu --calltree-depth 40 --calltree-width 40
rtk dotnet build -c Release src/Asynkron.JsEngine/Asynkron.JsEngine.csproj
rtk dotnet test tests/Asynkron.JsEngine.Tests -c Release --filter "FullyQualifiedName~ActivationSemanticsProofPackTests|FullyQualifiedName~StrictModeEvalTestBomb|FullyQualifiedName~PrivateNameEarlyErrorTests" -- xUnit.MaxParallelThreads=1 -timeout 20000
rtk ./benchmark.sh activation-evalscope-lite
rtk ./benchmark.sh --no-build activation-evalscope-lite
rtk ./benchmark.sh --no-build activation-evalscope-lite
rtk ./benchmark.sh --no-build activation-evalscope-lite
rtk ./tools/profile activation-evalscope-lite --cpu --calltree-depth 40 --calltree-width 40
```

The focused eval/activation/private-name proof pack passed with 52 tests. The
canonical internal quality gate remains `rtk make quality` and is delegated to
the orchestrator-run verification stage.
