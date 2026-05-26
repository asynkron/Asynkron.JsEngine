# ADR 0175: Keep private-field storage descriptor-typed

## Status

Accepted

## Context

Issue `autrun-disl2i2p0adk-86311e5aad` / PR #2115 continued the bounded
Unboxer cleanup of legacy `object?` carriers inside the core runtime.

`JsObjectState.PrivateFields` was still declared as
`Dictionary<string, object?>`, even though the discovered writers stored
`PropertyDescriptor` entries for private data fields and private accessors. The
mixed carrier forced read, write, clone, and descriptor-definition paths to
pattern-match private slots and keep legacy fallback branches such as
`JsValue.FromObjectUnsafe(slot)` for values that should already have been
descriptor-backed `JsValue` data.

The slice migrated only the private-field map and its direct consumers in
`src/Asynkron.JsEngine/JsTypes/JsObject.cs`. Public compatibility boundaries,
including the `IDictionary<string, object?>` implementation on `JsObject`, were
left unchanged.

## Decision

Keep `JsObjectState.PrivateFields` as
`Dictionary<string, PropertyDescriptor>`.

For private fields and private accessors:

1. store entries as `PropertyDescriptor` instances, not mixed `object?` slots;
2. use `PropertyDescriptor.JsValue` for data values and `Get` / `Set` for
   private accessors;
3. clone private-field entries through the descriptor clone path;
4. delete fallback branches that reinterpret private slots as arbitrary host
   objects; and
5. preserve existing private-field missing-entry TypeError behavior instead of
   creating private slots through ordinary property assignment.

Private brands remain separate identity state and are not part of the private
field value carrier decision.

## Consequences

- Future private-name runtime changes must not reintroduce
  `Dictionary<string, object?>` or generic object fallback storage for
  `PrivateFields`.
- Focused proof should include a scoped before/after search for
  `PrivateFields`, `Dictionary<string, object?>`, and
  `JsValue.FromObjectUnsafe(slot)`, plus private field, private accessor,
  inherited lookup, and static private-field coverage.
- Descriptor-typed private fields align this storage with ADR 0155's rule that
  core `PropertyDescriptor` data values stay `JsValue`-native.
- Public and interop APIs can continue exposing `object?` where they are facade
  boundaries; this decision is about the core `JsObjectState.PrivateFields`
  carrier.

## Related

- `.claude/rules/ecmascript-private-names.md`
- `.claude/rules/jsvalue-core-values.md`
- `docs/adrs/0155-keep-propertydescriptor-data-values-jsvalue-native.md`
