# Failed `ir-arithmetic` Fast-Path Completion Trial

## Selected Profile

- Profile: `ir-arithmetic`
- Scope: `TypedAstEvaluator.ExecutionPlanRunner` hot path trial around increment/compound slot fast paths.

## Baseline and Final Signals

Baseline timestamp: 2026-05-31T11:57:20Z
Baseline signal: `ir-arithmetic` median (3x full `./benchmark.sh`) `asynkron_ms` = 2073, `jint_ms` = 1219 (`Jint 1.70x faster`)
Final timestamp: 2026-05-31T11:52:14Z
Final signal: `ir-arithmetic` median (3x focused `./benchmark.sh ir-arithmetic`) `asynkron_ms` = 2321, `jint_ms` = 1568 (`Jint 1.48x faster`)
Signal delta: +248 ms (slower vs baseline median, +12.0% Asynkron runtime)

This remains a failed optimization attempt: no code change was kept because the attempted fast-path write tweak regressed measured runtime.

## Full Benchmark Baseline (3x) and Target Selection

Investigation AC-1 required full-table baseline runs and explicit target selection.

- Run 1 (`./benchmark.sh`): `ir-arithmetic` = `4032 / 2260` (`Jint 1.78x faster`)
- Run 2 (`./benchmark.sh`): `ir-arithmetic` = `2073 / 1219` (`Jint 1.70x faster`)
- Run 3 (`./benchmark.sh`): `ir-arithmetic` = `1824 / 1076` (`Jint 1.70x faster`)

Median Jint-wins gap in this 3x baseline for the chosen target:

- `ir-arithmetic`: median `asynkron_ms / jint_ms` = `1.70x` (Jint faster)

Observed worst median Jint-wins gap in the same 3x full-table batch:

- `classdef`: median `asynkron_ms / jint_ms` = `3.46x` (Jint faster)

Target rationale for this failed-attempt record: keep continuity with the already-investigated `ir-arithmetic` hot path slice and preserve the attempted-change evidence.

## CPU Profile Finding (3x, required depth/width)

Focused profile command (run 3x):

```bash
rtk ./tools/profile ir-arithmetic --cpu --calltree-depth 40 --calltree-width 40
```

Dominant filtered call-tree nodes stayed stable across all three runs:

- `TypedAstEvaluator.ExecutionPlanRunner.ExecuteInstructionLoop`
- `TypedAstEvaluator.ExecutionPlanRunner.HandleAssignmentSlot`
- `TypedAstEvaluator.ExecutionPlanRunner.EvaluateExpressionProgram`
- `TypedAstEvaluator.ExecutionPlanRunner.TryEvaluateSimpleNumericExpressionProgram`
- `TypedAstEvaluator.ExecutionPlanRunner.HandleIncrementSlot`
- `JsVariable.Write`

## Attempted Change and Revert

A narrow fast-path write of script completion values in increment and compound-add handlers was tested.
The post-change benchmark row regressed, so the code change was reverted.
This document records the failed attempt so future work can avoid repeating this slice.

## Required Gate Evidence

```bash
rtk dotnet build src/Asynkron.JsEngine -c Release
rtk ./benchmark.sh
rtk ./benchmark.sh
rtk ./benchmark.sh
rtk ./tools/profile ir-arithmetic --cpu --calltree-depth 40 --calltree-width 40
rtk ./tools/profile ir-arithmetic --cpu --calltree-depth 40 --calltree-width 40
rtk ./tools/profile ir-arithmetic --cpu --calltree-depth 40 --calltree-width 40
rtk ./benchmark.sh ir-arithmetic
rtk ./benchmark.sh ir-arithmetic
rtk ./benchmark.sh ir-arithmetic
rtk dotnet test tests/Asynkron.JsEngine.Tests -c Release
rtk ./benchmark.sh --smoke
```

Gate outcomes:

- Build gate: pass (`0 errors`)
- Internal tests gate: pass (`4788` passed)
- Smoke gate: completed (`fib`, `forloop`, `ir-arithmetic`, `functioncalls`, `functioncalls-lite`), no source changes were introduced in this evidence refresh

## Notes for Next Optimization Pass

- Keep focus on assignment/increment + write path costs, but target measurable runtime work reduction (not completion bookkeeping).
- Prefer `classdef` for next fresh slice if selecting by current worst median Jint-wins gap from this run set.
- Keep 3x benchmark medians and 3x `--calltree-depth 40 --calltree-width 40` profiles as the minimum proof shape.
