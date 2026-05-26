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

## Follow-up: direct-eval call-path fast entrypoint (issue #2149)

This follow-up keeps the existing eval-program cache behavior unchanged and
targets only one shape: same-engine direct eval with one non-spread argument
from expression-program execution. That shape now calls an explicit
`EvalHostFunction` entrypoint directly instead of going through the generic
callable/context handoff path first.

Fresh selected-profile rows captured for this follow-up:

```text
baseline (pre-change, this branch): activation-evalscope-lite  asynkron_ms=455  jint_ms=276  Jint 1.65x faster
final (post-change):                 activation-evalscope-lite  asynkron_ms=418  jint_ms=245  Jint 1.71x faster
final (post-change, no-build):       activation-evalscope-lite  asynkron_ms=402  jint_ms=219  Jint 1.84x faster
final (post-change, no-build):       activation-evalscope-lite  asynkron_ms=404  jint_ms=277  Jint 1.46x faster
```

The run-to-run spread is still high on this local host, so this evidence stays
bounded to call-path behavior and avoids broad runtime-parity claims. The
captured post-change rows are consistently below the 455 ms baseline
(approximately 8-12% faster for Asynkron).

The final focused CPU profile shows the same-engine one-argument direct-eval
shape flowing through `EvalHostFunction.InvokeDirectSingleArgumentFast ->
EvalHostFunction.InvokeSingleArgument -> EvalHostFunction.GetOrParseProgram`,
without the earlier hot `CastHelpers.Box` cost inside the eval fast path.

## Verification

Completed locally:

```bash
rtk ./benchmark.sh
rtk ./tools/profile activation-evalscope-lite --cpu --calltree-depth 40 --calltree-width 40
rtk dotnet build -c Release src/Asynkron.JsEngine/Asynkron.JsEngine.csproj
rtk dotnet test tests/Asynkron.JsEngine.Tests -c Release --filter "FullyQualifiedName~ActivationSemanticsProofPackTests|FullyQualifiedName~StrictModeEvalTestBomb|FullyQualifiedName~PrivateNameEarlyErrorTests" -- xUnit.MaxParallelThreads=1 -timeout 20000
rtk ./benchmark.sh activation-evalscope-lite
rtk ./benchmark.sh activation-evalscope-lite
rtk ./benchmark.sh --no-build activation-evalscope-lite
rtk ./benchmark.sh --no-build activation-evalscope-lite
rtk ./tools/profile activation-evalscope-lite --cpu --calltree-depth 40 --calltree-width 40
```

The focused eval/activation/private-name proof pack passed with 52 tests. The
canonical internal quality gate remains `rtk make quality` and is delegated to
the orchestrator-run verification stage.
