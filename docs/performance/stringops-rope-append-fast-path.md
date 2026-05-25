# String operations rope append fast path

## Baseline

Issue `autrun-dirph659s868-e8df189b62` selected the `stringops` profile from
the requested `rtk ./benchmark.sh` baseline:

```text
profile                 asynkron_ms  jint_ms  delta
stringops                      1086      352  Jint 3.09x faster
```

The selected profile was a bounded standard-library/runtime slice with a clear
large gap and a compact workload:

```js
let result = "";
for (let i = 0; i < 20000; i++) {
    result += "x";
}
let upper = result.toUpperCase();
let split = result.split("");
let joined = split.join("-");
joined.length;
```

## Profile Finding

The requested CPU profile command was:

```bash
rtk ./tools/profile stringops --cpu --calltree-depth 40 --calltree-width 40
```

The relevant call tree under `ExecuteInstructionLoop` was dominated by the
compound string append path:

```text
95.73 ms 100.0% 1x TypedAstEvaluator.ExecutionPlanRunner.ExecuteInstructionLoop
|- 47.69 ms 49.8% 4x TypedAstEvaluator.ExecutionPlanRunner.HandleCompoundAssignmentSlot
|  `- 44.40 ms 46.4% 6x TypedAstEvaluator.ExecutionPlanRunner.HandleCompoundAssignmentSlotSlow
|     `- 39.45 ms 41.2% 7x TypedAstEvaluator.ExecutionPlanRunner.ProfileApplyBinaryOperator
|        `- 39.45 ms 41.2% 7x TypedAstEvaluator.ApplyBinaryOperator
|           `- 39.45 ms 41.2% 7x TypedAstEvaluator.AddValue
|              `- 37.75 ms 39.4% 6x TypedAstEvaluator.AddStringValue
|                 `- 37.75 ms 39.4% 6x JsRopeString.Concat
|                    `- 37.75 ms 39.4% 6x JsRopeString.GetString
|                       `- 37.75 ms 39.4% 6x JsRopeString.Flatten
```

The rope implementation was intended to make repeated concatenation cheap, but
the depth guard forced a flatten every 32 appends. In this profile that made
`result += "x"` repeatedly rebuild the growing string before the final
`toUpperCase`, `split`, and `join` consumers needed the flat value.

## Change

`JsRopeString` now allows a much deeper rope before forcing a flatten. Its
flatten operation already uses an explicit stack rather than recursive calls,
so repeated append loops can safely delay flattening until the string content
is actually consumed.

The existing `ProfileCompoundAdd` fast path now also handles the primitive
`string + string` case directly. That keeps `result += "x"` on the slot
compound-assignment path instead of re-entering the generic binary-operator
dispatch and `AddStringValue` path for every append.

The semantic guard remains tight: only operands already tagged as JavaScript
strings use this fast path. Objects, symbols, BigInt, and non-string primitive
coercion still go through the existing generic addition implementation.

## Final Signal

After the change, the focused selected-profile runs were:

```text
profile                 asynkron_ms  jint_ms  delta
stringops                       551      289  Jint 1.91x faster
stringops                       555      256  Jint 2.17x faster
stringops                       566      260  Jint 2.18x faster
```

The slowest final Asynkron run is 47.9% faster than the baseline
(`1086ms -> 566ms`), clearing the requested 10% threshold despite timing noise.

## Verification

Focused correctness coverage:

```bash
rtk dotnet test tests/Asynkron.JsEngine.Tests/Asynkron.JsEngine.Tests.csproj -c Release --filter "FullyQualifiedName~String_LongConcatenation_ConsumersObserveFullString" -- xUnit.MaxParallelThreads=1 -timeout 20000
```

Result:

```text
1 tests passed
```

The canonical internal quality gate is still expected to run as the normal
post-build verification step.

## Expected Impact

This should primarily help workloads that build strings through repeated
primitive string appends and then consume the final value. It also avoids a
generic operator-dispatch layer for slot compound string addition. Other
addition semantics continue through the existing paths.
