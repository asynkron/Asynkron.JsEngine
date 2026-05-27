# ADR 0240: Keep JsOps property lookup receivers JsValue-native

## Status

Accepted

## Context

Issue `autrun-ditlbq6xugc0-9d0cb22469` / PR #2432 continued the recurring
Unboxer cleanup by targeting a private property-read helper in
`Runtime/JsOps.cs`.

Before the delivery, `TryGetPropertyValue(JsValue target, string propertyName,
...)` extracted `target.ObjectValue` and passed that payload into
`TryGetPropertyValueObject(object? target, ...)`. The helper then reconstructed
the receiver for `JsObject`, prototype, and accessor reads with
`JsValue.FromObjectUnsafe(target)`.

That bridge was not a public facade or host-interop boundary. The caller
already had the original JavaScript value, and property lookup receiver identity
is observable through accessors, proxies, primitive prototype methods, and
JavaScript throw propagation through the active `EvaluationContext`.

## Decision

Keep private `JsOps` property-value lookup helpers `JsValue`-native when the
caller already has the JavaScript target value.

For `JsOps.TryGetPropertyValue(...)` and adjacent private property-read helper
work:

1. pass the original `JsValue` target through the helper as the receiver;
2. inspect `target.ObjectValue` only for shape-specific dispatch;
3. forward the original `JsValue` receiver to `JsObject`, prototype, and
   `IJsPropertyAccessor` reads;
4. preserve the active `EvaluationContext?` on context-aware reads and
   `ThrowSignal` conversion; and
5. keep any unavoidable `JsValue.FromObjectUnsafe(...)` conversion explicit at
   a remaining legacy boundary rather than hiding it inside a private helper
   that already has a `JsValue`.

Do not reintroduce a private `object?` property-read helper just because the
runtime payload has been extracted for dispatch.

## Consequences

- The property lookup path no longer drops the original receiver to an untyped
  object payload and reconstructs it before accessor/prototype lookup.
- Future Unboxer slices can still pattern-match on concrete runtime payloads,
  but the JavaScript receiver remains the original `JsValue`.
- Public `object?` facades, debugger/inspection projections, and host interop
  boundaries remain outside this decision.
- The older property-read performance boundary remains in ADR 0188; this ADR
  owns the object-carrier cleanup rule for the private `JsOps` lookup helper.

## Evidence

- Delivery PR #2432 merged as commit
  `715eeb4617b5b51fd680c432969444ee9a371bbb`.
- Build-stage delivery commit
  `e0d0b84c Keep JsOps property lookup receiver as JsValue` changed only
  `src/Asynkron.JsEngine/Runtime/JsOps.cs`.
- Build-stage baseline signal:
  `TryGetPropertyValueObject|FromObjectUnsafe(target)` in `Runtime/JsOps.cs`
  had 9 hits.
- Build-stage final signal:
  `TryGetPropertyValueObject|FromObjectUnsafe(target)` in `Runtime/JsOps.cs`
  had 0 hits.
- Focused build-stage verification passed:
  `dotnet test tests/Asynkron.JsEngine.Tests/Asynkron.JsEngine.Tests.csproj -c Release --filter "FullyQualifiedName~PropertyAccessFastPathTests|FullyQualifiedName~JsOpsTests"`
  with 36 tests passing.
- Learn-stage recheck found no
  `TryGetPropertyValueObject|FromObjectUnsafe(target)` hits in
  `src/Asynkron.JsEngine/Runtime/JsOps.cs`.

## Related

- `.claude/rules/jsvalue-core-values.md`
- `.claude/rules/js-spec-property-access.md`
- `docs/adrs/0148-keep-context-property-reads-jsvalue-receiver-native.md`
- `docs/adrs/0188-keep-named-property-read-fast-paths-storage-owned-and-receiver-preserving.md`
- `docs/adrs/0220-keep-assignment-property-receivers-jsvalue-native.md`
