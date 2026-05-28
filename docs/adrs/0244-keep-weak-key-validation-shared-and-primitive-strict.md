# ADR 0244: Keep weak-key validation shared and primitive-strict

## Status

Accepted

## Context

Issue `autrun-diu14wsfwgjc-670bfedf98` / PR #2452 deduplicated weak-key
validation by replacing `FinalizationRegistryPrototype.CanBeHeldWeakly(...)`
with the shared `JsWeakCollectionHelpers.ExtractWeakKeyObject(...)` boundary
already used by `WeakMap` and `WeakSet`.

That consolidation exposed a spec-sensitive runtime shape: BigInt is a
JavaScript primitive, but the engine stores its payload as a `JsBigInt` object
inside `JsValue.ObjectValue`. If weak-key validation unwraps object payloads
before rejecting BigInt at the `JsValue` tag layer, a BigInt primitive can be
mistaken for a weakly held object reference.

The delivery fixed the hole in the shared helper by rejecting BigInt primitives
alongside `null`, `undefined`, strings, numbers, and booleans before any object
payload inspection. It also kept unregistered symbols on the accepted path and
registered symbols on the rejected path.

Focused proof used:

```bash
rtk dotnet test tests/Asynkron.JsEngine.Tests/Asynkron.JsEngine.Tests.csproj --configuration Release --filter "FullyQualifiedName~WeakCollectionsTests"
rtk dotnet build src/Asynkron.JsEngine/Asynkron.JsEngine.csproj --configuration Release
```

The focused weak-collection suite passed 4 tests, covering BigInt rejection for
`WeakMap`, `WeakSet`, and `FinalizationRegistry` target/unregister-token paths.
The source build passed with 0 errors and 0 warnings.

## Decision

Keep `JsWeakCollectionHelpers.ExtractWeakKeyObject(...)` as the shared
weak-reference validation boundary for `WeakMap`, `WeakSet`, and
`FinalizationRegistry`.

For future weak-reference work:

1. reject primitive `JsValue` tags before inspecting `ObjectValue`, including
   BigInt even though its payload is represented by the `JsBigInt` class;
2. accept only object identities and unregistered symbols as weak keys or
   unregister tokens;
3. reject registered symbols, `null`, `undefined`, strings, numbers, booleans,
   and BigInt primitives through the shared helper; and
4. do not reintroduce per-prototype weak-key validators unless a focused slice
   proves a spec-required semantic difference from the shared boundary.

## Consequences

- Weak-reference APIs now share one primitive-strict validation path.
- Future deduplication cannot accidentally widen `FinalizationRegistry` by
  moving it onto a helper that treats primitive payload objects as weak keys.
- The weak-key identity boundary remains separate from `WeakMap` value storage:
  key extraction returns the object identity required by `ConditionalWeakTable`,
  while stored JavaScript values stay `JsValue`-native as recorded in ADR 0191.
- Regression coverage for this boundary should exercise all consumers, not only
  the API currently being edited.

## Related

- `.claude/rules/jsvalue-core-values.md`
- `docs/adrs/0191-keep-weakmap-value-storage-jsvalue-native.md`
