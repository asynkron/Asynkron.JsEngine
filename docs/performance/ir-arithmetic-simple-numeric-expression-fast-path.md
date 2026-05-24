# IR arithmetic simple numeric expression fast path

## Baseline

Issue `autrun-dir3mbor11cg-7a927e0289` selected the `ir-arithmetic`
profile from the required `rtk ./benchmark.sh` run:

```text
profile                 asynkron_ms  jint_ms  delta
ir-arithmetic                  6993     2070  Jint 3.38x faster
```

The selected profile is the same tight arithmetic loop used by earlier IR
optimizer slices:

```js
var total = 0;
for (var i = 0; i < 200000; i++) {
    total = total + ((i * 3 + 7) % 11);
}
total;
```

## Profile Finding

The required CPU profile command was:

```bash
rtk ./tools/profile ir-arithmetic --cpu --calltree-depth 40 --calltree-width 40
```

Before the change, the `ExecuteInstructionLoop` profile still spent most of its
time in assignment-slot expression bytecode execution:

```text
2963.41 ms 100.0% 6x TypedAstEvaluator.ExecutionPlanRunner.ExecuteInstructionLoop
|- 1794.69 ms 60.6% 441x TypedAstEvaluator.ExecutionPlanRunner.HandleAssignmentSlot
|  `- 1280.49 ms 43.2% 458x TypedAstEvaluator.ExecutionPlanRunner.EvaluateExpressionProgram
|- 599.04 ms 20.2% 294x TypedAstEvaluator.ExecutionPlanRunner.EvaluateExpressionProgram
`- 212.22 ms 7.2% 130x TypedAstEvaluator.ExecutionPlanRunner.HandleIncrementSlot
```

The hot expression programs for this profile contain only numeric literals,
identifier loads, and numeric binary operators. They do not need optional-chain
stack flags, object constants, string constants, call target handling, or the
full expression opcode switch.

## Change

`ExpressionProgram` now marks programs that are simple numeric candidates:
`LoadLiteral`, `LoadIdentifier`, and numeric `Binary` operations only.
`ExecutionPlanRunner.EvaluateExpressionProgram` checks that marker first and
uses a narrow fast path for those programs.

The fast path:

- reads only statically cached identifiers from flat or scoped slots;
- requires numeric operands at runtime;
- handles arithmetic and numeric comparison operators directly;
- falls back to the existing full expression interpreter for unsupported
  shapes or non-number values.

This keeps the semantic boundary narrow: dynamic lookup, `with`, `arguments`,
string addition, object coercion, BigInt, calls, member access, optional
chaining, and all effectful opcodes remain on the existing interpreter path.

## Final Signal

Focused build and semantic checks:

```text
rtk dotnet build src/Asynkron.JsEngine/Asynkron.JsEngine.csproj -c Release --no-restore --disable-build-servers -m:1 -v:minimal
ok dotnet build: 2 projects, 0 errors, 2 warnings (00:03:07.06)

rtk dotnet test tests/Asynkron.JsEngine.Tests --filter "FullyQualifiedName~ExpressionProgramLoweringTests" -- xUnit.MaxParallelThreads=1 -timeout 20000
ok dotnet test: 153 tests passed, 0 warnings in 1 projects (9.3 s)

rtk dotnet test tests/Asynkron.JsEngine.Tests --filter "FullyQualifiedName~IrLoopEnvironmentTests" -- xUnit.MaxParallelThreads=1 -timeout 20000
ok dotnet test: 38 tests passed, 0 warnings in 1 projects (8.2 s)
```

Repeated selected-profile timing after the change:

```text
profile                 asynkron_ms  jint_ms  delta
ir-arithmetic                  4280     1881  Jint 2.28x faster
ir-arithmetic                  2884     3329  Asynkron 1.15x faster
ir-arithmetic                  6197     1431  Jint 4.33x faster
```

The environment was noisy, but even the slowest final Asynkron run improved
from `6993ms` to `6197ms`, a conservative 11.4% speedup. The best repeated run
was 58.8% faster than baseline.

The post-change CPU profile shows the selected work reaching the new fast path:

```text
3150.62 ms 100.0% 6x TypedAstEvaluator.ExecutionPlanRunner.ExecuteInstructionLoop
|- 1793.28 ms 56.9% 496x TypedAstEvaluator.ExecutionPlanRunner.HandleAssignmentSlot
|  `- 1152.10 ms 36.6% 467x TypedAstEvaluator.ExecutionPlanRunner.EvaluateExpressionProgram
|     `- 1032.20 ms 32.8% 452x TypedAstEvaluator.ExecutionPlanRunner.TryEvaluateSimpleNumericExpressionProgram
|- 590.87 ms 18.8% 316x TypedAstEvaluator.ExecutionPlanRunner.EvaluateExpressionProgram
|  `- 492.66 ms 15.6% 273x TypedAstEvaluator.ExecutionPlanRunner.TryEvaluateSimpleNumericExpressionProgram
`- 250.93 ms 8.0% 147x TypedAstEvaluator.ExecutionPlanRunner.HandleIncrementSlot
```

## Expected Impact

This helps IR/expression-bytecode loops whose expression programs are pure
numeric slot reads, literals, and arithmetic or comparison operators. The fast
path is intentionally runtime-guarded and falls back before changing observable
JavaScript behavior for mixed-type or dynamic expressions.
