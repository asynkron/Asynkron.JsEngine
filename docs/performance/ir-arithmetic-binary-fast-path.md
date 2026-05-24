# IR arithmetic binary fast path

## Baseline

Issue `autrun-diqwn50g1d08-a853a50d18` selected the `ir-arithmetic` profile
from the requested `rtk ./benchmark.sh` baseline:

```text
profile                 asynkron_ms  jint_ms  delta
ir-arithmetic                 10586     3399  Jint 3.11x faster
```

The broader baseline had larger gaps in object and array profiles, but
`ir-arithmetic` maps directly to the expression bytecode hot path and is a
bounded runtime slice.

## Profile Finding

The requested CPU profile command was:

```bash
rtk ./tools/profile ir-arithmetic --cpu --calltree-depth 40 --calltree-width 40
```

The relevant call tree under `ExecuteInstructionLoop` was dominated by
`HandleAssignmentSlot` and its `EvaluateExpressionProgram` work:

```text
7928.88 ms 100.0% 8x TypedAstEvaluator.ExecutionPlanRunner.ExecuteInstructionLoop
|- 5335.20 ms 67.3% 808x TypedAstEvaluator.ExecutionPlanRunner.HandleAssignmentSlot
|  `- 4127.01 ms 52.1% 911x TypedAstEvaluator.ExecutionPlanRunner.EvaluateExpressionProgram
|     `- 977.10 ms 12.3% 393x TypedAstEvaluator.ExecutionPlanRunner.ProfileApplyBinaryOperator
`- 1417.74 ms 17.9% 538x TypedAstEvaluator.ExecutionPlanRunner.EvaluateExpressionProgram
```

The benchmark body is a numeric loop:

```js
var total = 0;
for (var i = 0; i < 200000; i++) {
    total = total + ((i * 3 + 7) % 11);
}
total;
```

Every hot binary operation in this expression has number operands after the
loop variables and literals are loaded.

## Change

`ExpressionOpKind.Binary` now calls a runner-local `ApplyProgramBinaryOperator`
helper. The helper keeps the generic `ApplyBinaryOperator` path for mixed
types and uncommon operators, but handles number-number arithmetic and numeric
comparison directly in expression bytecode execution.

This removes one generic operator dispatch layer and avoids rechecking the
full JavaScript binary operator matrix for the profiled all-number path. The
modulo case still uses `JsOps.MathMod` to preserve ECMAScript negative-zero and
NaN behavior.

## Final Signal

After the change, the focused selected-profile runs were:

```text
profile                 asynkron_ms  jint_ms  delta
ir-arithmetic                  8007     1466  Jint 5.46x faster
ir-arithmetic                  2971     1776  Jint 1.67x faster
ir-arithmetic                  2984     1666  Jint 1.79x faster
ir-arithmetic                  2380     1293  Jint 1.84x faster
```

The slowest final Asynkron run is 24.4% faster than the baseline
(`10586ms -> 8007ms`), and the repeated no-build runs are substantially faster.
The environment was noisy during the initial full-table baseline, so the
conservative comparison uses the slowest final run.

## Expected Impact

The change should help IR/expression-bytecode workloads that repeatedly apply
numeric binary operators, especially loops with arithmetic assignment
expressions. Non-number operands, BigInt, string concatenation, object
coercion, relational coercion, and other JavaScript semantics continue through
the existing generic operator implementation.
