# JsValue Core Runtime Values

When working inside the core engine, keep JavaScript values represented as
`JsValue` until an explicitly intentional boundary requires another shape.

## Ownership

- This rule is the semantic home for cross-cutting core-runtime
  `JsValue`/object-carrier migration policy.
- Use this file for shared migration rules and proof expectations that apply to
  multiple helper clusters.
- Keep helper-specific carrier boundaries in their accepted ADRs instead of
  duplicating those decisions here:
  - `docs/adrs/0114-keep-array-length-helper-jsvalue-native.md`
  - `docs/adrs/0118-keep-array-reduce-some-result-helpers-jsvalue-native.md`
  - `docs/adrs/0123-keep-number-receiver-object-extraction-typed-and-accessor-compatible.md`
  - `docs/adrs/0143-keep-generator-pending-completion-payloads-jsvalue-native.md`
  - `docs/adrs/0148-keep-context-property-reads-jsvalue-receiver-native.md`

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
   overload or shared `JsOps` operation explicitly. Do not keep test or
   runtime callsites on extension syntax such as `.ToNumber()` or
   `.ToJsString()` when the receiver is already a `JsValue`. Wrap host
   primitives at the callsite with
   `new JsValue(...)`, `JsValue.FromJsArray(...)`, or another typed helper so
   overload resolution cannot silently fall back through `FromObjectUnsafe`.
   For C# collection expressions or array literals that combine host primitives
   and `JsValue` values, force the element type with `new JsValue(...)` members
   or `new JsValue[] { ... }` so constructor overload resolution stays on the
   `IEnumerable<JsValue>` path.
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
9. When deleting an `object?` helper cluster backed by pools, remove the whole
   dead carrier surface: helper methods, caller-side rent/return hooks, and
   private pool fields. Prove it with a targeted symbol search for both helper
   names and backing storage names; a method-only no-caller search can still
   leave unused `ObjectPool<object?[]>` fields behind.
10. When a private runtime helper builds and returns a JavaScript value for a
    caller that already requires `JsValue`, migrate the helper return type to
    `JsValue` and delete caller-side `JsValue.FromObjectUnsafe(...)` rewraps.
    Keep adjacent async, promise, or host-interop helpers separate unless the
    selected slice proves that boundary too.
11. When replacing untyped `JsValue.TryGetObject(out object?)` extraction, audit
    the old payload surface before narrowing to a concrete runtime type. If the
    old object-carrier branch accepted interface-backed host or object-like
    payloads, split the typed extraction into a concrete branch and an explicit
    interface fallback, for example `TryGetObject<JsObject>` followed by
    `TryGetObject<IJsPropertyAccessor>`. Do not silently collapse a previous
    accessor-compatible path to `JsObject` only.
12. When moving legacy object-carrier string coercion into `JsOps`/`JsValue`
    helpers, preserve exotic object classification before ordinary host
    interface shape. In particular, handle `IIsHtmlDda` before callable,
    accessor, host-function, or generic object fallback branches so HTMLDDA-like
    values stringify as `"undefined"` even when they also have callable or
    accessor shape. Pair this with a focused proof on the shared helper, because
    array stringification and other callers inherit the behavior from that
    helper.
13. When a private completion carrier temporarily stores JavaScript return or
    throw payloads across control-flow cleanup, keep that carrier typed as
    `JsValue` end to end. Use a separate presence flag for "no pending payload"
    and reset the payload slot with `JsValue.Undefined`; do not use `object?`,
    `null`, or `JsValue.FromObjectUnsafe(...)` as a private save/restore bridge
    unless the path is an explicit public, host interop, debugger, or diagnostic
    boundary.
14. When a private iterator driver stores the result of a JavaScript
    `next()` call or an enumerated JavaScript value for later loop binding, keep
    that temporary carrier typed as `JsValue`. Use typed extraction such as
    `TryGetObject<IJsObjectLike>(...)` for iterator-result objects and pass the
    carried value directly to loop binding; do not unwrap to `object?` and then
    rewrap with `JsValue.FromObjectUnsafe(...)`.
15. When constructing `PropertyDescriptor` data descriptors for JavaScript
    values inside the core runtime or standard library, assign the `JsValue`
    property directly. Treat the `Value` compatibility setter as an
    object-carrier bridge because it routes through `JsValue.FromObjectUnsafe`.
    Prove descriptor migrations with a scoped before/after search that
    distinguishes legacy `Value =` setters from `JsValue =` setters, for
    example `\bValue\s*=` in the selected file set, and pair it with the
    focused semantic proof for that descriptor cluster.
