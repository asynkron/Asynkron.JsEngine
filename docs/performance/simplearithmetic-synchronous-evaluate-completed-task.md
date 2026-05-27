# Simple arithmetic synchronous Evaluate completion

## Baseline

Issue `autrun-dit1y6ykons0-facccf278d` selected `simplearithmetic` from the
required full benchmark baseline:

```text
profile                    asynkron_ms  jint_ms  delta
simplearithmetic                  2302      138  Jint 16.68x faster
```

The selected profile runs a very small synchronous script 10,000 times against
a shared parsed program:

```js
let x = 1 + 2 * 3 - 4 / 2;
let y = x * x + Math.sqrt(16);
let z = y % 7 + Math.pow(2, 10);
z;
```

## Profile Finding

The required CPU profile command was:

```bash
rtk ./tools/profile simplearithmetic --cpu --calltree-depth 40 --calltree-width 40
```

Before the change, the selected profile spent substantial filtered time in the
async `Evaluate` state machine even though the script had no pending event-loop
work:

```text
Top Functions (Filtered)
4400.25 ms  Program.Main lambda
1132.86 ms  StateMachine.Evaluate.MoveNext
290.40 ms   JsEngine.ctor
98.66 ms    AstCache.GetOrCreate

Call Tree (root: ExecuteInstructionLoop)
27.19 ms 100.0% 1x TypedAstEvaluator.ExecutionPlanRunner.ExecuteInstructionLoop
```

The IR loop itself was small for this workload. The measurable owner was the
task/state-machine completion path around `JsEngine.Evaluate(ProgramNode)` for
pure synchronous executions.

## Change

`JsEngine.Evaluate(ProgramNode)` now keeps the existing synchronous execution
first, but it is no longer an `async` method unconditionally. When the program
runs synchronously, drains microtasks, and has no pending event-loop work, it
cleans up timers/deferred tasks and returns `Task.FromResult(...)`.

If execution schedules timers, promise continuations, async modules, or other
pending work, the method continues through the existing async drain path and
uses the same cleanup behavior. Faulted synchronous execution is returned as a
faulted task so the public `Evaluate(ProgramNode)` contract remains await-based.

The test `PendingAsyncWorkTrackingTests.Evaluate_SynchronousProgram_CompletesWithoutStartingEventLoop`
locks the intended fast path: a pre-parsed synchronous script completes
successfully without starting the event loop.

## Final Signal

Focused build and semantic checks:

```text
rtk dotnet build src/Asynkron.JsEngine/Asynkron.JsEngine.csproj -c Release --no-restore --disable-build-servers -m:1 -v:minimal
ok dotnet build: 2 projects, 0 errors, 0 warnings (00:00:11.44)

rtk dotnet test tests/Asynkron.JsEngine.Tests --filter "FullyQualifiedName~PendingAsyncWorkTrackingTests|FullyQualifiedName~TimerTests|FullyQualifiedName~PromiseTests|FullyQualifiedName~JsOpsTests" -- xUnit.MaxParallelThreads=1 -timeout 20000
ok dotnet test: 58 tests passed, 7 existing nullable warnings in 2 projects (2.8 s)
```

Repeated selected-profile timing after the change:

```text
profile                 asynkron_ms  jint_ms  delta
simplearithmetic                402      122  Jint 3.30x faster
simplearithmetic                390      120  Jint 3.25x faster
simplearithmetic                390      119  Jint 3.28x faster
```

The conservative final comparison uses the slowest final Asynkron run:
`2302ms -> 402ms`, an 82.5% speedup. Repeated final runs were stable around
390-402ms.

The post-change CPU profile no longer shows `StateMachine.Evaluate.MoveNext` in
the filtered top functions:

```text
Top Functions (Filtered)
1393.26 ms  Program.Main lambda
143.54 ms   JsEngine.ctor
36.93 ms    JsEngine.ParseProgram
34.51 ms    JsEngine.CancelAllTimers

Call Tree (root: ExecuteInstructionLoop)
16.80 ms 100.0% 1x TypedAstEvaluator.ExecutionPlanRunner.ExecuteInstructionLoop
```

## Expected Impact

This helps repeated synchronous `Evaluate(ProgramNode)` workloads where the
engine can execute, drain microtasks, and prove no event-loop work remains.
Async scripts and modules still use the async drain path, so timers, promises,
and top-level async module dependencies keep their existing behavior.
