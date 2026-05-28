# Failed Classdef This-Initialization Guard

Date: 2026-05-28

## Selected Profile

This recurrence child kept the investigation handoff's `classdef`
constructor/super dispatch slice. The required full benchmark baseline still
showed `classdef` as a current Asynkron-vs-Jint loss:

```text
profile                    asynkron_ms  jint_ms  delta
classdef                           745      259  Jint 2.88x faster
```

Repeated focused baseline rows were noisy:

```text
classdef                           728      280  Jint 2.60x faster
classdef                           770      297  Jint 2.59x faster
classdef                          1430      366  Jint 3.91x faster
classdef                          1800      308  Jint 5.84x faster
classdef                           972      276  Jint 3.52x faster
```

The first two focused samples matched the latest retained classdef
optimization range. The next two were large noise outliers, so this run treated
any retained runtime change as needing a clearly repeatable improvement.

## Profile Finding

Three CPU profiles were captured with:

```bash
rtk ./tools/profile classdef --cpu --calltree-depth 40 --calltree-width 40
```

The dominant subtree remained constructor and `super(...)` dispatch:

```text
ExecuteProgramConstructNoSpread
  ReflectHelper.Construct
    SyncFunctionInvoker.InvokeWithContext
      SyncFunctionInvoker.InvokeWithContextSlow
        ExecutionPlanRunner.RunSync
          ExecuteProgramSuperConstruct
            ExecuteProgramConstructNoSpread
```

The `dogs.map(d => d.speak())` tail also repeatedly showed `this` resolution
and this-initialization checks under class method calls. That made a small
guarded `IsThisInitializationKnownTrue(...)` shortcut a plausible adjacent
classdef slice.

## Trial

I tried a guarded early return in `JsEnvironmentExtensions.IsThisInitializationKnownTrue(...)`:

- return true when the evaluation context already knows `this` is initialized
- require no local `Symbol.LexicalThisEnvironment` binding on the current
  environment
- require no local `Symbol.ThisInitialized` binding on the current environment

The intended fast path was to avoid scope-chain searches for ordinary class
method bodies while preserving arrow, derived-constructor, and explicit
this-initialization owner environments.

The trial built successfully and the focused proof pack passed:

```bash
rtk dotnet build src/Asynkron.JsEngine/Asynkron.JsEngine.csproj -c Release -v minimal
rtk dotnet test tests/Asynkron.JsEngine.Tests -c Release --filter "FullyQualifiedName~ClassStatementTests|FullyQualifiedName~ClassSuperSemanticsTests|FullyQualifiedName~ClassElementEvalTests|FullyQualifiedName~ActivationSemanticsProofPackTests" -- xUnit.MaxParallelThreads=1 -timeout 20000
```

Results:

- Release library build passed.
- Focused class, super, class-element, and activation proof tests passed: 84
  tests.

## Final Signal

Repeated focused timings after the trial were:

```text
classdef                           947      310  Jint 3.05x faster
classdef                           927      416  Jint 2.23x faster
classdef                           777      259  Jint 3.00x faster
```

Baseline timestamp: 2026-05-28T06:45:00Z
Baseline signal: classdef focused Asynkron = 728/770 ms stable early range, 972 ms post-outlier sample
Final timestamp: 2026-05-28T06:53:20Z
Final signal: classdef focused Asynkron = 947/927/777 ms
Signal delta: no repeatable improvement; final samples did not clear the required 10% improvement threshold

## Outcome

No runtime change was retained. The this-initialization shortcut was reverted
because the final signal was not faster than the stable part of the focused
baseline and did not clear the 10% acceptance gate.

This evidence suggests the remaining `classdef` cost is still dominated by
generic construction, `super(...)` dispatch, and full constructor invocation
rather than the ordinary class-method this-initialization guard alone.
