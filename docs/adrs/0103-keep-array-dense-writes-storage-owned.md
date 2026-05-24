# ADR 0103: Keep array dense writes storage-owned

## Status

Accepted

## Context

Issue `autrun-diqyh2msb568-aa393696f2` / PR #1687 selected `arrayops` from the
required `rtk ./benchmark.sh` baseline because it was one of the largest current
Asynkron-vs-Jint losses:

```text
arrayops  asynkron_ms=1379  jint_ms=361  Jint 3.82x faster
```

The required CPU profile,
`rtk ./tools/profile arrayops --cpu --calltree-depth 40 --calltree-width 40`,
showed dense array element creation and repeated length bookkeeping under
`Array.prototype.map`, `Array.prototype.filter`, and `Array.prototype.push`.
Fresh array result writes were routing through generic descriptor/property
creation, and length growth was repeatedly updating the backing `length`
property through boxed numeric writes.

The accepted implementation added a dense `JsArray` data-property fast path,
routed numeric-index `map` and `filter` result writes through it, and updated the
cached `length` slot with `JsValue` directly. The focused comparison improved
the selected benchmark from the 1379 ms baseline to repeated final runs of
906 ms, 953 ms, and 902 ms.

A build-back fix then caught the critical boundary: ECMAScript array indices are
strictly less than `2^32 - 1`. The property name `"4294967295"` is an ordinary
property and must not grow array length, so the numeric helper can only enter the
dense array fast path for indices `< uint.MaxValue`.

## Decision

Keep dense array creation and length-growth performance work owned by
`JsArray` storage helpers, not by ad hoc built-in shortcuts.

The durable policy is:

1. add dense-write fast paths at the array storage boundary, where extensibility,
   numeric descriptors, length writability, sparse storage, and length updates
   are already owned;
2. keep built-ins such as `map` and `filter` on numeric helper overloads that
   still fall back to the ordinary descriptor-based `CreateDataProperty` path;
3. only treat numeric property indices `< uint.MaxValue` as array indices for
   dense array growth; and
4. prove any future array fast path with both performance evidence for the
   selected benchmark and semantic coverage for descriptor, writability, and
   `2^32 - 1` boundary behavior.

## Consequences

- Fresh dense array result writes avoid descriptor allocation and index string
  construction in the common `arrayops` path.
- The generic property-definition path remains the source of truth for
  non-extensible arrays, custom numeric descriptors, non-writable length,
  proxies, species results that are not `JsArray`, and ordinary properties at
  `"4294967295"`.
- Future performance work must not widen the dense path by using `uint` range
  checks alone; `uint.MaxValue` is a valid `uint` value but not an ECMAScript
  array index.
- Performance proof alone is not enough for this area. The same change must
  carry focused spec regressions for observable array/property boundaries.

## Related

- `docs/performance/arrayops-dense-array-length-storage.md`
- `.claude/rules/performance-profiling-guardrails.md`
- `.claude/rules/js-spec-property-access.md`
