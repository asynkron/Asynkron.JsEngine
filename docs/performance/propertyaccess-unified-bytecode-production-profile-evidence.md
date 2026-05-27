# Propertyaccess unified-bytecode property-read production evidence refresh (#2340)

Date: 2026-05-27
Issue: #2340

## Canonical workload surface

- Manifest entry: `tools/profile-manifest.json` -> `propertyaccess`
- Script: `tools/profile-scripts/propertyaccess.js`
- Workload shape: repeated direct and nested property reads in `sum += ...` loops.

## Baseline signal

Baseline source: prior checked-in rows from this evidence surface (issue #2313, Date: 2026-05-27).

Benchmark commands used for the prior checked-in rows:

```bash
rtk ./benchmark.sh propertyaccess
rtk ./benchmark.sh --allocations propertyaccess
```

Historical baseline rows:

```text
profile                 asynkron_ms  jint_ms  delta
propertyaccess                 1012      505  Jint 2.00x faster
```

```text
profile                 asynkron_ms    asynkron_kb  jint_ms     jint_kb  time_delta             alloc_delta
propertyaccess                 1035          290.6      477     87298.2  Jint 2.17x faster      Asynkron 300.43x lower alloc
```

## Final signal (current run)

Benchmark commands run in this worktree:

```bash
rtk ./benchmark.sh propertyaccess
rtk ./benchmark.sh --allocations propertyaccess
```

Rows captured:

```text
profile                 asynkron_ms  jint_ms  delta
propertyaccess                 2565     2470  Tie
```

```text
profile                 asynkron_ms    asynkron_kb  jint_ms     jint_kb  time_delta             alloc_delta
propertyaccess                 2566          302.6     2195     87285.7  Jint 1.17x faster      Asynkron 288.47x lower alloc
```

Before/after comparison against prior checked-in rows:

- Time row: Asynkron `1012 -> 2565 ms` (+1553 ms), Jint `505 -> 2470 ms` (+1965 ms), delta `Jint 2.00x faster -> Tie`.
- Allocation row: Asynkron `290.6 -> 302.6 KB` (+12.0 KB), Jint `87298.2 -> 87285.7 KB` (-12.5 KB), allocation delta `Asynkron 300.43x lower -> Asynkron 288.47x lower`.

CPU profile command and key excerpt:

```bash
rtk ./tools/profile propertyaccess --cpu --calltree-depth 40 --calltree-width 40
```

```text
Call Tree (Total Time) - root: ExecuteInstructionLoop
... -> HandleCompoundAssignmentSlot -> HandleCompoundAssignmentSlotSlow -> EvaluateExpressionProgram
... -> EvaluateExpressionProgram -> GetProgramNamedPropertyValue -> JsOps.TryGetPropertyValue -> JsObject.TryGetProperty*
```

## Production-boundary interpretation

Accepted first-boundary property-read shapes (eligible for production unified-bytecode routing):

- Direct named property read from an activation-resolved base (`return box.value;`).
- Exact two-hop named property-read chains where both hops are named and non-optional.
- Exact first-boundary computed property read shape using `RequireObjectCoercible(Depth: 1)` and `ResolvePropertyKey` immediately before `GetComputedProperty` (`return box[key];`).

Primary pre-VM decline families (decline before production VM execution):

- Property reads outside the first boundary (for example non-exact computed/read chains such as `left + right` or deeper mixed chains).
- Optional chaining (`box?.value`, `box?.[key]`).
- Property writes and updates (`box.value = ...`, `box.value++`).
- `delete` and `super` property access.
- Call/construct shapes, dynamic lookup, `arguments`, `this`, `new.target`, async/generator activation, and captured/dynamic activation.

Reference surfaces:

- `src/Asynkron.JsEngine/Execution/UnifiedBytecode/UnifiedBytecodeProductionEligibility.cs`
- `tests/Asynkron.JsEngine.Tests/UnifiedBytecodeProductionEligibilityTests.cs`

## Claim scope

This note records measured benchmark/profile rows and routing constraints only.
It does not claim Node.js parity or broad runtime wins beyond these captured rows.

## Batch-5 property-read boundary proof pack (issue child run)

Run timestamp (UTC): 2026-05-27T08:30:58Z
Issue: `planitem-planmanual1779860498694736000-batch-1-property-read-boundary-batch-5-evi-25c34c1968`

Commands and outcome:

```bash
rtk dotnet test tests/Asynkron.JsEngine.Tests --filter "FullyQualifiedName~UnifiedBytecodeProductionEligibilityTests&FullyQualifiedName~PropertyRead"
```

Result: `18 tests passed`.

```bash
rtk dotnet test tests/Asynkron.JsEngine.Tests --filter "FullyQualifiedName~UnifiedBytecodeProductionInvocationTests&(FullyQualifiedName~PropertyRead|FullyQualifiedName~IndexedReads)"
```

Result: `18 tests passed`.

```bash
rtk dotnet test tests/Asynkron.JsEngine.Tests --filter "FullyQualifiedName~UnifiedBytecodeProductionEligibilityTests|FullyQualifiedName~UnifiedBytecodeProductionInvocationTests"
```

Result: `89 tests passed`.

```bash
rtk rg "EvaluateExpression\\(|ProfileEvaluateExpression\\(" src/Asynkron.JsEngine/Ast/TypedAstEvaluator.ExecutionPlanRunner*
```

Result: no matches.

```bash
rtk ./tools/profile forloop --memory
```

Result excerpt:

```text
Metric          Value
Total allocated 6.80 MB
```

Allocation interpretation for this evidence run: allocation-stable for the route under this proof pack, with no new allocator regression signal in the captured output.
