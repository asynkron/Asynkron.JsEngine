# Propertyaccess unified-bytecode property-read production evidence (#2313)

Date: 2026-05-27
Issue: #2313

## Canonical workload surface

- Manifest entry: `tools/profile-manifest.json` -> `propertyaccess`
- Script: `tools/profile-scripts/propertyaccess.js`
- Workload shape: repeated direct and nested property reads in `sum += ...` loops.

## Baseline signal

Baseline source: checked-in row from `docs/performance/propertyaccess-compound-add-fast-path.md` (Date: 2026-05-26).

Command used for that historical row:

```bash
rtk ./benchmark.sh
```

Historical baseline row:

```text
propertyaccess  1735 ms  576 ms  Jint 3.01x faster
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
propertyaccess                 1012      505  Jint 2.00x faster
```

```text
profile                 asynkron_ms    asynkron_kb  jint_ms     jint_kb  time_delta             alloc_delta
propertyaccess                 1035          290.6      477     87298.2  Jint 2.17x faster      Asynkron 300.43x lower alloc
```

CPU profile command and key excerpt:

```bash
rtk ./tools/profile propertyaccess --cpu --calltree-depth 40 --calltree-width 40
```

```text
Call Tree (Total Time) - root: ExecuteInstructionLoop
... -> HandleCompoundAssignmentSlotSlow -> EvaluateExpressionProgram
... -> GetProgramNamedPropertyValue -> JsOps.TryGetPropertyValue -> JsObject.TryGetProperty*
```

## Production-boundary interpretation

Accepted first-boundary property-read shapes (eligible for production unified-bytecode routing):

- Direct named property read from an activation-resolved base (`return box.value;`).
- Exact two-hop named property read from an activation-resolved base (`return box.child.value;`).
- First-boundary computed property read shape using `RequireObjectCoercible(Depth: 1)` and `ResolvePropertyKey` immediately before `GetComputedProperty` (`return box[key];`).

Current production program boundary for accepted property-read routing:

- Accepted opcodes in these programs: `LoadSlot`, `LoadLiteral`, `StoreSlot`, supported `Binary` subset, `RequireObjectCoercible`, `ResolvePropertyKey`, `GetNamedProperty`, `GetComputedProperty`, `Jump`, `JumpIfFalse`, and `Return`.
- Property-read operand ownership:
  - `LoadSlot(slotIndex)` loads activation-resolved base/key slots.
  - `LoadLiteral(literalIndex)` loads allowed literal keys for computed reads.
  - `GetNamedProperty(stringConstantIndex)` resolves property names through `UnifiedBytecodeProgram.StringConstants`.
  - `RequireObjectCoercible(Depth: 1)` checks the base operand before computed-key coercion.
  - `ResolvePropertyKey` and `GetComputedProperty` use stack operands only (no instruction operand).

Accepted source-shape summary:

- `box.value`
- `box.child.value`
- `box[key]` where `box` is activation-resolved and `key` is activation-resolved identifier or supported literal.

Primary pre-VM decline families (decline before production VM execution):

- Property reads outside the first boundary (for example computed keys such as `left + right`).
- Optional chaining (`box?.value`, `box?.[key]`).
- Computed-in-chain shapes (`box[key].value`, `box.child[key]`) and richer computed-key expressions.
- Property writes and updates (`box.value = ...`, `box.value++`).
- `delete` and `super` property access.
- Call/construct shapes, dynamic lookup, `arguments`, `this`, `new.target`, async/generator activation, captured/dynamic activation, and object literal/spread adjacency.

No-mixed-execution constraint for accepted programs:

- Accepted production property-read programs execute through owned unified VM opcodes only.
- They do not bridge to `ExpressionProgram`, `ExecutionPlanRunner`, or AST evaluation callbacks.

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
