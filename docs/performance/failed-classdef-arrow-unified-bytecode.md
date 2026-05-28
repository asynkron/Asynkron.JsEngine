# Failed classdef arrow unified bytecode callback

Date: 2026-05-28
Issue: autrun-diuj48oxr9eg-a2279c89bf

## Slice

This run targeted the `classdef` profile's final array callback:

```javascript
let sounds = dogs.map(d => d.speak());
```

The selected hypothesis was that simple arrow callbacks with no lexical
`this`, `new.target`, or `super` dependency could safely use the production
unified-bytecode function fast path instead of the existing simple IR activation
path. The attempted guard was narrow: arrows stayed rejected unless their
simple return expression program contained no `this`, `new.target`, or `super`
operation.

No runtime change is retained. Focused semantic tests passed while the change
was present, but the selected benchmark regressed sharply.

## Baseline signal

Baseline timestamp: 2026-05-28T19:13:22Z

Full pre-edit benchmark table:

```bash
rtk ./benchmark.sh
```

Selected row:

```text
profile                 asynkron_ms  jint_ms  delta
classdef                       2378      879  Jint 2.71x faster
```

Focused pre-edit benchmark:

```bash
rtk ./benchmark.sh classdef
```

```text
profile                 asynkron_ms  jint_ms  delta
classdef                        778      257  Jint 3.03x faster
```

Required pre-edit CPU profile command, run three times:

```bash
rtk ./tools/profile classdef --cpu --calltree-depth 40 --calltree-width 40
```

Representative profile evidence showed the selected callback surface under
`ArrayPrototype.Map`:

```text
ArrayPrototype.Map
  ArrayPrototype.InvokeArrayIterationCallback
    SyncFunctionInvoker.InvokeWithContext
      SyncFunctionInvoker.InvokeWithContextSlow
        SyncFunctionInvoker.TryInvokeIrFast
          ExecutionPlanRunner.EvaluateStandaloneExpressionProgram
            ExecutionPlanRunner.EvaluateExpressionProgram
              ExecutionPlanRunner.ExecuteProgramCall
```

## Attempted change

The reverted change relaxed `CanUseProductionUnifiedBytecodeFastPath` for arrow
functions only when the plan's simple return expression had no lexical
`this`/`new.target`/`super` dependency. That reused the same expression-program
operation scan already used by the simple IR activation arrow guard.

Focused semantic guardrails passed while the change was present:

```bash
rtk dotnet test tests/Asynkron.JsEngine.Tests -c Release --filter "FullyQualifiedName~ArrayIterationCallbacks" -- xUnit.MaxParallelThreads=1 -timeout 20000
```

```text
ok dotnet test: 4 tests passed, 7 existing nullable warnings in 1 project
```

## Final signal from reverted attempt

Final timestamp: 2026-05-28T19:27:45Z

Focused benchmark with the attempted runtime change:

```bash
rtk ./benchmark.sh classdef
```

```text
profile                 asynkron_ms  jint_ms  delta
classdef                       2957      965  Jint 3.06x faster
```

Signal delta:

```text
Baseline signal: classdef focused Asynkron row = 778 ms
Final signal: classdef attempted Asynkron row = 2957 ms
Signal delta: +2179 ms slower; no retained runtime improvement because the attempted arrow unified-bytecode path regressed the selected benchmark
```

Post-revert sanity checks showed no runtime/test diff remained. Follow-up
focused rows were also noisy and slow in this worker (`2967 ms`, then
`3253 ms` with `--no-build`), so they were not used to justify any retained
optimization.

## Interpretation

The production unified-bytecode path is not a drop-in win for this arrow
callback shape today. Even though it avoids the simple IR activation path, the
current production VM setup pays enough per-call overhead that the `dogs.map(d
=> d.speak())` callback becomes much slower than the existing runner path.

Future work should not retry merely relaxing the arrow rejection in
`CanUseProductionUnifiedBytecodeFastPath`. A more promising classdef callback
slice would need to remove callback-call setup directly or add a dedicated
shape-specific path for simple receiver method calls while proving that
ordinary method receiver binding remains intact.
