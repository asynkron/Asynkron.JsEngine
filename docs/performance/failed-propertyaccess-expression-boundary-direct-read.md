# Failed propertyaccess expression-boundary direct read

Date: 2026-05-28
Issue: autrun-diu8sjuliufk-8777abed24

## Slice

This run targeted the `propertyaccess` profile's named property reads under
compound-add expression-program execution:

```text
HandleCompoundAssignmentSlotSlow
-> EvaluateExpressionProgram
-> GetProgramNamedPropertyValue
-> JsOps.TryGetPropertyValue
-> JsObject.TryGetProperty
-> JsObject.TryGetSimplePropertyWithReceiver
```

The attempted optimization added a direct expression-boundary fast path for
non-private `JsObject` data-property reads. The idea was to skip the generic
`JsOps.TryGetPropertyValue` dispatch and the repeated private-slot check when
`GetProgramNamedPropertyValue` had already proven that the property name was
not private.

No runtime change is retained. Focused semantic tests passed, but repeated
timing rows did not improve over the focused baseline.

## Baseline signal

Baseline timestamp: 2026-05-28T10:45:00Z

Full pre-edit benchmark table:

```bash
rtk ./benchmark.sh
```

Selected row:

```text
profile                 asynkron_ms  jint_ms  delta
propertyaccess                  946      538  Jint 1.76x faster
```

Focused pre-edit benchmark:

```bash
rtk ./benchmark.sh propertyaccess
```

```text
profile                 asynkron_ms  jint_ms  delta
propertyaccess                  914      501  Jint 1.82x faster
```

Required pre-edit CPU profile command, run three times:

```bash
rtk ./tools/profile propertyaccess --cpu --calltree-depth 40 --calltree-width 40
```

Representative profile root timings:

```text
ExecuteInstructionLoop: 232.87 ms
HandleCompoundAssignmentSlotSlow: 151.19 ms
EvaluateExpressionProgram under compound assignment: 111.25 ms
GetProgramNamedPropertyValue under compound assignment: 36.15 ms
```

```text
ExecuteInstructionLoop: 243.52 ms
HandleCompoundAssignmentSlotSlow: 171.88 ms
EvaluateExpressionProgram under compound assignment: 131.12 ms
GetProgramNamedPropertyValue under compound assignment: 38.59 ms
```

```text
ExecuteInstructionLoop: 248.90 ms
HandleCompoundAssignmentSlotSlow: 169.82 ms
EvaluateExpressionProgram under compound assignment: 132.27 ms
GetProgramNamedPropertyValue under compound assignment: 34.53 ms
```

## Attempted change

The reverted change added an internal `JsObject` helper for known non-private
simple data-property reads and called it from `GetProgramNamedPropertyValue`
before falling back to `JsOps.TryGetPropertyValue`.

The helper only returned true for own stored data properties when no virtual
provider or own descriptor was present. Descriptors, getters, prototype reads,
primitives, proxies, and private names all stayed on the existing fallback path.

Focused semantic guardrails passed while the change was present:

```bash
rtk dotnet test tests/Asynkron.JsEngine.Tests --filter "FullyQualifiedName~PropertyAccessFastPathTests|FullyQualifiedName~UnifiedBytecodeProductionInvocationTests|FullyQualifiedName~UnifiedBytecodeProductionEligibilityTests" -- xUnit.MaxParallelThreads=1 -timeout 20000
```

```text
ok dotnet test: 200 tests passed, 7 warnings in 2 projects (2.4 s)
```

Warnings were existing nullable warnings in unrelated test files.

## Final signal from reverted attempt

Final timestamp: 2026-05-28T10:51:11Z

Repeated focused benchmark rows with the attempted change:

```bash
rtk ./benchmark.sh propertyaccess
rtk ./benchmark.sh --no-build propertyaccess
rtk ./benchmark.sh --no-build propertyaccess
rtk ./benchmark.sh --no-build propertyaccess
```

```text
profile                 asynkron_ms  jint_ms  delta
propertyaccess                  923      491  Jint 1.88x faster
propertyaccess                  927      516  Jint 1.80x faster
propertyaccess                  918      466  Jint 1.97x faster
propertyaccess                  917      526  Jint 1.74x faster
```

Signal delta:

```text
Baseline signal: propertyaccess focused Asynkron row = 914 ms
Final signal: propertyaccess retained-runtime Asynkron row = 914 ms
Signal delta: 0 ms; no retained runtime improvement because the attempted rows regressed to 917-927 ms
```

## Interpretation

The direct expression-boundary read path was too small to matter in the current
profile. It avoided some dispatch and private-slot checks, but the remaining
cost stayed dominated by expression-program execution, identifier reads, object
storage lookup, and compound assignment plumbing. The attempted rows were all
slower than the focused baseline, so retaining the runtime edit would add a
special case without meeting the 10% performance gate.

Future work should not retry this exact `GetProgramNamedPropertyValue` direct
own-data-property shortcut. More promising follow-up remains shared encoded
expression-program execution or emit-time slot normalization that removes
operation decode and identifier lookup overhead together instead of shaving a
single property-read dispatch edge.
