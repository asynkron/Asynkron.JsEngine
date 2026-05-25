# Fib Single-Argument Typed Call Dispatch

Date: 2026-05-26

## Selected Profile

`fib` was selected from the required `rtk ./benchmark.sh` baseline because it
was the largest current Asynkron-vs-Jint loss in this run:

```text
profile                 asynkron_ms  jint_ms  delta
fib                            3892      634  Jint 6.14x faster
```

Repeated pre-change selected-profile timing showed the expected noise, but kept
the same loss shape:

```text
profile                 asynkron_ms  jint_ms  delta
fib                            4653      594  Jint 7.83x faster
fib                            3835      753  Jint 5.09x faster
fib                            3573      555  Jint 6.44x faster
```

The repeated pre-change Asynkron average was about 4020 ms.

## Profile Finding

The required CPU profile was:

```bash
rtk ./tools/profile fib --cpu --calltree-depth 40 --calltree-width 40
```

The hot path was recursive single-argument function invocation through the
expression-program call executor:

```text
71617.26 ms 30,375 TypedAstEvaluator.ExecutionPlanRunner.EvaluateExpressionProgram
71560.58 ms 30,301 TypedAstEvaluator.SyncFunctionInvoker.InvokeWithContextSlow
71482.72 ms 30,287 TypedAstEvaluator.ExecutionPlanRunner.ExecuteInstructionLoop
71137.76 ms 30,124 TypedAstEvaluator.ExecutionPlanRunner.ExecuteProgramCall
 4738.88 ms    398 TypedAstEvaluator.SyncFunctionInvoker.InvokeWithContext
 4735.44 ms    396 TypedAstEvaluator.InvokeCallableJsValueGeneric
 4720.39 ms    390 TypedAstEvaluator.InvokeCallableSingleArg
```

`fib` repeatedly calls the same typed JavaScript function with one argument.
The general single-argument path still went through generic callable dispatch
and caller-environment plumbing before reaching `SyncFunctionInvoker`.

## Change

Single-argument typed JavaScript function calls now route directly to the
existing `SyncFunctionInvoker.InvokeWithContext1` entrypoint:

- `ExecutionPlanRunner.ExecuteProgramCall` bypasses the generic helper for
  one-argument `SyncFunctionInvoker` calls.
- `InvokeCallableSingleArg` keeps the same direct typed-function path for
  other one-argument helper callers.

The change is intentionally limited to typed JavaScript functions. Host
functions, eval, debug-aware host functions, spread calls, class-constructor
rejection, and multi-argument dispatch keep the existing paths.

## Final Signal

Repeated selected-profile timing after the change:

```text
profile                 asynkron_ms  jint_ms  delta
fib                            3580      596  Jint 6.01x faster
fib                            3505      596  Jint 5.88x faster
fib                            3559      679  Jint 5.24x faster
fib                            3577      593  Jint 6.03x faster
fib                            3484      643  Jint 5.42x faster
fib                            3565      676  Jint 5.27x faster
fib                            3665      545  Jint 6.72x faster
fib                            3537      535  Jint 6.61x faster
```

The repeated final Asynkron average was about 3559 ms. Compared with the
repeated pre-change average of about 4020 ms, this is about an 11.6% Asynkron
side improvement.

The follow-up CPU profile still shows the expected recursive call stack, but
the dispatch helper layer is no longer present in the filtered top functions:

```text
71173.84 ms 29,128 TypedAstEvaluator.SyncFunctionInvoker.InvokeWithContextSlow
70855.72 ms 29,180 TypedAstEvaluator.ExecutionPlanRunner.EvaluateExpressionProgram
70730.80 ms 29,098 TypedAstEvaluator.ExecutionPlanRunner.ExecuteInstructionLoop
70395.36 ms 28,939 TypedAstEvaluator.ExecutionPlanRunner.ExecuteProgramCall
 5064.67 ms    586 TypedAstEvaluator.SyncFunctionInvoker.InvokeWithContext1
```

## Verification

```bash
rtk ./benchmark.sh
rtk ./tools/compare-jint-profiles --no-build fib fib fib
rtk ./tools/compare-jint-profiles fib fib fib fib
rtk ./tools/profile fib --cpu --calltree-depth 40 --calltree-width 40
```

Focused internal recursion tests and the canonical internal quality gate are
recorded in the build handoff for this issue.
