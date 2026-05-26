# ADR 0191: Keep WeakMap value storage JsValue-native

## Status

Accepted

## Context

Issue `autrun-disqx5obgqv4-52e407689b` / PR #2163 continued the recurring
object-to-`JsValue` cleanup by targeting `JsWeakMap`.

Before the delivery, `JsWeakMap` stored values in
`ConditionalWeakTable<object, object?>` (CWT), converted `JsValue` values
through a private `ExtractValueObject(...)` switch, and recovered stored values
through `JsValue.FromObjectUnsafe(...)`. That made `WeakMap` a private runtime
object-carrier even though the standard-library entrypoints already passed and
returned `JsValue`.

The key side of the table is intentionally different. `WeakMap` keys are weakly
held object identities, and `ConditionalWeakTable` requires reference-type keys.
`JsWeakCollectionHelpers.ExtractWeakKeyObject(...)` therefore remains the
boundary that extracts the runtime object identity for weak-key semantics.

The table value side is also constrained by `ConditionalWeakTable`: values must
be reference types, so `JsValue` cannot be stored directly as the CWT value. The
accepted delivery replaced the boxed value with a private reference wrapper that
carries a `JsValue`. Missing-entry detection still comes from
`TryGetValue(...)`, which keeps stored `JsValue.Undefined` distinct from absence.

Focused proof used:

```bash
rtk rg -n "ConditionalWeakTable<object, object\?>|ExtractValueObject|FromObjectUnsafe" src/Asynkron.JsEngine/JsTypes/JsWeakMap.cs
rtk dotnet test tests/Asynkron.JsEngine.Tests --filter "FullyQualifiedName~WeakMap"
```

The baseline scan matched the legacy storage/conversion seams, the final scan
found no matches, and the focused WeakMap suite passed 24 tests. The canonical
run-quality gate then ran `make quality` successfully.

## Decision

Keep `JsWeakMap` value storage `JsValue`-native while preserving weak-key object
identity as the CWT key boundary.

For future weak collection work:

1. keep weak keys as the object references required by
   `ConditionalWeakTable`;
2. store `WeakMap` values as `JsValue` inside a private reference wrapper when a
   CWT-backed table owns the storage;
3. use `TryGetValue(...)` or an equivalent presence check to distinguish a
   missing key from a stored `JsValue.Undefined`;
4. do not convert `WeakMap` values through `ExtractValueObject(...)`,
   `JsValueExtractor`, `JsValue.FromObjectUnsafe(...)`, or separate
   `null`/`undefined` sentinels; and
5. treat `WeakSet` CWT values as presence sentinels, not JavaScript values, until
   a separate focused slice proves a different storage contract.

## Consequences

- `WeakMap` values now stay on the core runtime value primitive from `set`
  through `get`.
- The CWT reference-type constraint is explicit and local to the wrapper rather
  than leaking as boxed JavaScript values.
- Future weak-collection migrations should not mistake intentional weak-key
  object identity plumbing for legacy `object?` JavaScript-value storage.
- Proof for this boundary should include the legacy seam search above and
  focused WeakMap cases for object keys, missing entries, updates, `undefined`,
  `null`, and primitive-key rejection.

## Related

- `.claude/rules/jsvalue-core-values.md`
- `docs/adrs/0115-keep-jsmap-key-storage-jsvalue-native.md`
