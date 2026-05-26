# ADR 0111: Keep Array static sync result helpers JsValue-native

## Status

Accepted

## Context

Issue `autrun-dir4l5ptszpk-836da057ea` / PR #1738 continued the bounded
object-carrier cleanup in Array static helpers.

Before the delivery, `ArrayOf`, `ArrayFrom`, and `ArrayFromIterable` all
returned `object?` even though their callers immediately converted the returned
array object back to `JsValue` with `JsValue.FromObjectUnsafe(...)`. The helper
flow already operated on `JsValue` arguments and produced JavaScript array
objects, so the `object?` return type only preserved an avoidable conversion
bridge at the final result boundary.

The accepted delivery changed the synchronous helper returns to `JsValue` and
removed the duplicate caller-side wrapping in `ArrayConstructor.Of(...)` and the
attached `Array.from` host function. `ArrayFromAsync` was deliberately left as a
separate slice because it returns through the promise/async path and was outside
the bounded synchronous helper migration. That async-specific boundary is now
owned by ADR 0198.

## Decision

Keep synchronous Array static result helpers `JsValue`-native once their
callers expect JavaScript values.

For `Array.of`, synchronous `Array.from`, and iterable `Array.from` helper
flows:

1. return `JsValue` from the private helper rather than `object?`;
2. wrap the constructed result array exactly once at the helper boundary with
   `JsValue.FromObjectUnsafe(result)`;
3. return the helper result directly from the host/constructor callsite instead
   of re-wrapping it; and
4. keep promise-producing helpers under their own proof boundary rather than
   folding async behavior into the synchronous helper migration.

## Consequences

- Future Array static-helper work should remove caller-side
  `JsValue.FromObjectUnsafe(...)` bridges when the helper can own the typed
  return value.
- A focused signature scan is the right proof shape for this class:
  `ArrayOf`, `ArrayFrom`, and `ArrayFromIterable` should be `JsValue`. The
  async `ArrayFromAsync` carrier is governed by ADR 0198.
- Static-method semantic proof should stay focused on the Array static-method
  tests first, then widen only when the touched helper participates in broader
  Array semantics.
- This complements `.claude/rules/jsvalue-core-values.md` and ADR 0103's array
  storage ownership rule. This ADR owns the result-carrier boundary for Array
  static sync helpers, not dense element writes.

## Related

- `.claude/rules/jsvalue-core-values.md`
- `docs/adrs/0103-keep-array-dense-writes-storage-owned.md`
- `docs/adrs/0198-keep-array-fromasync-result-helper-jsvalue-native.md`
