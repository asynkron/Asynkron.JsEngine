# Symbol property-access owner evidence

Date: 2026-05-31
Issue: gh2829

## Scope

This slice adds a focused `symbol-propertyaccess` ProfileRunner workload and
captures baseline/current CPU+memory evidence for Symbol-key property access.

The goal is owner identification for the next allocation cut, not a retained
runtime optimization in this issue.

## Workload

Added profile key:

- `symbol-propertyaccess` -> `tools/profile-scripts/symbol-propertyaccess.js`

The script repeatedly performs:

- Symbol-key writes and reads (`obj[s1]`, `obj[s2]`, `obj[s3]`, `obj[s4]`)
- `Object.getOwnPropertySymbols(obj)` enumeration in-loop
- `Object.getOwnPropertyDescriptor(obj, s3)` descriptor retrieval

## Commands Run

```bash
rtk ./tools/profile symbol-propertyaccess --cpu --calltree-depth 40 --calltree-width 40
rtk ./tools/profile symbol-propertyaccess --memory --calltree-depth 40 --calltree-width 40
rtk ./benchmark.sh --allocations symbol-propertyaccess
```

## Baseline and Final Signals

Baseline timestamp: 2026-05-31T12:07:19Z
Baseline signal: benchmark --allocations symbol-propertyaccess asynkron_ms = 11184 ms, asynkron_kb = 258936.7 KB
Final timestamp: 2026-05-31T12:08:17Z
Final signal: benchmark --allocations symbol-propertyaccess asynkron_ms = 6490 ms, asynkron_kb = 258960.8 KB
Signal delta: time -4694 ms (faster), allocation +24.1 KB (flat/noise-level)

CPU profile snapshots (filtered top frame excerpt):

- Baseline run: `EvaluateExpressionProgram` 14294.63 ms, `HandleEvaluateAndDiscard` 6913.21 ms, `HandleCompoundAssignmentSlot` 5337.71 ms, `ObjectConstructor.GetOwnPropertySymbols` 1276.45 ms
- Final run: `EvaluateExpressionProgram` 8703.25 ms, `HandleEvaluateAndDiscard` 4103.85 ms, `HandleCompoundAssignmentSlot` 3264.46 ms, `ObjectConstructor.GetOwnPropertySymbols` 900.72 ms

Memory profile snapshots:

- Baseline run: `Total allocated 278.28 MB`, `String 260.49 MB`, `PropertyDescriptor 6.19 MB`
- Final run: `Total allocated 278.89 MB`, `String 262.73 MB`, `PropertyDescriptor 6.09 MB`

## Owner Readout

Observed owner surfaces in this workload:

- `TypedAstEvaluator.ExecutionPlanRunner.EvaluateExpressionProgram`
- `AssignmentReferenceResolver.AssignObjectProperty` / `JsObject.GetOwnPropertyDescriptor` via computed symbol assignment path
- `ObjectConstructor.GetOwnPropertySymbols` and own-key materialization path

Not observed as a runtime owner:

- `SymbolHybridDictionary<TValue>` (bounded search still shows no runtime consumer in this path)

## Next bounded optimization slice

Target the computed symbol assignment path in `JsObject`:

- avoid allocating a fresh `PropertyDescriptor` for the common writable data
  descriptor update case on `obj[symbol] = value` when descriptor shape is
  already known and unchanged.

Why this slice:

- Memory call tree repeatedly attributes sampled `PropertyDescriptor` allocation
  to `JsObject.GetOwnPropertyDescriptor` in the symbol computed-assignment path.
- It is owner-local to `JsObject`/assignment handling and does not require
  speculative changes to currently unreferenced `SymbolHybridDictionary<TValue>`.

## Notes

- `tools/profile-output/` was treated as generated profiler output.
- This issue intentionally records evidence and the next optimization target;
  it does not claim a retained global runtime speedup.
