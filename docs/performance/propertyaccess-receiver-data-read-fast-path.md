# Propertyaccess receiver data-read fast path

Date: 2026-05-27
Issue: autrun-ditg7nt935mg-6d4d6539dd

## Slice

This run targeted the ordinary-object named property read path used by the
`propertyaccess` profile. The benchmark repeatedly reads nested data properties
inside `sum += ...` loops:

- `obj.a.b.c.d.e`
- `obj.x + obj.y + obj.z`

The retained runtime change is limited to `JsObject.TryGetProperty` overloads
that receive an explicit receiver. It mirrors the existing no-receiver simple
data-property path when virtual providers, own descriptors, and private slots
are not involved. Prototype traversal preserves the original receiver.

## Baseline signal

Baseline timestamp: 2026-05-27T12:22:20Z

Command:

```bash
rtk ./benchmark.sh propertyaccess
```

```text
profile                 asynkron_ms  jint_ms  delta
propertyaccess                 2092      685  Jint 3.05x faster
```

Required pre-edit CPU profile command, run three times:

```bash
rtk ./tools/profile propertyaccess --cpu --calltree-depth 40 --calltree-width 40
```

The repeated profiles consistently kept property reads under this owner surface:

```text
ExecuteInstructionLoop
-> HandleCompoundAssignmentSlot
-> HandleCompoundAssignmentSlotSlow
-> EvaluateExpressionProgram
-> GetProgramNamedPropertyValue
-> JsOps.TryGetPropertyValue
-> JsObject.TryGetProperty
-> JsObject.TryGetPropertyJsValue
-> JsObject.TryGetOwnPropertyJsValue
```

## Change

Added `TryGetSimplePropertyWithReceiver` in `JsObject` and called it from both
receiver overloads before the full depth-limited lookup. The helper:

- exits to the existing full path when a virtual property provider exists;
- exits to the existing full path when the object has an own descriptor for the
  requested name;
- exits to the existing full path for private-slot names;
- returns direct storage hits immediately for simple data properties;
- recurses through `Prototype` or `PrototypeAccessor` with the original receiver
  on misses.

This deliberately avoids descriptor, accessor, proxy, virtual provider,
private-slot, and receiver-sensitive getter changes.

## Final signal

Final timestamp: 2026-05-27T12:25:31Z

Commands:

```bash
rtk ./benchmark.sh propertyaccess
rtk ./benchmark.sh --no-build propertyaccess
rtk ./benchmark.sh --no-build propertyaccess
rtk ./benchmark.sh --no-build propertyaccess
rtk ./benchmark.sh --no-build propertyaccess
```

```text
profile                 asynkron_ms  jint_ms  delta
propertyaccess                 1363      638  Jint 2.14x faster
propertyaccess                 2205      725  Jint 3.04x faster
propertyaccess                 1493      976  Jint 1.53x faster
propertyaccess                 1354      722  Jint 1.88x faster
propertyaccess                 1686      720  Jint 2.34x faster
```

The median final Asynkron row is 1493 ms, a 599 ms improvement from the 2092 ms
baseline, or about 28.6% faster. One final row regressed to 2205 ms and is
recorded as benchmark noise; four of five final rows were at least 19% faster
than the baseline.

Final CPU profile command:

```bash
rtk ./tools/profile propertyaccess --cpu --calltree-depth 40 --calltree-width 40
```

The final profile shows the retained direct helper on the same named-read path:

```text
GetProgramNamedPropertyValue
-> JsOps.TryGetPropertyValue
-> JsObject.TryGetProperty
-> JsObject.TryGetSimplePropertyWithReceiver
-> JsObject.TryGetJsValue
-> HybridDictionary<JsValue>.TryGetValue
```

## Test signal

Focused guardrails:

```bash
rtk dotnet test tests/Asynkron.JsEngine.Tests --filter "FullyQualifiedName~PropertyAccessFastPathTests|FullyQualifiedName~UnifiedBytecodeProductionInvocationTests" -- xUnit.MaxParallelThreads=1 -timeout 20000
```

```text
ok dotnet test: 41 tests passed, 7 warnings in 2 projects (2.5 s)
```

The warnings are existing nullable warnings in unrelated test files.
