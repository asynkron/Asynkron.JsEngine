# Propertyaccess named-read production routing

Date: 2026-05-27
Issue: autrun-dita1u7282fk-c0402b3866

## Slice

This run widened production unified-bytecode routing for named property reads:

- activation-resolved nested chains such as `box.a.b.c.d.e`
- supported binary expressions composed from activation-resolved named reads such as `box.x + box.y + box.z`

The boundary still declines optional chains, property writes/updates, private
property handling through this path, computed reads outside the existing first
computed-read boundary, and property reads whose receiver is not an
activation-resolved named-read chain.

## Baseline signal

Command:

```bash
rtk ./benchmark.sh propertyaccess
```

Row captured before editing:

```text
profile                 asynkron_ms  jint_ms  delta
propertyaccess                 1042      482  Jint 2.16x faster
```

## Final signal

Commands:

```bash
rtk dotnet test tests/Asynkron.JsEngine.Tests --filter "FullyQualifiedName~UnifiedBytecodeProductionEligibilityTests.Evaluate_NestedNamedPropertyReadCandidate_AcceptsPropertyOpcodeChain|FullyQualifiedName~UnifiedBytecodeProductionEligibilityTests.Evaluate_NamedPropertyReadBinaryCandidate_AcceptsPropertyOpcodeChains|FullyQualifiedName~UnifiedBytecodeProductionEligibilityTests.Evaluate_PropertyReadAdjacentFamilies_DeclineWithExplicitCodes|FullyQualifiedName~UnifiedBytecodeProductionInvocationTests.NestedNamedPropertyRead_UsesUnifiedBytecodeProductionFastPathAndGetterSemantics|FullyQualifiedName~UnifiedBytecodeProductionInvocationTests.NamedPropertyReadBinaryExpression_UsesUnifiedBytecodeProductionFastPath|FullyQualifiedName~UnifiedBytecodeProductionInvocationTests.ComputedPropertyRead_UsesUnifiedBytecodeProductionFastPathAndToPropertyKeySemantics" -- xUnit.MaxParallelThreads=1 -timeout 20000
rtk ./benchmark.sh propertyaccess
rtk ./benchmark.sh --no-build propertyaccess
rtk ./benchmark.sh --no-build propertyaccess
rtk ./benchmark.sh --no-build propertyaccess
```

Focused tests:

```text
16 tests passed
```

Benchmark rows:

```text
profile                 asynkron_ms  jint_ms  delta
propertyaccess                 1081      485  Jint 2.23x faster
propertyaccess                  967      473  Jint 2.04x faster
propertyaccess                  974      465  Jint 2.09x faster
propertyaccess                  969      504  Jint 1.92x faster
```

## Interpretation

The code slice is retained because it moves the explicit production routing
boundary forward and proves runtime semantics for nested getters and direct
property-read binary expressions.

The `propertyaccess` benchmark remains noisy and did not produce a stable 10%+
improvement in this run. The best captured row improved from 1042 ms to 967 ms,
and the final no-build confirmation was 969 ms. The repeated rows ranged from a
small gain to a small regression. Do not use this note as a benchmark-win claim.
