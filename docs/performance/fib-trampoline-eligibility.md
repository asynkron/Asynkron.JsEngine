# Fib Trampoline Eligibility

Date: 2026-05-25

## Selected Profile

`fib` was selected from the required `rtk ./benchmark.sh` baseline because it
was the largest current Asynkron-vs-Jint loss in this run:

```text
profile                 asynkron_ms  jint_ms  delta
fib                            7394      866  Jint 8.54x faster
```

## Profile Finding

The required CPU profile was:

```bash
rtk ./tools/profile fib --cpu --calltree-depth 40 --calltree-width 40
```

The hot path was recursive function invocation through the IR expression
program call path:

```text
120595.07 ms 24,503 TypedAstEvaluator.SyncFunctionInvoker.InvokeWithContextSlow
120319.84 ms 24,404 TypedAstEvaluator.ExecutionPlanRunner.ExecuteProgramCall
120034.24 ms 24,376 TypedAstEvaluator.ExecutionPlanRunner.EvaluateExpressionProgram
119972.62 ms 24,350 TypedAstEvaluator.ExecutionPlanRunner.ExecuteInstructionLoop
117482.68 ms 23,495 TypedAstEvaluator.ExecutionPlanRunner.RunSync
  7643.99 ms  1,594 TypedAstEvaluator.SyncFunctionInvoker.SyncIrCallTrampoline.TryInvoke
  6619.75 ms  1,442 TypedAstEvaluator.SyncFunctionInvoker.SyncIrCallTrampoline.PushFrame
```

`SyncIrCallTrampoline` could only execute self-calls when the call was the
final operation of a return expression. The eligibility check was broader than
that executor contract, so non-tail recursive return expressions such as
`fib(n - 1) + fib(n - 2)` repeatedly entered the trampoline, initialized frame
storage, then bailed back to normal invocation.

## Change

`SyncIrCallTrampoline.CanRunExpression` now receives the expression purpose.
Branch expressions reject calls, and return expressions accept calls only when
the call is the final expression-program operation. This keeps the existing
tail-recursive trampoline path available while preventing non-tail recursive
calls from paying a failed trampoline setup cost on every invocation.

The change is intentionally limited to trampoline eligibility. It does not add
new recursive semantics, change normal invocation, widen supported expression
bytecode operations, or alter recurrence infrastructure.

## Final Signal

Repeated selected-profile timing after the change:

```text
profile                 asynkron_ms  jint_ms  delta
fib                            3971      609  Jint 6.52x faster
fib                            4282      774  Jint 5.53x faster
fib                            3582      614  Jint 5.83x faster
fib                            3806      790  Jint 4.82x faster
```

The repeated final Asynkron timings averaged about 3910 ms. Compared with the
7394 ms full-table baseline signal, that is roughly a 47% Asynkron-side
improvement.

The follow-up CPU profile no longer listed `SyncIrCallTrampoline.TryInvoke` or
`SyncIrCallTrampoline.PushFrame` in the filtered top functions for `fib`.
Remaining sampled cost is normal recursive call execution through
`InvokeWithContextSlow`, `EvaluateExpressionProgram`, and
`ExecuteProgramCall`.

## Verification

```bash
rtk ./benchmark.sh
rtk ./tools/profile fib --cpu --calltree-depth 40 --calltree-width 40
rtk ./benchmark.sh fib
rtk ./benchmark.sh --no-build fib
rtk dotnet test tests/Asynkron.JsEngine.Tests -c Release --filter "FullyQualifiedName~Function_StrictFibonacci_RecursiveSelfCall|FullyQualifiedName~Function_RecursiveNamed_ViaInternalName|FullyQualifiedName~Function_StrictSelfNameShadowedByParameter_DoesNotForceRecursiveTarget" -- xUnit.MaxParallelThreads=1 -timeout 20000
```

The focused internal recursion tests passed: 3 tests.

The canonical internal quality gate remains `rtk make quality` and is delegated
to the orchestrator-run verification stage.
