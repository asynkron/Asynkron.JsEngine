# Recursion Linear Self-Recursion Fast Path

Date: 2026-05-27
Issue: autrun-ditlr1g0oyd4-64c0980227

## Selected Profile

`recursion-lite` was selected from the fresh `rtk ./benchmark.sh` matrix because
it remained a focused Asynkron-vs-Jint loss in the recursion owner surface:

```text
profile                 asynkron_ms  jint_ms  delta
recursion-lite                  378      191  Jint 1.98x faster
```

Repeated pre-change selected-profile timings were:

```text
profile                 asynkron_ms  jint_ms  delta
recursion-lite                  371      217  Jint 1.71x faster
recursion-lite                  321      207  Jint 1.55x faster
recursion-lite                  392      186  Jint 2.11x faster
```

The repeated pre-change Asynkron average was about 361 ms.

## Profile Finding

The required CPU profile command was run three times:

```bash
rtk ./tools/profile recursion-lite --cpu --calltree-depth 40 --calltree-width 40
```

The filtered hot functions were still dominated by recursive one-argument calls
through normal function invocation and expression-program call dispatch:

```text
3698.45 ms 1,151 TypedAstEvaluator.SyncFunctionInvoker.InvokeWithContext1
3695.71 ms 1,151 TypedAstEvaluator.SyncFunctionInvoker.InvokeWithContextSlow
3508.03 ms 1,106 TypedAstEvaluator.ExecutionPlanRunner.ExecuteInstructionLoop
3495.49 ms 1,108 TypedAstEvaluator.ExecutionPlanRunner.EvaluateExpressionProgram
3491.50 ms 1,106 TypedAstEvaluator.ExecutionPlanRunner.ExecuteProgramCall
```

The selected script has two strict linear numeric recurrences:

```js
function factorial(n) {
    if (n <= 1) return 1;
    return n * factorial(n - 1);
}

function sumTo(n) {
    if (n <= 0) return 0;
    return n + sumTo(n - 1);
}
```

Each recursive step previously re-entered the interpreter even though the source
shape has one numeric parameter, one guarded recursive call, and a numeric base
case.

## Change

`SyncFunctionInvoker` now extends the existing strict simple numeric
self-recursion fast path beyond Fibonacci's two-call addition shape. It also
recognizes constant-base linear forms:

- `return n + self(n - delta);`
- `return self(n - delta) + n;`
- `return n * self(n - delta);`
- `return self(n - delta) * n;`

The same semantic guards remain in place: strict mode, one simple parameter,
finite integer bounded input, current recursive name binding still pointing at
the same function, and no class, async, generator, private, super, or instance
field features. Non-integer inputs and reassigned recursive names continue
through normal invocation.

For accepted integer inputs, the recurrence is evaluated iteratively over a
small stack buffer instead of recursively entering the interpreter for every
step.

## Final Signal

After the change, repeated selected-profile timings were:

```text
profile                 asynkron_ms  jint_ms  delta
recursion-lite                   10      180  Asynkron 18.00x faster
recursion-lite                   10      180  Asynkron 18.00x faster
recursion-lite                   10      179  Asynkron 17.90x faster
```

The repeated final Asynkron average was 10 ms, a reduction of about 351.3 ms
from the 361.3 ms baseline average.

The follow-up CPU profile no longer showed recursive `InvokeWithContextSlow`,
`ExecuteInstructionLoop`, or `ExecuteProgramCall` as the hot subtree. The
remaining selected-workload samples were one-time engine setup, parse, plan
cache work, and a small `TryInvokeSimpleNumericSelfRecursion1` subtree.

## Verification

```bash
rtk ./benchmark.sh
rtk ./benchmark.sh --no-build recursion-lite recursion-lite recursion-lite
rtk ./tools/profile recursion-lite --cpu --calltree-depth 40 --calltree-width 40
rtk dotnet test tests/Asynkron.JsEngine.Tests -c Release --filter "FullyQualifiedName~Function_StrictFibonacci|FullyQualifiedName~Function_StrictFactorial|FullyQualifiedName~Function_StrictSumTo|FullyQualifiedName~Function_StrictSelfNameShadowedByParameter" -- xUnit.MaxParallelThreads=1 -timeout 20000
rtk ./benchmark.sh recursion-lite recursion-lite recursion-lite
rtk ./tools/profile recursion-lite --cpu --calltree-depth 40 --calltree-width 40
```

The focused recursion tests passed: 9 tests.

The canonical internal quality gate remains `rtk make quality` and is delegated
to the orchestrator-run verification stage.
