# Failed ir-arithmetic Fast-Path Completion Trial

## Selected Profile

- Profile: `ir-arithmetic`
- Scope: `TypedAstEvaluator.ExecutionPlanRunner` hot path trial around increment/compound slot fast paths.

## Baseline and Final Signals

Baseline timestamp: 2026-05-31T08:41:52Z
Baseline signal: ir-arithmetic asynkron_ms = 1281
Final timestamp: 2026-05-31T11:49:27Z
Final signal: ir-arithmetic asynkron_ms = 1736
Signal delta: +455 ms (slower, +35.5%)

## Profile Finding

Focused CPU profile command:

```bash
rtk ./tools/profile ir-arithmetic --cpu --calltree-depth 20 --calltree-width 20
```

Top filtered hot paths remained:

- `TypedAstEvaluator.ExecutionPlanRunner.HandleAssignmentSlot`
- `TypedAstEvaluator.ExecutionPlanRunner.EvaluateExpressionProgram`
- `TypedAstEvaluator.ExecutionPlanRunner.HandleIncrementSlot`
- `JsVariable.Write`

## Attempted Change and Revert

A narrow fast-path write of script completion values in increment and compound-add handlers was tested.
The post-change benchmark row regressed, so the code change was reverted.
This document records the failed attempt so future work can avoid repeating this slice.

## Commands Run

```bash
rtk dotnet build src/Asynkron.JsEngine -c Release
rtk ./tools/profile ir-arithmetic --cpu --calltree-depth 20 --calltree-width 20
rtk ./benchmark.sh ir-arithmetic
```

## Notes for Next Optimization Pass

- Keep focus on assignment/increment + write path costs, but target measurable runtime work reduction (not completion bookkeeping).
- Re-run with 3+ benchmark samples before keeping any code change.