16. When a private runtime property-read helper already has a JavaScript
    receiver, keep the context-aware read path typed as `JsValue` for both the
    receiver and the returned value. Do not unbox primitive receivers into CLR
    payloads such as `bool`, `double`, `string`, `JsBigInt`, or general
    `object?` before prototype/accessor lookup and then rewrap the result.
    Preserve the active `EvaluationContext?` so accessors, proxies, and
    JavaScript throws keep propagating on the same path. If an unmigrated
    legacy branch must still call the typed helper, make its
    `JsValue.FromObjectUnsafe(...)` conversion explicit at that branch and keep
    the branch as a remaining migration target.

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

Issue `autrun-dirzigpmcp40-968f785da0` / PR #1914 found that guarded `JsArray`
legacy overloads can still be selected by mixed collection expressions such as
`[key, value]` where one element is a host string and another is already a
`JsValue`. The fix kept `Object.entries` and `JsMap.Entries()` pair arrays on
the `JsValue` path by typing the host string with `new JsValue(key)` and typing
the map pair literal as `new JsValue[] { key, GetByKey(key) }`. Future
`JsArray` caller migrations should audit mixed literals, not just direct
`object?` arguments, because collection-expression inference can otherwise
route core runtime values through the obsolete object-carrier constructor.

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

Issue `autrun-dir3tzc35h6w-a5094b82c5` / PR #1728 removed the legacy dynamic
call `object?[]` argument-array helper surface from `JsValueCache` after the
call path had moved to pooled `JsValue[]` arrays. Review still found the
private `ObjectPool<object?[]>` fields (`Pool1`-`Pool4`) after the helper
methods were gone, which showed that object-carrier cleanup must include both
callable symbols and backing storage. Future core `JsValue` migrations should
scan for the old helper names and the old storage type/name pattern before
claiming the object carrier has been removed.

Issue `autrun-dir4l5ptszpk-836da057ea` / PR #1738 migrated the synchronous
Array static helper result boundary for `Array.of`, synchronous `Array.from`,
and iterable `Array.from`. The helpers already constructed JavaScript arrays and
their callsites immediately rewrapped the `object?` result into `JsValue`, so
the durable lesson is to let the private helper own the typed return value and
return it directly from the host/constructor callsite. `Array.fromAsync` stayed
out of scope because its promise/async path is a separate boundary that needs
its own focused proof.

Issue `autrun-dirl74f4ybwo-4a2a4b907a` / PR #1765 migrated the internal
`JsArray.SetLength` helper boundary by adding a `JsValue` overload and marking
the legacy `SetLength(object?, ...)` overload obsolete with `error: true`.
Array length assignment already receives `JsValue` values from core property
assignment paths, so routing through `JsValue.FromObjectUnsafe(...)` only kept
an accidental object-carrier entry point alive. Future array length work should
preserve `TrySetArrayLength(...)` as the spec-owned conversion/failure path, but
keep the public helper boundary typed to `JsValue` and prove accidental object
binding is gone with the focused `SetLength(object?)` signature search. Related
ADR: `docs/adrs/0114-keep-array-length-helper-jsvalue-native.md`.

Issue `autrun-diro77wbl75s-869c762677` / PR #1784 migrated `JsMap` key storage
from extracted CLR `object` keys plus `null`/`undefined` side channels to
`JsValue` keys with `SameValueZeroComparer.JsValueInstance`. The deterministic
quality regression showed that a `JsValue`-native comparer alone is not the
whole storage contract: numeric `-0` could still remain in the insertion-order
record and become observable through `Map.groupBy` or iterators. Future keyed
collection migrations should canonicalize storage keys at the owning `Set`/
`Get`/`Has`/`Delete` boundary and prove both lookup behavior and observable
stored-key behavior. Related ADR:
`docs/adrs/0115-keep-jsmap-key-storage-jsvalue-native.md`.

Issue `autrun-dirph7vxdbdc-edfa353492` / PR #1798 migrated the internal Array
prototype `ReduceLike`/`SomeLike` result helpers from `object?` to `JsValue` and
removed caller-side `JsValue.FromObjectUnsafe(...)` rewraps in `reduce`,
`reduceRight`, and `some`. The old boundary did not preserve host interop or
compatibility; it only converted JavaScript values and primitive booleans
through CLR object carriers before returning to `JsValue` host methods. Future
Array prototype helper migrations should keep the helper result typed once the
selected callsites all require `JsValue`, and prove cleanup with a focused
legacy-signature/wrapper search. Related ADR:
`docs/adrs/0118-keep-array-reduce-some-result-helpers-jsvalue-native.md`.

Issue `autrun-dirquxdu7e2w-b1e0d8c752` / PR #1810 migrated
`NumberPrototype.RequireNumberReceiver` away from untyped object extraction.
Review feedback showed the important compatibility edge: replacing
`TryGetObject(out object?)` with only `TryGetObject<JsObject>` would remove
non-`JsObject` payloads that still implement `IJsPropertyAccessor` and were
accepted by the old path. Future object-extraction migrations should use typed
`JsValue` access without narrowing away interface-backed object semantics.
Related ADR:
`docs/adrs/0123-keep-number-receiver-object-extraction-typed-and-accessor-compatible.md`.

