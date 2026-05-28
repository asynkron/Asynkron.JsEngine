# Failed propertyaccess compound RHS fast path

Date: 2026-05-28
Issue: autrun-diu68o64336o-198e1cf9f1

## Slice

This run targeted the `propertyaccess` profile's compound-assignment right-hand
side property reads:

- `sum += obj.a.b.c.d.e`
- `sum += obj.x + obj.y + obj.z`

The goal was to remove overhead under:

```text
HandleCompoundAssignmentSlotSlow
-> EvaluateExpressionProgram
-> GetProgramNamedPropertyValue
```

No runtime change is retained. Two variants were tried and reverted because the
repeated timing rows did not clear the required 10%+ Asynkron-side improvement
gate.

## Baseline signal

Baseline timestamp: 2026-05-28T08:47:00Z

Focused pre-edit benchmark:

```bash
rtk ./benchmark.sh propertyaccess
```

```text
profile                 asynkron_ms  jint_ms  delta
propertyaccess                  987     1037  Tie
```

Full pre-edit benchmark table later confirmed `propertyaccess` was still a
current Jint-faster row in the broader run:

```bash
rtk ./benchmark.sh
```

```text
propertyaccess                     906      561  Jint 1.61x faster
```

Required pre-edit CPU profile command, run three times:

```bash
rtk ./tools/profile propertyaccess --cpu --calltree-depth 40 --calltree-width 40
```

The repeated profiles consistently pointed at the same owner surface:

```text
ExecuteInstructionLoop
-> HandleCompoundAssignmentSlot
-> HandleCompoundAssignmentSlotSlow
-> EvaluateExpressionProgram
-> GetProgramNamedPropertyValue
-> JsOps.TryGetPropertyValue
-> JsObject.TryGetProperty
-> JsObject.TryGetSimplePropertyWithReceiver
```

Representative root timings:

```text
ExecuteInstructionLoop: 192.08 ms
HandleCompoundAssignmentSlotSlow: 121.68 ms
EvaluateExpressionProgram under compound assignment: 80.39 ms
```

```text
ExecuteInstructionLoop: 197.54 ms
HandleCompoundAssignmentSlotSlow: 129.45 ms
EvaluateExpressionProgram under compound assignment: 102.19 ms
```

```text
ExecuteInstructionLoop: 198.43 ms
HandleCompoundAssignmentSlotSlow: 129.45 ms
EvaluateExpressionProgram under compound assignment: 100.71 ms
```

## Attempted changes

### Simple named-property RHS evaluator

The first variant added a candidate flag for simple named-property RHS
expression programs and routed compound assignment RHS evaluation through a
small stack evaluator that supported:

- `LoadLiteral`
- `LoadIdentifier`
- `RequireObjectCoercible`
- `GetNamedProperty`
- simple binary operators

This preserved getter order in focused tests, but the profile showed the new
helper itself becoming a larger owner surface:

```text
EvaluateCompoundAssignmentRhsProgram
-> TryEvaluateSimpleNamedPropertyRhsProgram
-> EvaluateProgramIdentifier
-> GetProgramNamedPropertyValue
```

Focused timing after this variant:

```bash
rtk ./benchmark.sh propertyaccess
```

```text
profile                 asynkron_ms  jint_ms  delta
propertyaccess                  910      577  Jint 1.58x faster
```

This did not improve meaningfully over the 906 ms baseline row.

### Slot-aware target read/write in compound slow path

The second variant reverted the custom RHS evaluator and tried a smaller
compound-slot change: when flat slots were unavailable, use
`TryReadIdentifierWithSlot` / `TryWriteIdentifierWithSlot` before falling back to
the generic identifier cache path for the compound assignment target.

Focused semantic tests passed while this attempt was in place:

```bash
rtk dotnet test tests/Asynkron.JsEngine.Tests --filter "FullyQualifiedName~PropertyAccessFastPathTests" -- xUnit.MaxParallelThreads=1 -timeout 20000
```

```text
ok dotnet test: 5 tests passed, 7 warnings in 1 projects (1.4 s)
```

Repeated timing did not clear the gate:

```bash
rtk ./benchmark.sh propertyaccess
rtk ./benchmark.sh --no-build propertyaccess
rtk ./benchmark.sh --no-build propertyaccess
```

```text
profile                 asynkron_ms  jint_ms  delta
propertyaccess                  814      484  Jint 1.68x faster
propertyaccess                  946      511  Jint 1.85x faster
propertyaccess                  909      447  Jint 2.03x faster
```

The best row was about 10.2% faster than the 906 ms baseline, but the repeated
rows were not stable: median final Asynkron time was 909 ms, effectively no
improvement. The runtime and test edits were reverted.

## Final signal

Final timestamp: 2026-05-28T08:55:59Z

Signal delta:

```text
Baseline signal: propertyaccess Asynkron focused row = 906 ms
Final signal: propertyaccess Asynkron retained-runtime row = 906 ms
Signal delta: 0 ms, no retained runtime improvement
```

## Interpretation

The profile evidence remains useful: compound-assignment RHS expression-program
execution is still visible in `propertyaccess`, but replacing the generic
expression loop with a narrow named-property RHS loop did not reduce enough
overhead and made identifier reads more prominent. The slot-aware target
read/write variant produced one good row but failed repeatability.

Future work should avoid another runner-local mini interpreter unless it can
remove `ExpressionProgram.GetOperation` and identifier lookup overhead together.
More promising follow-up targets are emit-time normalization that gives this
loop real flat-slot IDs, or a compact encoded expression-program execution path
that stays shared with the existing expression interpreter rather than adding a
parallel RHS evaluator.
