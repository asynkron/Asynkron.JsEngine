# JsValue Core Runtime Values

When working inside the core engine, keep JavaScript values represented as
`JsValue` until an explicitly intentional boundary requires another shape.

## Rules

1. Do not introduce new `object?` helper parameters, return values, or local
   conversion bridges for JavaScript values in parser, AST, optimizer,
   bytecode, IR, evaluator, or runtime helper code.
2. Preserve `object?` only at intentional boundaries such as public facade APIs,
   host interop surfaces, debugger/diagnostic projections, or CLR object
   conversion helpers.
3. When migrating legacy `object?` helper clusters, move the whole private
   helper flow to `JsValue` first, then delete obsolete bridge helpers such as
   `ToJsValue`, `ToObject`, or `FromObjectUnsafe` round trips that are no
   longer needed.
4. Use shared `JsValue`/`JsOps` operations for JavaScript coercions, equality,
   truthiness, and string conversion instead of recreating object-pattern
   coercion switches.
5. Keep the proof scoped to the migrated cluster: capture a targeted baseline
   search for the legacy signatures, rerun the matching search after the edit,
   and pair it with focused tests that cover the affected semantics.
6. If a public or compatibility `object?` convenience overload must remain
   during migration, quarantine it with compile-time pressure such as
   `[Obsolete(..., true)]` and migrate repo-internal callers to the `JsValue`
   overload explicitly. Wrap host primitives at the callsite with
   `new JsValue(...)`, `JsValue.FromJsArray(...)`, or another typed helper so
   overload resolution cannot silently fall back through `FromObjectUnsafe`.
7. For JavaScript collection storage (`Set`, `Map`, weak collections, or
   collection-like helpers), migrate the owning storage and equality comparer
   together. A `JsValue`-backed collection must use `JsValue`-native
   SameValueZero or spec equality semantics directly, including `NaN`,
   positive/negative zero, ordinal strings, symbols, and object identity. Keep
   legacy `object?` comparers only for collection owners that still store
   `object?`; do not route migrated storage through `ToObject`,
   `FromObjectUnsafe`, or side-channel sentinels for `null`/`undefined`.
8. When a private helper cluster already has complete `JsValue` coverage and a
   targeted search shows the `object?` overload has no internal callers, delete
   the legacy overload instead of keeping it as a speculative compatibility
   bridge. Use `[Obsolete(..., true)]` only when the overload must remain long
   enough to expose real callers or preserve an intentional boundary.

## Why

Issue `autrun-diqzx0r7ibgg-35b8604f32` / PR #1697 migrated
`TypedConstantExpressionTransformer` constant-fold helpers from `object?` to
`JsValue`. The old flow extracted literals into CLR objects, folded through
object-pattern coercion helpers, then converted folded results back to
`JsValue`. That made a core optimizer path violate the engine value-primitive
contract and kept boxing/conversion bridges alive in code that already had
`JsValue` literals. Future object-to-`JsValue` migrations should preserve
intentional public/interop boundaries, but core helper clusters should stay
`JsValue`-native end to end.

Issue `autrun-dir0bkkyd220-65fe370aa4` / PR #1700 migrated the bounded
`JsArray` legacy-overload slice by marking the `IEnumerable<object?>`,
`SetElement(..., object?)`, and `Push(object?)` overloads obsolete with
`error: true`, then updating exposed internal callsites to pass `JsValue`
directly. The old convenience overloads were still useful as compatibility
bridges, but leaving them unguarded let core runtime code accidentally choose
object conversion when it already had JavaScript values. Future array/object
carrier migrations should use the same bounded quarantine pattern: expose
callers with the compiler, migrate only the selected cluster, and keep the
before/after signature search as proof that accidental internal binding is gone.

Issue `autrun-dir1jb469ky8-1d5d23090a` / PR #1704 migrated `JsSet` storage from
`object?` plus separate `null`/`undefined` tracking to `List<JsValue>` and
`HashSet<JsValue>`. The important lesson was not just the field type change:
`Set` equality is part of the storage contract. The fix added a `JsValue` native
SameValueZero comparer so `NaN` coalesces, positive and negative zero compare
equal, strings stay ordinal, and symbols/objects keep identity semantics without
boxing through CLR objects. Future collection migrations should move storage,
membership tests, iteration, deletion, and comparer semantics as one cluster;
leaving a shared `object?` comparer in the path reintroduces the same conversion
boundary the migration is trying to remove.

Issue `autrun-dir2jazax9jk-d413d0e4d7` / PR #1713 removed the dead
`StandardLibrary.Array.Helpers.CreateDataPropertyOrThrow(..., object? value, ...)`
overload after earlier slices had already moved array result builders to
`CreateDataPropertyOrThrowJsValue`. The durable lesson is that once a private
core helper has no remaining internal callers and an equivalent typed path owns
the semantics, preserving the legacy overload just keeps an accidental
object-carrier entry point alive for future code.
