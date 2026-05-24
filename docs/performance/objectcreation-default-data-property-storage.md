# Objectcreation Default Data Property Storage

Date: 2026-05-24

## Selected Benchmark

`objectcreation` was selected from the required `rtk ./benchmark.sh` baseline
because it was a current high-gap Asynkron-vs-Jint loss while the same-day
investigation notes called out `classdef`, `arrayops`, and `ir-arithmetic` as
recently covered slices.

Baseline signal:

```text
objectcreation  asynkron_ms=1464  jint_ms=455  Jint 3.22x faster
```

## Profile Finding

The required CPU profile command was:

```bash
rtk ./tools/profile objectcreation --cpu --calltree-depth 40 --calltree-width 40
```

The hot call tree rooted at `ExecuteInstructionLoop` spent most of the selected
sample under ordinary object-literal property creation:

```text
ExecuteInstructionLoop                                      169.69 ms
HandleEvaluateAndDiscard                                    150.26 ms
EvaluateExpressionProgram                                   150.26 ms
DefineObjectLiteralProperty                                 116.10 ms
JsObject.DefineProperty / DefinePropertyInternal            116.10 ms
Dictionary<__Canon,__Canon>.set_Item                        113.32 ms
```

The selected script repeatedly creates plain object literals with default data
properties:

```js
{
    id: i,
    name: "item" + i,
    value: i * 2,
    nested: { a: i, b: i * 2 }
}
```

Those properties have the default object-literal attributes
`writable/enumerable/configurable: true`, so storing an explicit descriptor for
every fresh key was unnecessary in the common ordinary-object path.

## Change

`JsObject.DefineDefaultDataProperty` now stores ordinary default data
properties directly in object storage and preserves insertion order, leaving the
descriptor dictionary empty for these implicit descriptors.

The runner uses that path for static and computed object-literal data
properties. It falls back to the existing descriptor machinery when the object
is non-extensible, a virtual property provider is present, the key is private,
or a stored descriptor already exists. Prototype mutation, accessors, methods,
and other non-default descriptor paths continue using the existing full
semantics.

`Object.getOwnPropertyDescriptor` already materializes the correct default
descriptor from storage, and later `Object.defineProperty` calls still promote
implicit storage to an explicit descriptor when attributes change.

## Final Signal

The focused post-change profile showed the targeted subtree shrink:

```text
ExecuteInstructionLoop                                      121.72 ms
DefineObjectLiteralProperty                                  72.84 ms
JsObject.DefineDefaultDataProperty                           72.84 ms
Dictionary<__Canon,__Canon>.set_Item                         72.84 ms
```

Repeated focused comparison after the change:

```text
objectcreation  asynkron_ms=1156  jint_ms=419  Jint 2.76x faster
objectcreation  asynkron_ms=1229  jint_ms=451  Jint 2.73x faster
objectcreation  asynkron_ms=1169  jint_ms=451  Jint 2.59x faster
```

The final Asynkron average was about 1185 ms versus the 1464 ms baseline
signal, roughly a 19% improvement. The repeated final runs stayed above the
requested 10% threshold despite local timing noise.

## Verification

Focused semantic verification:

```text
rtk dotnet test tests/Asynkron.JsEngine.Tests --filter "FullyQualifiedName~ObjectLiteralSemanticsRegressionTests"
ok dotnet test: 8 tests passed
```

The canonical internal quality gate remains `rtk make quality` and is delegated
to the orchestrator-run verification stage.