Issue `autrun-dirtf04x6gtk-01ca431d93` / PR #1907 migrated array string
coercion onto the shared `JsOps.ToJsString` object-value helper. Review found
that placing the `IIsHtmlDda` branch after callable/accessor branches made an
HTMLDDA-like value stringify as native function text instead of `"undefined"`.
Future object-carrier string-coercion migrations must preserve the legacy
exotic-object branch precedence while moving the helper to `JsValue`. Related
ADR:
`docs/adrs/0141-keep-htmldda-string-coercion-precedence-in-jsops.md`.

Issue `autrun-dis251ifyqog-d9eb7698a5` / PR #1934 migrated
`GeneratorPendingCompletion.Value` from `object?` to `JsValue`. The old pending
completion path saved generator return and throw payloads as untyped objects
while `finally` executed, then restored them through a defensive
`pending.Value is JsValue ... JsValue.FromObjectUnsafe(pending.Value)` bridge.
That bridge was not a compatibility boundary; it was a private completion
payload slot that already captured `JsValue` values. Future completion-carrier
migrations should preserve a separate pending flag, keep the payload slot
typed as `JsValue`, and reset it with `JsValue.Undefined`. Related ADR:
`docs/adrs/0143-keep-generator-pending-completion-payloads-jsvalue-native.md`.

Issue `autrun-dis3ezcxvtww-2f5a72bcac` / PR #1945 migrated the for-of iterator
driver's private `nextResult` local from `object?` to `JsValue`. The old driver
called iterator `next()` into a JavaScript value, unwrapped object results into
CLR objects for protocol handling, then rewrapped the fallback loop value with
`JsValue.FromObjectUnsafe(...)`. That was not a public or host-interop
boundary; it was a private loop-carrier inside the evaluator. Future iterator
driver migrations should keep the carrier `JsValue`-native, use typed object
extraction for iterator-result records, and prove cleanup with focused searches
for the legacy carrier name, `nextResult is JsValue`, and
`FromObjectUnsafe(nextResult)`.

Issue `autrun-dis4ox75yk7c-4a701ae598` / PR #1952 marked
`JsValueExtensions.ToNumber(this object?)` and
`JsValueExtensions.ToJsString(this object?, ...)` obsolete with
`error: true`, then migrated the remaining exposed internal test callsites to
`JsOps.ToNumber(...)` and `JsOps.ToJsString(...)`. The lesson is that even
low-risk test helpers can keep legacy extension-style object coercion callable
inside the repo; future bounded slices should let the compiler expose those
callers, migrate them to shared `JsOps` operations, and keep the proof as a
targeted before/after search plus the focused semantic tests for the touched
callers.

Issue `autrun-dis5yv12l95s-27dcaf0dbf` / PR #1959 migrated the StdLib/Error
`PropertyDescriptor` initializers from the legacy `Value` compatibility setter
to `JsValue`. The implementation was intentionally mechanical, but review
required a build re-entry because the initial evidence did not include explicit
baseline and final signals for the scoped descriptor setter search. Future
descriptor migrations should keep JavaScript data values on the `JsValue`
setter and record the before/after legacy-setter signal in the delivery
evidence so reviewers do not have to reconstruct whether the selected slice was
fully migrated.

Issue `autrun-dis8iqp0agxk-43776f3f25` / PR #1984 applied the same descriptor
setter migration to `JsArgumentsObject` constructor setup. Even in a tiny
three-site slice, `length`, internal marker, and mapped `callee` descriptor
values are core JavaScript data values, not host-interop boundaries. Future
arguments-object or descriptor cleanup should keep these constructor
initializers on `JsValue =`, use an explicit conversion only for unavoidable
legacy object payloads, and prove the slice with a scoped `\bValue\s*=` search
plus the focused arguments-object tests.

Issue `autrun-dis78svbpuvk-6736b1535b` / PR #1966 migrated the `JsOps`
context-aware property-read flow from the legacy object-carrier overload to a
`JsValue` receiver/return path on `JsObject.TryGetProperty`. The old branches
passed extracted CLR receivers such as `target.AsBoolean()` or
`target.NumberValue` into prototype lookup, then normalized the result through
defensive `value is JsValue ... JsValue.FromObjectUnsafe(...)` bridges. That
was not a compatibility boundary; it was a private runtime property-access flow
that already had a `JsValue` receiver and active `EvaluationContext?`. Future
property-access migrations should keep receivers and results typed through the
context-aware helper, and prove cleanup with a before/after search for the
legacy `TryGetProperty(..., object?, context, out ...)` shape. Related ADR:
`docs/adrs/0148-keep-context-property-reads-jsvalue-receiver-native.md`.
