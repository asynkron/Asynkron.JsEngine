# ADR 0115: Keep JsMap key storage JsValue-native

## Status

Accepted

## Context

Issue `autrun-diro77wbl75s-869c762677` / PR #1784 continued the
object-carrier cleanup in `JsMap` by migrating map storage from extracted CLR
`object` keys plus side-channel `null`/`undefined` handling to `JsValue` keys
with the `JsValue`-native `SameValueZero` comparer.

The first storage migration preserved lookup compatibility for most cases, but
the quality gate exposed a deterministic regression:
`GroupByTests.Map_GroupBy_Canonicalizes_NegativeZero_Key`. The map could accept
lookups for both `0` and `-0`, while still retaining `-0` as the stored
insertion-order key. That made the key observable through `Map.groupBy`,
`keys()`, `entries()`, and related iterator surfaces.

The accepted follow-up fixed the storage boundary by canonicalizing numeric
zero in `JsMap.Set`, `Get`, `Has`, `Delete`, and the internal retrieval path so
`-0` is stored and observed as canonical `+0`. The focused proof reran
`GroupByTests.Map_GroupBy_Canonicalizes_NegativeZero_Key` together with
`MapTests` and passed 47 tests.

## Decision

Keep `JsMap` key storage `JsValue`-native and make key canonicalization part of
the storage API boundary.

For `JsMap` and similar keyed JavaScript collections:

1. store JavaScript keys as `JsValue` when the owning runtime already receives
   `JsValue`;
2. use the `JsValue`-native `SameValueZero` comparer for lookup semantics;
3. canonicalize numeric `-0` to `+0` before adding keys to insertion-order or
   record-tracking structures, not only in equality comparison;
4. keep `null` and `undefined` as ordinary `JsValue` keys instead of
   side-channel sentinels; and
5. prove both lookup and observable stored-key behavior with direct collection
   cases and grouped/iterator cases that use `Object.is(...)` or reciprocal
   infinity checks.

## Consequences

- Future `JsMap` work should not reintroduce `JsValueExtractor.Extract(...)`,
  `JsValue.FromObjectUnsafe(...)`, or separate `null`/`undefined` carrier state
  on the internal map-key path.
- Passing `SameValueZero` lookup checks is not enough for signed-zero keys; the
  stored key must also be observable as `+0`.
- A focused proof pack for this boundary should include
  `GroupByTests.Map_GroupBy_Canonicalizes_NegativeZero_Key` and the owning
  `MapTests` group before relying on broader quality gates.

## Related

- `.claude/rules/jsvalue-core-values.md`
- `.claude/rules/ecmascript-numeric-coercions.md`
- `docs/adrs/0114-keep-array-length-helper-jsvalue-native.md`
