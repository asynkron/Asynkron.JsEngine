# Propertyaccess unified-bytecode production boundary evidence refresh (#2340, #2367)

Date: 2026-05-27
Issue: #2340

## Canonical workload surface

- Manifest entry: `tools/profile-manifest.json` -> `propertyaccess`
- Script: `tools/profile-scripts/propertyaccess.js`
- Workload shape: repeated direct and nested property access in arithmetic loops.

## Baseline signal

Baseline source: prior checked-in rows from this evidence surface.

Benchmark commands for baseline rows:

```bash
rtk ./benchmark.sh propertyaccess
rtk ./benchmark.sh --allocations propertyaccess
```

Historical baseline rows:

```text
profile                 asynkron_ms  jint_ms  delta
propertyaccess                 2565     2470  Tie
```

```text
profile                 asynkron_ms    asynkron_kb  jint_ms     jint_kb  time_delta             alloc_delta
propertyaccess                 2566          302.6     2195     87285.7  Jint 1.17x faster      Asynkron 288.47x lower alloc
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
propertyaccess                  927      492  Jint 1.88x faster
```

```text
profile                 asynkron_ms    asynkron_kb  jint_ms     jint_kb  time_delta             alloc_delta
propertyaccess                  881          283.2      520     87298.0  Jint 1.69x faster      Asynkron 308.27x lower alloc
```

Comparison vs baseline rows:

- Time row: Asynkron `2565 -> 927 ms` (-1638 ms), Jint `2470 -> 492 ms` (-1978 ms), delta `Tie -> Jint 1.88x faster`.
- Allocation row: Asynkron `302.6 -> 283.2 KB` (-19.4 KB), Jint `87285.7 -> 87298.0 KB` (+12.3 KB), allocation delta `Asynkron 288.47x lower -> Asynkron 308.27x lower`.

CPU profile command and key excerpt:

```bash
rtk ./tools/profile propertyaccess --cpu --calltree-depth 40 --calltree-width 40
```

```text
Call Tree (Total Time) - root: ExecuteInstructionLoop
... -> HandleCompoundAssignmentSlot -> HandleCompoundAssignmentSlotSlow -> EvaluateExpressionProgram
... -> EvaluateExpressionProgram -> GetProgramNamedPropertyValue -> JsOps.TryGetPropertyValue -> JsObject.TryGetProperty*
```

## Accepted production boundary

Accepted first-boundary source shapes (selector-owned):

- Direct named read: `return box.value;`
- Exact two-hop named read: `return box.child.value;`
- Direct computed read: `return box[key];`
- Direct named write: `return box.value = value;`
- Direct computed write: `return box[key] = value;`
- Direct named compound write: `return box.value += value;`
- Direct computed compound write: `return box[key] += value;`
- Direct named prefix/postfix update: `return ++box.value;` / `return box.value++;`
- Direct computed prefix/postfix update: `return ++box[key];` / `return box[key]++;`

Current accepted opcode set from `UnifiedBytecodeProgram.cs`:

- `LoadSlot`
- `LoadLiteral`
- `StoreSlot`
- `Binary`
- `RequireObjectCoercible`
- `ResolvePropertyKey`
- `GetNamedProperty`
- `GetComputedProperty`
- `GetNamedPropertyForCompoundSet`
- `GetComputedPropertyForCompoundSet`
- `SetNamedProperty`
- `SetComputedProperty`
- `UpdateNamedProperty`
- `UpdateComputedProperty`
- `Jump`
- `JumpIfFalse`
- `Return`

No-mixed-execution constraint:

- Accepted programs execute through owned unified VM opcodes only.
- Accepted programs do not bridge to `ExpressionProgram`, `ExecutionPlanRunner`, or AST evaluation callbacks.

Primary pre-VM declines that remain out of scope:

- Logical assignment (`&&=`, `||=`, `??=`), nested/complex member chains, richer computed-key expressions.
- Optional chaining, `super`, `delete`, private fields.
- Call/construct shapes, dynamic/captured activation, `arguments`, `this`, `new.target`, destructuring, object literal/spread adjacency.

## #2367 accounting

A Faktorial-supported numeric issue lookup for `#2367` is currently unavailable in this runtime:

```bash
rtk curl -fsS "${FAKTORIAL_URL:-http://127.0.0.1:8787}/api/issues/2367"
```

Result: HTTP `400`.

Because direct status lookup is unavailable, this evidence surface records the local, reproducible benchmark/profile and proof-command signals only, and treats `#2367` as an external status dependency for planner/review tracking.

## Focused proof commands

```bash
rtk dotnet test tests/Asynkron.JsEngine.Tests --filter "FullyQualifiedName~UnifiedBytecodeProductionEligibilityTests&(FullyQualifiedName~PropertyRead|FullyQualifiedName~PropertyWrite|FullyQualifiedName~PropertyUpdate|FullyQualifiedName~Compound)"
```

Result: `42 tests passed`.

```bash
rtk dotnet test tests/Asynkron.JsEngine.Tests --filter "FullyQualifiedName~UnifiedBytecodeProductionInvocationTests&(FullyQualifiedName~PropertyRead|FullyQualifiedName~PropertyWrite|FullyQualifiedName~PropertyUpdate|FullyQualifiedName~Compound|FullyQualifiedName~IndexedReads)"
```

Result: `35 tests passed`.

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
Total allocated 6.75 MB
```

```bash
rtk git diff --check
```

Result: clean after this doc refresh.

## Claim scope

This note records measured rows and route constraints only.
It does not claim broad Node.js parity or generalized property-access wins beyond the accepted boundary and captured rows.
