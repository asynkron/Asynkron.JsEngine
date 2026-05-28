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

Issue #2451 / PR #2464 found the same seam in the adjacent iterator protocol
helper `TryInvokeSymbolMethod`. The helper extracted an
`IJsPropertyAccessor` target to prove object-like shape, but then used
`JsValue.FromObjectUnsafe(target)` as the `JsOps.TryGetPropertyValueJsValue`
lookup receiver even though the callers already passed the original iterable
`JsValue` as `thisArg`.

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
6. when a helper has both an extracted accessor/object payload and the original
   JavaScript receiver value, perform Get/GetMethod-style property lookup
   through the original `JsValue` receiver. The extracted payload may prove
   shape or dispatch interface behavior, but it must not replace the receiver
   merely because it is the extension target.

Do not reintroduce a private `object?` property-read helper just because the
runtime payload has been extracted for dispatch.

## Consequences

- The property lookup path no longer drops the original receiver to an untyped
  object payload and reconstructs it before accessor/prototype lookup.
- Future Unboxer slices can still pattern-match on concrete runtime payloads,
  but the JavaScript receiver remains the original `JsValue`.
- Adjacent iterator protocol and symbol-method lookup helpers follow the same
  rule: keep lookup receiver identity on the original `JsValue` while
  preserving existing symbol key fallback order and callable invocation
  `this` binding.
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
- Issue #2451 / PR #2464 extended the same decision to
  `src/Asynkron.JsEngine/Ast/IJsPropertyAccessorExtensions.cs`.
- Build-stage delivery commit
  `07767a39 Use JsValue receiver for symbol-method property lookup` replaced
  the `TryInvokeSymbolMethod` lookup receiver
  `JsValue.FromObjectUnsafe(target)` with the caller-provided `thisArg`.
- Delivery PR #2464 was squash-merged as commit
  `7df61f04 Use JsValue receiver for symbol-method property lookup (#2464)`.
- Build-stage signal for
  `FromObjectUnsafe(target)` in `IJsPropertyAccessorExtensions.cs` changed from
  1 hit to 0 hits.
- Focused build-stage verification passed:
  `dotnet test tests/Asynkron.JsEngine.Tests/Asynkron.JsEngine.Tests.csproj -c Release --filter "FullyQualifiedName~JsOpsTests|FullyQualifiedName~DestructuringIteratorTests|FullyQualifiedName~PropertyAccessFastPathTests"`
  with 48 tests passing.
- Canonical verification for PR #2464 passed `make quality` with 4439 tests
  passing and 2 skipped.

## Related

- `.claude/rules/jsvalue-core-values.md`
- `.claude/rules/js-spec-property-access.md`
- `docs/adrs/0148-keep-context-property-reads-jsvalue-receiver-native.md`
- `docs/adrs/0188-keep-named-property-read-fast-paths-storage-owned-and-receiver-preserving.md`
- `docs/adrs/0220-keep-assignment-property-receivers-jsvalue-native.md`
