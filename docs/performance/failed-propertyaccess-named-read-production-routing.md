# Failed propertyaccess named-read production routing

Date: 2026-05-27
Issue: autrun-dita1u7282fk-c0402b3866

## Slice

This run tried to widen production unified-bytecode routing for named property
reads:

- activation-resolved nested chains such as `box.a.b.c.d.e`
- supported binary expressions composed from activation-resolved named reads,
  such as `box.x + box.y + box.z`

The implementation was initially added, but the retained evidence did not meet
the issue's performance gate. The runtime and focused test edits were reverted.
No benchmark-win runtime change is retained by this note.

## Baseline signal

The required full `rtk ./benchmark.sh` table was not captured before the initial
implementation. The only recorded baseline row was a focused propertyaccess run:

```bash
rtk ./benchmark.sh propertyaccess
```

```text
profile                 asynkron_ms  jint_ms  delta
propertyaccess                 1042      482  Jint 2.16x faster
```

CPU profile evidence was also required before and after the attempted change:

```bash
rtk ./tools/profile propertyaccess --cpu --calltree-depth 40 --calltree-width 40
```

No before/after CPU profile output was captured for the retained attempt, so the
run cannot support a profile-backed performance claim.

## Final signal from the reverted attempt

The initial implementation ran focused tests and repeated the selected
benchmark:

```bash
rtk dotnet test tests/Asynkron.JsEngine.Tests --filter "FullyQualifiedName~UnifiedBytecodeProductionEligibilityTests.Evaluate_NestedNamedPropertyReadCandidate_AcceptsPropertyOpcodeChain|FullyQualifiedName~UnifiedBytecodeProductionEligibilityTests.Evaluate_NamedPropertyReadBinaryCandidate_AcceptsPropertyOpcodeChains|FullyQualifiedName~UnifiedBytecodeProductionEligibilityTests.Evaluate_PropertyReadAdjacentFamilies_DeclineWithExplicitCodes|FullyQualifiedName~UnifiedBytecodeProductionInvocationTests.NestedNamedPropertyRead_UsesUnifiedBytecodeProductionFastPathAndGetterSemantics|FullyQualifiedName~UnifiedBytecodeProductionInvocationTests.NamedPropertyReadBinaryExpression_UsesUnifiedBytecodeProductionFastPath|FullyQualifiedName~UnifiedBytecodeProductionInvocationTests.ComputedPropertyRead_UsesUnifiedBytecodeProductionFastPathAndToPropertyKeySemantics" -- xUnit.MaxParallelThreads=1 -timeout 20000
rtk ./benchmark.sh propertyaccess
rtk ./benchmark.sh --no-build propertyaccess
rtk ./benchmark.sh --no-build propertyaccess
rtk ./benchmark.sh --no-build propertyaccess
```

Focused tests passed in that attempt:

```text
16 tests passed
```

Repeated benchmark rows from the reverted attempt:

```text
profile                 asynkron_ms  jint_ms  delta
propertyaccess                 1081      485  Jint 2.23x faster
propertyaccess                  967      473  Jint 2.04x faster
propertyaccess                  974      465  Jint 2.09x faster
propertyaccess                  969      504  Jint 1.92x faster
```

## Interpretation

The result was noisy and did not show the required repeatable 10%+ Asynkron-side
improvement. The best recorded row improved from 1042 ms to 967 ms, which is
about 7.2%, while another row regressed to 1081 ms. Because the performance gate
was unmet and CPU profile evidence was missing, the runtime/test changes were
reverted and this note records the attempt as failed evidence.

Future work should rerun the full benchmark table first, capture the required
propertyaccess CPU profile before editing, and only retain a runtime change when
the repeated final rows clear the 10%+ gate beyond noise.
