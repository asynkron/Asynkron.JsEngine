# Objectcreation Known-New Property Fast Path

## Why this slice

`objectcreation` was selected from the required `rtk ./benchmark.sh` baseline
because it remained a clear Jint win in repeated measurements and maps to a
bounded object literal owner surface.

Baseline excerpts:

```text
objectcreation  asynkron_ms=2847  jint_ms=777  Jint 3.66x faster
objectcreation  asynkron_ms=1187  jint_ms=639  Jint 1.86x faster
```

## Profile signal

Command:

```bash
rtk ./tools/profile objectcreation --cpu --calltree-depth 40 --calltree-width 40
```

The pre-change CPU call tree rooted in `ExecuteInstructionLoop` showed the
object literal path dominated by property insertion bookkeeping:

```text
DefineObjectLiteralProperty
  DefineDefaultDataProperty
    TrackPropertyInsertion
      Dictionary.set_Item
```

That path runs for every static property in the profile's repeated object
literals:

```js
{
    id: i,
    name: "item" + i,
    value: i * 2,
    nested: { a: i, b: i * 2 }
}
```

## Change

The expression program compiler now marks static object literal properties as
known-new when all earlier property names are statically known and the name has
not appeared before. The runner uses that proof to call a new
`DefineKnownNewDefaultDataProperty` path, which avoids duplicate-key checks in
both object storage and insertion-order tracking.

The generic insertion-order structure was also tightened for small objects:
ordinary objects now track insertion order with a list first and create a lookup
set only after the small-object threshold is crossed. This avoids allocating a
linked-list node and dictionary entry for each small object literal property.

Computed names, spreads, duplicate static names, accessors, and methods keep the
existing conservative path unless the compiler can still prove the static data
property is new at that program point.

## Measurement

Final repeated focused comparison:

```text
objectcreation  asynkron_ms=737  jint_ms=510  Jint 1.45x faster
objectcreation  asynkron_ms=678  jint_ms=535  Jint 1.27x faster
objectcreation  asynkron_ms=671  jint_ms=543  Jint 1.24x faster
```

Compared with the warmed baseline run at 1187 ms, the selected profile improved
by roughly 38-43% in the repeated final measurements.

## Verification

Focused semantic coverage:

```bash
rtk dotnet test tests/Asynkron.JsEngine.Tests --filter "FullyQualifiedName~ExpressionProgramLoweringTests|FullyQualifiedName~ObjectDescriptorTests|FullyQualifiedName~NumericObjectKeysTests|FullyQualifiedName~DeleteOperatorTests" -- xUnit.MaxParallelThreads=1 -timeout 20000
```

Result: 232 tests passed.
