# IR arithmetic self-assignment compound slot lowering

## Baseline

Issue `autrun-dir08v6q4vag-367d2e753a` selected the `ir-arithmetic`
profile from the requested `rtk ./benchmark.sh` baseline:

```text
profile                 asynkron_ms  jint_ms  delta
ir-arithmetic                  4810     2009  Jint 2.39x faster
```

The selected benchmark is intentionally small and maps directly to expression
bytecode assignment work:

```js
var total = 0;
for (var i = 0; i < 200000; i++) {
    total = total + ((i * 3 + 7) % 11);
}
total;
```

## Profile Finding

The requested CPU profile command was:

```bash
rtk ./tools/profile ir-arithmetic --cpu --calltree-depth 40 --calltree-width 40
```

Before the change, the profile under `ExecuteInstructionLoop` was dominated by
plain assignment statements re-evaluating the whole right-hand expression
program:

```text
7494.93 ms 100.0% 9x TypedAstEvaluator.ExecutionPlanRunner.ExecuteInstructionLoop
|- 4684.74 ms 62.5% 773x TypedAstEvaluator.ExecutionPlanRunner.HandleAssignmentSlot
|  `- 3427.20 ms 45.7% 806x TypedAstEvaluator.ExecutionPlanRunner.EvaluateExpressionProgram
|     `- 641.92 ms 8.6% 263x TypedAstEvaluator.ExecutionPlanRunner.ApplyProgramBinaryOperator
|- 1323.05 ms 17.7% 493x TypedAstEvaluator.ExecutionPlanRunner.EvaluateExpressionProgram
`- 490.61 ms 6.5% 204x TypedAstEvaluator.ExecutionPlanRunner.HandleIncrementSlot
```

The hot statement shape was `total = total + rhs`. This is semantically the
same as a compound assignment for simple identifier targets, and the runtime
already has a dedicated `CompoundAssignmentSlotInstruction` path that reads the
target slot once, evaluates only the right-hand operand, applies the operator,
and writes the result back.

## Change

`ExpressionStatementEmitter` now recognizes self-referential arithmetic
assignments shaped as `x = x <op> rhs` and emits the existing
`CompoundAssignmentSlotInstruction` instead of a generic `AssignmentSlotInstruction`
whose value program includes both `x` and the binary operation.

The rewrite is limited to simple identifier targets and arithmetic or bitwise
operators already supported by compound-slot lowering. It preserves the
existing awaited-right-hand-side path and still routes unsupported or failed
expression-program compilation through the existing fallback/failure behavior.

## Final Signal

Focused correctness guardrails:

```text
rtk dotnet test tests/Asynkron.JsEngine.Tests --filter "FullyQualifiedName~SelfReferentialArithmeticAssignment|FullyQualifiedName~SimpleAssignmentExpression_UsesAssignmentInstruction|FullyQualifiedName~ScriptExpressionStatement_MutableIdentifierAssignment_UsesAssignmentSlotInstruction"
ok dotnet test: 3 tests passed
```

Repeated selected-profile timing after the change:

```text
profile                 asynkron_ms  jint_ms  delta
ir-arithmetic                  2700     1509  Jint 1.79x faster
ir-arithmetic                  2720     1743  Jint 1.56x faster
```

Compared with the baseline `4810ms`, the repeated final Asynkron timings are
43.9% and 43.5% faster.

The post-change CPU profile shows the selected statement now routes through the
compound-slot path and evaluates a smaller expression program:

```text
3406.30 ms 100.0% 6x TypedAstEvaluator.ExecutionPlanRunner.ExecuteInstructionLoop
|- 1721.41 ms 50.5% 381x TypedAstEvaluator.ExecutionPlanRunner.HandleCompoundAssignmentSlotSlow
|  `- 1096.06 ms 32.2% 351x TypedAstEvaluator.ExecutionPlanRunner.EvaluateExpressionProgram
|- 746.50 ms 21.9% 295x TypedAstEvaluator.ExecutionPlanRunner.EvaluateExpressionProgram
`- 236.68 ms 6.9% 119x TypedAstEvaluator.ExecutionPlanRunner.HandleIncrementSlot
```

## Expected Impact

This helps loops and straight-line IR code that spell accumulation as
`x = x + rhs`, `x = x * rhs`, or another supported arithmetic/bitwise operator
instead of source-level compound assignment. The change does not alter property
assignments, destructuring assignments, logical assignments, non-identifier
targets, or generic expression bytecode execution.
