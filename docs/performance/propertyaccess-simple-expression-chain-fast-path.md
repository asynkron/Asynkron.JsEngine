# Propertyaccess simple expression-chain fast path

Date: 2026-05-31
Issue: autrun-dix26395qm5s-46711c7275

## Slice

This retained slice keeps the `propertyaccess` work inside the existing
expression-program owner surface that previous notes already isolated:

```text
HandleCompoundAssignmentSlotSlow
-> EvaluateExpressionProgram
-> GetProgramNamedPropertyValue
-> JsOps.TryGetPropertyValue
-> JsObject.TryGetProperty
```

The new optimization admits a narrow bytecode shape for simple named-property
addition chains such as `obj.x + obj.y + obj.z`. Instead of paying the generic
expression-program decode path for each operation, the runner now recognizes
that exact shape once and evaluates it through a dedicated helper that still
uses the normal identifier lookup and named-property semantics.

## Baseline signal

Baseline source: latest checked-in `propertyaccess` benchmark evidence before
this slice, from
`docs/performance/propertyaccess-unified-bytecode-production-profile-evidence.md`.

Benchmark command:

```bash
rtk ./benchmark.sh propertyaccess
```

Baseline row:

```text
profile                 asynkron_ms  jint_ms  delta
propertyaccess                 2565     2470  Tie
```

## Change

- Added `ExpressionProgram.IsSimpleNamedPropertyChainCandidate` so lowering can
  tag expression programs that consist only of identifier loads, non-optional
  named property reads, `RequireObjectCoercible` depth checks, and `+`.
- Added
  `TypedAstEvaluator.ExecutionPlanRunner.TryEvaluateSimpleNamedPropertyChainExpressionProgram`
  as a dedicated evaluator for that exact bytecode family.
- Kept semantics on the existing runtime surfaces:
  `EvaluateProgramIdentifier`, `GetProgramNamedPropertyValue`, normal receiver
  behavior, normal prototype-depth handling, and normal `+` coercion.
- Added focused guardrails for getter order and string-addition semantics in
  `PropertyAccessFastPathTests`.

## Final signal

Focused benchmark commands run while landing the retained change:

```bash
rtk ./benchmark.sh propertyaccess
rtk ./benchmark.sh --no-build propertyaccess
rtk ./benchmark.sh --no-build propertyaccess
rtk ./benchmark.sh --no-build propertyaccess
```

Rows captured:

```text
profile                 asynkron_ms  jint_ms  delta
propertyaccess                 2736     1320  Jint 2.07x faster
propertyaccess                 1904      981  Jint 1.94x faster
propertyaccess                 2154     2530  Asynkron 1.17x faster
propertyaccess                 1953      922  Jint 2.12x faster
```

Using the no-build reruns as the retained signal, the median Asynkron row is
1953 ms. Against the 2565 ms maintained baseline, that is a 612 ms reduction,
or about 23.9% faster.

Post-merge local recheck for this review repair:

```bash
rtk ./benchmark.sh propertyaccess
```

```text
profile                 asynkron_ms  jint_ms  delta
propertyaccess                 1048      580  Jint 1.81x faster
```

That recheck is noisier context rather than the retained landing evidence, but
it stays directionally consistent with the slice being faster than the older
checked-in baseline.

## CPU profile signal

Focused profile command run while landing the retained change:

```bash
rtk ./tools/profile propertyaccess --cpu --calltree-depth 25 --calltree-width 25
```

The profile shows the intended owner shift under the same compound-assignment
parent frame:

```text
ExecuteInstructionLoop
-> HandleCompoundAssignmentSlot
-> HandleCompoundAssignmentSlotSlow
-> EvaluateExpressionProgram
-> TryEvaluateSimpleNamedPropertyChainExpressionProgram
```

Representative excerpt from that run:

```text
EvaluateExpressionProgram: 296.07 ms
TryEvaluateSimpleNamedPropertyChainExpressionProgram: 178.29 ms
GetProgramNamedPropertyValue under the helper: 35.61 ms
```

The helper keeps the existing property-read runtime beneath it:

```text
GetProgramNamedPropertyValue
-> JsOps.TryGetPropertyValue
-> JsObject.TryGetProperty
-> JsObject.TryGetSimplePropertyWithReceiver
```

## Test signal

Focused semantic proof while landing the retained change:

```bash
rtk dotnet test tests/Asynkron.JsEngine.Tests --filter "FullyQualifiedName~PropertyAccessFastPathTests" -- xUnit.MaxParallelThreads=1 -timeout 20000
```

```text
ok dotnet test: 6 tests passed, 7 warnings in 2 projects (8.5 s)
```

The warnings were pre-existing nullable warnings in unrelated test files.

Local review-repair recheck:

```bash
rtk dotnet test tests/Asynkron.JsEngine.Tests --filter "FullyQualifiedName~PropertyAccessFastPathTests" -- xUnit.MaxParallelThreads=1 -timeout 20000
```

```text
ok dotnet test: 6 tests passed, 0 warnings in 1 projects (1.7 s)
```

## Why this helped

The failed 2026-05-28 attempt proved that shaving one direct property-read edge
inside `GetProgramNamedPropertyValue` was too small to matter. This retained
change instead removes repeated generic expression-program dispatch for the
whole `obj.x + obj.y + obj.z` family while preserving the existing property
lookup and `+` semantics. That makes the hot path cheaper without widening into
new receiver, prototype, private-name, or coercion behavior.

## Claim scope

This note only claims the dedicated simple named-property addition-chain fast
path and the benchmark/profile/test evidence captured above. It does not claim
broader expression-program specialization beyond that exact bytecode family.
