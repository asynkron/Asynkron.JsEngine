# ADR 0123: Keep Number receiver object extraction typed and accessor-compatible

## Status

Accepted

## Context

Issue `autrun-dirquxdu7e2w-b1e0d8c752` / PR #1810 continued the recurring
core-runtime cleanup that moves legacy `object?` carriers back to `JsValue`.

Before the delivery, `NumberPrototype.RequireNumberReceiver` accepted a
`JsValue` receiver but immediately called the untyped `receiver.TryGetObject(out
var obj)`, producing a CLR object carrier before checking for a boxed number
payload. The method then used that object both for direct `JsObject` property
reads and for non-`JsObject` payloads that implement `IJsPropertyAccessor`.

The accepted delivery removed the ambiguous untyped object extraction and split
the receiver handling into explicit typed branches:

1. `receiver.TryGetObject<JsObject>(out var obj)` for direct `JsObject`
   `__value__` reads.
2. `receiver.TryGetObject<IJsPropertyAccessor>(out var accessor)` as a separate
   fallback for object payloads that are accessor-compatible without being
   `JsObject`.

Focused review confirmed this kept the previous functional surface for
host/object-like wrappers while removing the accidental `object?` local from the
Number prototype slice. The focused `AdditionalBuiltinsTests` pack passed 10/10.

## Decision

Keep Number prototype receiver extraction typed at the `JsValue` boundary while
preserving interface-based object payload semantics.

For `Number.prototype` receiver helpers and similar boxed-primitive helpers:

1. prefer `JsValue.TryGetObject<T>` over untyped `TryGetObject(out object?)`
   when the next operation requires a known runtime object shape;
2. split concrete runtime-object handling from interface fallback handling
   instead of replacing the old untyped branch with only a `JsObject` branch;
3. preserve accessor-compatible payloads such as `IJsPropertyAccessor` when the
   old object-carrier path admitted them;
4. keep primitive-number receiver handling separate from boxed-object receiver
   handling; and
5. prove the slice with a focused signature/local search plus the owning
   built-in tests before widening to other boxed primitives.

## Consequences

- Future object-carrier migrations must audit the old untyped payload surface
  before choosing a narrower typed branch. If a prior `object?` path accepted an
  interface implemented by host/object-like wrappers, the typed migration should
  preserve that interface path explicitly.
- The right narrow proof for this slice is a search such as
  `rtk rg -n "TryGetObject\\(out var|object\\? obj" src/Asynkron.JsEngine/StdLib/Number`
  plus the focused `AdditionalBuiltinsTests` command.
- Do not infer that all boxed primitive helpers can collapse to `JsObject`.
  Each helper owns its receiver compatibility boundary and needs its own focused
  proof.
- This complements the broader `JsValue` core value rule and the Array helper
  carrier ADRs by covering object extraction from `JsValue` rather than helper
  return values or collection storage.

## Related

- `.claude/rules/jsvalue-core-values.md`
- `docs/adrs/0114-keep-array-length-helper-jsvalue-native.md`
- `docs/adrs/0118-keep-array-reduce-some-result-helpers-jsvalue-native.md`
