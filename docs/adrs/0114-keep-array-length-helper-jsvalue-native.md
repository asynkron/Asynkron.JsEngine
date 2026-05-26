# ADR 0114: Keep array length helper JsValue-native

## Status

Accepted

## Context

Issue `autrun-dirl74f4ybwo-4a2a4b907a` / PR #1765 continued the recurring
object-carrier cleanup in the core `JsArray` runtime.

Before the delivery, `JsArray.SetLength(object? value, ...)` was still the only
direct helper for array length assignment even though the internal callers
already held `JsValue` values from property assignment and assignment-reference
paths. That forced the length path through `JsValue.FromObjectUnsafe(...)`,
kept an avoidable `object?` overload available for core runtime code, and made
future callsites able to bind to the object carrier by accident.

The accepted delivery added an internal `SetLength(JsValue value, ...)` overload
that delegates to the existing `TrySetArrayLength(...)` semantics, and marked
the legacy `SetLength(object? value, ...)` overload `[Obsolete(..., true)]` as
a compile-time tripwire. A focused signature search showed that only the
obsolete overload definition remained, and the canonical quality gate passed
after a transient async test was rerun and then the full `make quality` gate
passed.

Issue #2052 / PR #2060 later found that the helper boundary was typed, but
adjacent array length descriptor initializers still constructed fallback length
descriptors with `PropertyDescriptor.Value = (double)_length`. That setter is
the object-carrier compatibility path because it routes through
`JsValue.FromObjectUnsafe(...)`. The follow-up migrated the three `JsArray`
length descriptor sites to `PropertyDescriptor.JsValue =
JsValue.FromDouble(_length)` and pinned descriptor value/writable/enumerable/
configurable behavior with a focused `Object.getOwnPropertyDescriptor(...,
"length")` regression.

## Decision

Keep array length assignment helpers `JsValue`-native inside the core runtime.

For `JsArray.SetLength` and similar array mutation helpers:

1. route internal runtime callers that already hold JavaScript values through a
   `JsValue` overload;
2. preserve the existing spec-owned length conversion and strict/sloppy failure
   behavior in the shared `TrySetArrayLength(...)` path;
3. construct initial and fallback array length data descriptors with
   `PropertyDescriptor.JsValue` rather than the `Value` compatibility setter
   whenever the source is the internal numeric `_length`;
4. keep any remaining compatibility `object?` overload as an obsolete
   error-level tripwire only while it is needed to expose or preserve a boundary;
5. prove the slice with targeted legacy-signature and descriptor-setter searches
   before and after the edit; and
6. treat unrelated public facade, host interop, debugger, or async boundaries as
   separate slices unless the selected proof covers them.

## Consequences

- Future array length work should not reintroduce an internal
  `JsValue.FromObjectUnsafe(...)` bridge when the caller already has a
  `JsValue`.
- A clean search such as
  `rtk rg -n "SetLength\\s*\\(\\s*object\\?" src tests` is the right narrow
  proof that internal callers no longer bind to the object overload.
- For array length descriptor cleanup, pair the helper signature search with an
  owner-file setter search such as
  `rtk rg -n "Value\\s*=\\s*\\(double\\)_length" src/Asynkron.JsEngine/JsTypes/JsArray.cs`
  so fallback descriptor initializers cannot silently keep the compatibility
  setter alive.
- If the obsolete object overload no longer preserves a real compatibility or
  discovery boundary, remove it rather than keeping a dead fallback.
- This complements ADR 0103's array storage ownership rule and ADR 0111's Array
  static result-helper boundary, and overlaps with ADR 0155's descriptor value
  carrier rule. This ADR owns the array length helper and length descriptor
  carrier boundary.

## Related

- `.claude/rules/jsvalue-core-values.md`
- `docs/adrs/0103-keep-array-dense-writes-storage-owned.md`
- `docs/adrs/0111-keep-array-static-sync-result-helpers-jsvalue-native.md`
- `docs/adrs/0155-keep-propertydescriptor-data-values-jsvalue-native.md`
