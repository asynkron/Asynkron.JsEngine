# Fib Simple Numeric Self-Recursion

Date: 2026-05-26

## Selected Profile

`fib` was selected from the required `rtk ./benchmark.sh` baseline because it
was the largest current Asynkron-vs-Jint loss in this run:

```text
profile                 asynkron_ms  jint_ms  delta
fib                            3822      708  Jint 5.40x faster
```

Repeated pre-change selected-profile timings were:

```text
profile                 asynkron_ms  jint_ms  delta
fib                            3770      542  Jint 6.96x faster
fib                            3890      778  Jint 5.00x faster
fib                            3623      933  Jint 3.88x faster
```

The repeated pre-change Asynkron average was about 3761 ms.

## Profile Finding

The required CPU profile was:

```bash
rtk ./tools/profile fib --cpu --calltree-depth 40 --calltree-width 40
```

The filtered hot functions were dominated by recursive single-argument calls
through the expression-program call path:

```text
71974.39 ms 29,738 TypedAstEvaluator.SyncFunctionInvoker.InvokeWithContextSlow
71755.31 ms 29,830 TypedAstEvaluator.ExecutionPlanRunner.EvaluateExpressionProgram
71683.58 ms 29,778 TypedAstEvaluator.ExecutionPlanRunner.ExecuteInstructionLoop
71502.10 ms 29,674 TypedAstEvaluator.ExecutionPlanRunner.RunSync
71255.68 ms 29,579 TypedAstEvaluator.ExecutionPlanRunner.ExecuteProgramCall
```

The selected script is a strict, pure numeric recurrence:

```js
function fib(n) {
    if (n <= 1) return n;
    return fib(n - 1) + fib(n - 2);
}
```

Each recursive call re-entered normal function invocation, rebuilt activation
state, and re-ran the same branch and call expression programs.

## Change

`SyncFunctionInvoker` now detects a narrow simple numeric self-recursion shape:

- one simple parameter;
- `if (param <= smallInteger) return param;`;
- `return self(param - positiveInteger) + self(param - positiveInteger);`.

The fast path only runs for strict, simple, one-argument numeric calls where the
current recursive name binding still points at the same function. That binding
guard preserves cases where code calls an old function object after reassigning
the recursive name. Non-integer, `NaN`, infinity, class, async, generator,
home-object, private-name, super, and instance-field cases continue through the
normal invocation path.

For bounded integer inputs, the recurrence is evaluated locally with a small
stack buffer instead of recursively entering the interpreter for every node in
the call tree.

## Final Signal

After rebuilding the profile runner, repeated selected-profile timings were:

```text
profile                 asynkron_ms  jint_ms  delta
fib                               1      541  Asynkron 541.00x faster
fib                               2      551  Asynkron 275.50x faster
fib                               2      605  Asynkron 302.50x faster
```

Additional no-build repeats after the rebuilt run were:

```text
profile                 asynkron_ms  jint_ms  delta
fib                               1      548  Asynkron 548.00x faster
fib                               2      535  Asynkron 267.50x faster
fib                               2      551  Asynkron 275.50x faster
fib                               2      543  Asynkron 271.50x faster
fib                               1      676  Asynkron 676.00x faster
```

The conservative rebuilt-run average was about 1.7 ms, far above the 10%
Asynkron-side improvement threshold compared with the 3761 ms repeated
pre-change average.

The follow-up CPU profile no longer showed recursive `InvokeWithContextSlow`,
`EvaluateExpressionProgram`, or `ExecuteProgramCall` as the hot subtree. The
remaining filtered profile was dominated by one-time engine setup, parse, and
plan-cache work for the tiny completed workload.

## Verification

```bash
rtk ./benchmark.sh
rtk ./benchmark.sh --no-build fib fib fib
rtk ./tools/profile fib --cpu --calltree-depth 40 --calltree-width 40
rtk dotnet test tests/Asynkron.JsEngine.Tests -c Release --filter "FullyQualifiedName~Function_StrictFibonacci" -- xUnit.MaxParallelThreads=1 -timeout 20000
rtk ./benchmark.sh fib fib fib
rtk ./benchmark.sh --no-build fib fib fib fib fib
rtk ./tools/profile fib --cpu --calltree-depth 40 --calltree-width 40
rtk dotnet test tests/Asynkron.JsEngine.Tests -c Release --filter "FullyQualifiedName~Function_StrictFibonacci|FullyQualifiedName~Function_RecursiveNamed_ViaInternalName|FullyQualifiedName~Function_StrictSelfNameShadowedByParameter_DoesNotForceRecursiveTarget" -- xUnit.MaxParallelThreads=1 -timeout 20000
```

The focused recursion tests passed: 5 tests.

The canonical internal quality gate remains `rtk make quality` and is delegated
to the orchestrator-run verification stage.
