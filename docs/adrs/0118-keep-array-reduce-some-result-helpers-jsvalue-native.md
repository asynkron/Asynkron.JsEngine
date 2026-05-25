# ADR 0118: Keep Array reduce/some result helpers JsValue-native

## Status

Accepted

## Context

Issue `autrun-dirph7vxdbdc-edfa353492` / PR #1798 continued the recurring
object-carrier cleanup in Array prototype helpers.

Before the delivery, `ReduceLike` and `SomeLike` returned `object?` even though
their only direct Array prototype callsites already returned `JsValue` and
immediately rewrapped the helper results through
`JsValue.FromObjectUnsafe(...)`. The helper flow already held JavaScript values
as `JsValue`: the reducer accumulator, callback results, and boolean `some`
result all belong to the engine value primitive rather than a CLR object
carrier.

The accepted delivery changed `ReduceLike` and `SomeLike` to return `JsValue`,
made `SomeLike` return `JsValue.True`/`JsValue.False`, and returned the helper
results directly from `Array.prototype.reduce`, `reduceRight`, and `some`.
Review confirmed that the legacy `object?` signatures and caller-side
`FromObjectUnsafe(...)` wrappers were gone for the selected slice.

## Decision

Keep Array prototype result helpers `JsValue`-native when the helper computes a
JavaScript result for a `JsValue` host-method callsite.

For `Array.prototype.reduce`, `reduceRight`, `some`, and nearby private
iteration helpers:

1. return `JsValue` from private helpers once all selected callsites already
   require JavaScript values;
2. return canonical primitive values such as `JsValue.True`,
   `JsValue.False`, and `JsValue.Undefined` instead of CLR `bool`/`null`
   sentinels;
3. delete caller-side `JsValue.FromObjectUnsafe(...)` rewraps after the helper
   owns the typed return; and
4. leave unrelated async, promise, or host-interop result boundaries to their
   own focused slices unless the proof covers them too.

## Consequences

- Future Array prototype helper work should treat a private helper returning
  `object?` to a `JsValue` host method as migration debt, not as a compatibility
  boundary by default.
- A focused search is the right proof shape for this class:
  `rtk rg -n "object\\? (ReduceLike|SomeLike)|FromObjectUnsafe\\((ReduceLike|SomeLike)" src/Asynkron.JsEngine/StdLib/Array`
  should stay clean after this slice.
- Behavioral proof should stay on the affected methods first, for example the
  focused reduce/reduceRight/some internal test filter, before widening to
  broader Array semantics.
- This complements ADR 0111's Array static result-helper boundary and ADR
  0114's Array length helper carrier boundary. This ADR owns the Array
  prototype reduce/some result-helper carrier boundary.

## Related

- `.claude/rules/jsvalue-core-values.md`
- `docs/adrs/0111-keep-array-static-sync-result-helpers-jsvalue-native.md`
- `docs/adrs/0114-keep-array-length-helper-jsvalue-native.md`
