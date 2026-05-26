# Activation Params Trampoline Frame Capacity

Date: 2026-05-26

## Selected Profile

`activation-params-lite` was selected from the required `rtk ./benchmark.sh`
baseline because it was the largest current Asynkron-side loss in this run:

```text
profile                    asynkron_ms  jint_ms  delta
activation-params-lite            1416      297  Jint 4.77x faster
```

The benchmark repeatedly calls a strict function with three simple parameters:

```js
function blend(a, b, c) {
    return (a + b) ^ c;
}
```

This maps directly to typed function invocation and simple IR activation.

## Profile Finding

The required CPU profile command was:

```bash
rtk ./tools/profile activation-params-lite --cpu --calltree-depth 40 --calltree-width 40
```

The relevant call tree under `InvokeWithContextSlow` showed the call trampoline
as the owner surface, with sampled time in frame setup and copied/cleared
storage:

```text
320.22 ms 100.0% 52x TypedAstEvaluator.SyncFunctionInvoker.InvokeWithContextSlow
|- 287.28 ms 89.7% 48x TypedAstEvaluator.SyncFunctionInvoker.TryInvokeIrFast
|  `- 283.05 ms 88.4% 48x TypedAstEvaluator.SyncFunctionInvoker.SyncIrCallTrampoline.TryInvoke
|     |- 115.93 ms 36.2% 1x Buffer.BulkMoveWithWriteBarrier
|     |- 68.95 ms 21.5% 9x TypedAstEvaluator.SyncFunctionInvoker.SyncIrCallTrampoline.PushFrame
|     |  `- 59.06 ms 18.4% 3x Buffer.BulkMoveWithWriteBarrier
|     `- 29.19 ms 9.1% 17x TypedAstEvaluator.SyncFunctionInvoker.SyncIrCallTrampoline.StepExpression
```

`activation-params-lite` uses shallow, non-recursive calls. Each trampoline
entry only needs one active frame, but the trampoline eagerly allocated storage
for 64 frames before the first `PushFrame`. That made the common shallow path
pay unnecessary zeroing/copy cost before executing the parameter expression.

## Change

`SyncIrCallTrampoline.InitialFrameCapacity` now starts at `4` instead of `64`.
The existing `EnsureFrameCapacity` growth path is unchanged, so deeper
recursive trampoline workloads still grow as needed. The change only reduces
eager frame-array work for the common shallow invocation case.

This keeps the slice inside the selected owner surface and does not change
recurrence infrastructure, invocation eligibility, expression semantics, or
normal fallback behavior.

## Final Signal

Repeated narrow baselines before the change were noisy:

```text
profile                 asynkron_ms  jint_ms  delta
activation-params-lite         1432      349  Jint 4.10x faster
activation-params-lite          770      303  Jint 2.54x faster
activation-params-lite          896      244  Jint 3.67x faster
```

After the change, repeated selected-profile timings were:

```text
profile                 asynkron_ms  jint_ms  delta
activation-params-lite          604      230  Jint 2.63x faster
activation-params-lite          571      267  Jint 2.14x faster
activation-params-lite          568      250  Jint 2.27x faster
activation-params-lite          633      388  Jint 1.63x faster
```

The final Asynkron average is about 594 ms versus about 1033 ms for the
repeated narrow baseline, a roughly 42% improvement. Even comparing the worst
final run to the fastest baseline run gives about an 18% improvement
(`770ms -> 633ms`), which clears the 10% threshold despite the noisy host.

## Verification

```bash
rtk ./benchmark.sh
rtk ./tools/profile activation-params-lite --cpu --calltree-depth 40 --calltree-width 40
rtk ./benchmark.sh --no-build activation-params-lite
rtk dotnet test tests/Asynkron.JsEngine.Tests -c Release --filter "FullyQualifiedName~ActivationSemanticsProofPackTests" -- xUnit.MaxParallelThreads=1 -timeout 20000
rtk rg "EvaluateExpression\\(|ProfileEvaluateExpression\\(" src/Asynkron.JsEngine/Ast/TypedAstEvaluator.ExecutionPlanRunner*
rtk ./tools/profile forloop --memory
```

The focused internal activation semantics proof pack passed: 32 tests.
The AST-eval seam scan returned no matches in `ExecutionPlanRunner*`, and the
`forloop` memory profile remained allocation-stable at 5.86 MB sampled.

The canonical internal quality gate remains `rtk make quality` and is delegated
to the orchestrator-run verification stage.
