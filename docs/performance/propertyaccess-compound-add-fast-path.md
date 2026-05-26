# Propertyaccess named-property read fast path

Date: 2026-05-26

## Workload

- Profile: `propertyaccess`
- Script: `tools/profile-scripts/propertyaccess.js`
- Shape: repeated direct and nested object property reads accumulated through
  `sum += ...` inside a `for` loop.

## Baseline

Command:

```bash
rtk ./benchmark.sh
```

Relevant row from the pre-edit full matrix:

```text
propertyaccess  1735 ms  576 ms  Jint 3.01x faster
```

CPU profile command:

```bash
rtk ./tools/profile propertyaccess --cpu --calltree-depth 40 --calltree-width 40
```

The hot subtree was:

```text
ExecuteInstructionLoop
-> HandleCompoundAssignmentSlot
-> HandleCompoundAssignmentSlotSlow
-> EvaluateExpressionProgram
-> GetProgramNamedPropertyValue
-> JsOps.TryGetPropertyValue
-> JsObject.TryGetProperty*
```

The profile also showed assignment-slot overhead around the two `sum += ...`
statements in the loop.

## Change

- Added a direct `JsValueKind.Object` + `JsObject` path in
  `JsOps.TryGetPropertyValue`, preserving the current receiver value instead of
  entering the generic object switch.
- Added an own data-property read path in `JsObject.TryGetOwnPropertyJsValue`
  that returns stored data values directly while still falling through for
  accessors, virtual properties, and prototype traversal.
- Added a flat-slot `+= <expression>` path for non-awaiting add compound
  assignments so hot loop accumulation can evaluate the right-hand expression
  and write the slot without the generic identifier assignment path.

Focused semantic coverage was added for own data property shadowing, prototype
getter receiver binding, and single evaluation of a getter RHS in `+=`.

## Final Signal

Focused test:

```bash
rtk dotnet test tests/Asynkron.JsEngine.Tests --filter "FullyQualifiedName~PropertyAccessFastPathTests" -- xUnit.MaxParallelThreads=1 -timeout 20000
```

Result:

```text
3 tests passed
```

Repeated focused timing:

```bash
rtk ./benchmark.sh propertyaccess
rtk ./benchmark.sh --no-build propertyaccess
rtk ./benchmark.sh --no-build propertyaccess
```

Results:

```text
propertyaccess  1067 ms  836 ms  Jint 1.28x faster
propertyaccess  1262 ms  489 ms  Jint 2.58x faster
propertyaccess  1061 ms  515 ms  Jint 2.06x faster
```

Against the 1735 ms pre-edit Asynkron baseline, the repeated focused runs were
about 27% to 39% faster, clearing the requested 10% improvement threshold.

Final CPU profile still names the same broad owner surface, but the named
property subtree is smaller relative to the loop and direct `JsObject` lookup is
visible under `JsOps.TryGetPropertyValue`.
