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
  - `docs/adrs/0115-keep-jsmap-key-storage-jsvalue-native.md`
  - `docs/adrs/0118-keep-array-reduce-some-result-helpers-jsvalue-native.md`
  - `docs/adrs/0123-keep-number-receiver-object-extraction-typed-and-accessor-compatible.md`
  - `docs/adrs/0143-keep-generator-pending-completion-payloads-jsvalue-native.md`
  - `docs/adrs/0148-keep-context-property-reads-jsvalue-receiver-native.md`
  - `docs/adrs/0153-keep-destructuring-toobject-coercion-jsvalue-native.md`
  - `docs/adrs/0155-keep-propertydescriptor-data-values-jsvalue-native.md`
  - `docs/adrs/0164-keep-super-constructor-resolver-jsvalue-native.md`
  - `docs/adrs/0168-keep-executeprogram-jsvalue-native.md`
  - `docs/adrs/0182-keep-module-namespace-own-keys-jsvalue-native.md`
  - `docs/adrs/0191-keep-weakmap-value-storage-jsvalue-native.md`
  - `docs/adrs/0196-keep-intl-receiver-brand-validation-jsvalue-native.md`
  - `docs/adrs/0198-keep-array-fromasync-result-helper-jsvalue-native.md`
  - `docs/adrs/0203-keep-runner-symbol-stores-jsvalue-native.md`
  - `docs/adrs/0212-keep-typed-module-execution-helper-jsvalue-native.md`
  - `docs/adrs/0220-keep-assignment-property-receivers-jsvalue-native.md`
  - `docs/adrs/0223-keep-typedarray-constructor-result-jsvalue-native.md`
  - `docs/adrs/0232-keep-sync-function-this-binding-jsvalue-native.md`
  - `docs/adrs/0240-keep-jsops-property-lookup-receivers-jsvalue-native.md`
  - `docs/adrs/0244-keep-weak-key-validation-shared-and-primitive-strict.md`

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
   collection-like helpers), migrate the owning storage contract as one unit.
   For collections that own equality, migrate storage and equality comparer
   together. A `JsValue`-backed collection must use `JsValue`-native
   SameValueZero or spec equality semantics directly, including `NaN`,
   positive/negative zero, ordinal strings, symbols, and object identity. Keep
   legacy `object?` comparers only for collection owners that still store
   `object?`; do not route migrated storage through `ToObject`,
   `FromObjectUnsafe`, or side-channel sentinels for `null`/`undefined`.
   For CWT-backed weak collections, keep the weak-key object identity boundary
   intentional, but keep weak-key validation primitive-strict before inspecting
   object payloads. `JsWeakCollectionHelpers.ExtractWeakKeyObject(...)` is the
   shared boundary for `WeakMap`, `WeakSet`, and `FinalizationRegistry`: reject
   `null`, `undefined`, strings, numbers, booleans, registered symbols, and
   BigInt primitives even though BigInt is backed by a `JsBigInt` object
   payload; accept only object identities and unregistered symbols. Do not treat
   the value side as permission to box JavaScript values. Store `WeakMap`
   values as `JsValue` inside a private reference wrapper, use `TryGetValue` to
   separate presence from a stored `JsValue.Undefined`, and treat `WeakSet` CWT
   values as presence sentinels rather than JavaScript values unless a separate
   focused slice proves a new storage contract. Related ADRs:
   `docs/adrs/0191-keep-weakmap-value-storage-jsvalue-native.md` and
   `docs/adrs/0244-keep-weak-key-validation-shared-and-primitive-strict.md`.
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
    selected slice proves that boundary too. Once a promise-producing helper
    proves that every setup branch returns the same JavaScript promise object to
    a `JsValue` host-function callsite, wrap the promise at the helper return
    boundary and return the helper result directly from the callsite. When a
    fallback helper calls an intrinsic that already returns `JsValue`, such as
    `%Object.prototype.toString%`, keep both the helper input and result typed as
    `JsValue`; wrap the object/accessor receiver once at the caller boundary and
    do not route the fallback through `object?` just because the receiver is
    object-like.
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
    Builtin metadata descriptors in standard-library setup and global bootstrap
    descriptors in `JsEngine`, including `name`, `length`, symbols,
    constructor/prototype method properties, `Array`, `BigInt`, `Infinity`,
    `NaN`, `undefined`, Intl namespace constructor tables, `supportedLocalesOf`
    metadata, local Temporal shims, local error-object data properties such as
    `SuppressedError` `_errorData` / `error` / `suppressed` / `message`, and
    stateful builtin data slots such as RegExp `lastIndex`, are still JavaScript
    data descriptors; use `new JsValue(...)`, `JsValue.True`, or an explicit
    object wrapping helper instead of hiding that conversion behind `Value =`.
    Object-literal dictionary spread descriptors in both
    `TypedAstEvaluator.ExecutionPlanRunner.Helpers` and
    `Ast/Legacy/ExpressionNodeExtensions` are the same kind of core data
    descriptor sink: `IDictionary<string, object?>` is the intentional interop
    source shape, but copied values should enter `PropertyDescriptor` through
    `JsValue = JsValue.FromObjectUnsafe(...)`, not the `Value` compatibility
    setter.
    Prove descriptor migrations with a scoped before/after search that
    distinguishes legacy `Value =` setters from `JsValue =` setters, for
    example `\bValue\s*=` in the selected file set, and pair it with the
    focused semantic proof for that descriptor cluster. For helper migrations,
    prove the helper body as well as the signature and callsites: changing a
    parameter to `JsValue` is incomplete if an initializer still says
    `Value = value`. When the selected owner surface has mirrored IR and
    legacy/dynamic evaluator branches, include both branches in the legacy-setter
    search and either update both or document the intentional divergence. Do not
    turn the compatibility setter into error-level
    obsoletion inside a small descriptor slice when the exposed callers are
    repo-wide or include generated code; record that deferral and keep the
    bounded migration moving.
    WHY: issue `autrun-diu14wv8korc-df99b99081` / PR #2449 initially migrated
    only the IR object-literal dictionary spread branch. Review had to send the
    slice back because the mirrored legacy/dynamic branch still used
    `Value = kvp.Value`, which violated AC-2 even though the production runner
    diff looked complete.
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
    When a helper receives both an extracted object/accessor payload and the
    original JavaScript receiver value, such as
    `TryInvokeSymbolMethod(this IJsPropertyAccessor target, JsValue thisArg,
    ...)`, use the original `JsValue` receiver for `JsOps` property lookup.
    The extracted payload may prove shape or dispatch interface behavior, but
    do not reconstruct a lookup receiver from it with
    `JsValue.FromObjectUnsafe(target)`. Preserve existing symbol key fallback
    order and callable invocation `this` binding when cleaning up
    Get/GetMethod-style helpers.
    WHY: issue `autrun-ditlbq6xugc0-9d0cb22469` / PR #2432 removed the private
    `TryGetPropertyValueObject(object? target, ...)` bridge from
    `Runtime/JsOps.cs` after the helper was only reconstructing the original
    JavaScript receiver with `JsValue.FromObjectUnsafe(target)`. Reintroducing
    a private object-carrier property-read helper would undo that Unboxer slice
    and risk losing accessor/prototype receiver identity.
    WHY: issue #2451 / PR #2464 found the same object-carrier seam one hop
    away from `Runtime/JsOps.cs`: `TryInvokeSymbolMethod` already had the
    original iterable `JsValue` as `thisArg`, but still rebuilt the lookup
    receiver from the extracted `IJsPropertyAccessor` target. Future adjacent
    property-lookup cleanup must keep the caller-owned receiver value as the
    lookup receiver instead of treating the extracted payload as a compatibility
    boundary.
    Related ADR:
    `docs/adrs/0240-keep-jsops-property-lookup-receivers-jsvalue-native.md`.
17. When obsolete `object?` convenience overloads on a core runtime type no
    longer expose real compatibility callers or an intentional host boundary,
    delete the overloads rather than keeping them as permanent tripwires. For
    property-read helpers, remove the whole duplicate family together:
    context-aware lookup, own-property lookup, prototype traversal, and getter
    bridge helpers. For `JsArray` constructor, element-write, push, or length
    helpers, the typed `JsValue` surface is the core contract; prove removal
    with a focused signature search for the retired overload family.
18. When a private destructuring helper receives a `JsValue` and needs
    `ToObject`/primitive-boxing semantics, call the shared
    `StandardLibrary.TryGetObject(JsValue, realm, out ...)` path directly.
    Do not manually unwrap booleans, numbers, strings, symbols, bigints, or
    object payloads into CLR values before calling the legacy `object?`
    overload; that recreates an object-carrier bridge inside evaluator code
    that already has the JavaScript value and can drift from the shared
    coercion helper. Prove these slices with the destructuring-focused legacy
    switch search and focused destructuring tests.
19. When a private resolver can fail to find an optional JavaScript value but
    successful callers already require `JsValue`, expose absence separately
    from the payload, for example with a `bool Try...(..., out JsValue value)`
    contract or an equivalent typed result. Do not keep a nullable `object?`
    payload, use `null`, or use `JsValue.Undefined` as the only no-value signal
    when callers must preserve an existing missing-value error branch. Keep any
    unavoidable `JsValue.FromObjectUnsafe(...)` wrapping inside the resolver
    boundary for known runtime objects and prove the caller-side rewraps are
    gone.
20. When private program/script/eval or typed module execution wrappers feed
    callsites that already consume JavaScript values, keep the wrapper and
    immediate callsites typed as `JsValue`. Public `Evaluate*` facades may
    unwrap at the API edge. Module-body result storage, `ModuleEntry.LastValue`,
    async module runner last-value storage, stored module evaluation tasks, and
    internal dependency-drain task lists are now selected typed module owner
    surfaces and must stay `JsValue`-native. Do not call
    `EvaluateProgram(...)`, private typed statement/expression `object?`
    wrappers, use private module last-value storage as `object?`, or return
    `object?` from private execution plumbing only to immediately rewrap with
    `JsValue.FromObjectUnsafe(...)`; prefer `EvaluateProgramJsValue(...)`, a
    `JsValue`-returning `ExecuteProgram`, `ExecuteModuleBody(...)`,
    `ExecuteTypedStatementJsValue(...)`, `ExecuteTypedExpressionJsValue(...)`,
    or another typed execution helper. Convert with the local legacy object
    adapter only at public `object?` facade or edge-returning `Task<object?>`
    adapter boundaries; do not store module evaluation completion as
    `Task<object?>` when the payload is a private JavaScript value.
    Benchmark, test, profiling, or diagnostic harnesses that bypass public
    facades and invoke internal evaluator entrypoints by reflection are still
    repo-internal execution callers; keep them on `JsValue` too, and unwrap only
    at an explicit reporting edge. Prove error-level obsolete-wrapper changes
    with a repo-internal callsite scan that covers `src`, `tests`, `benchmarks`,
    and `tools`, plus focused eval/Function/ShadowRealm/module proof when
    behavior changes.
    WHY: issue `autrun-ditfw6gh2qag-7ade4a3977` / PR #2364 removed the last
    private `ExecuteTypedExpression(...)` `object?` adapter in `JsEngine.cs`
    after the remaining module and async-module callers moved to
    `ExecuteTypedExpressionJsValue(...)`. Reintroducing that bridge would hide a
    core-runtime JavaScript value behind a legacy object carrier and undo the
    focused Unboxer cleanup.
    WHY: issue `autrun-ditjxyki91ew-7082b9173d` / PR #2403 removed the last
    private `ExecuteTypedStatement(...)` `object?` adapter in `JsEngine.cs`
    after the remaining module and async-module statement callers moved to
    `ExecuteTypedStatementJsValue(...)`. Reintroducing that bridge would reopen
    a private typed module execution seam that now has no internal compatibility
    caller.
    WHY: issue `gh2372` / PR #2380 completed the ADR 0212 follow-through by
    moving `ModuleEntry.LastValue`, `ExecuteModuleBody(...)`, and
    `AsyncModuleBodyRunner._lastValue` to `JsValue` while keeping public
    `object?` APIs as adapter boundaries. Reintroducing object-shaped module
    last-value storage would reopen the same private compatibility seam.
    WHY: issue `gh2374` / PR #2383 completed the adjacent module evaluation
    task seam by moving `ModuleEntry.EvaluationTask`, async module body
    completions, and dependency-drain task lists to `Task<JsValue>`. Keeping
    those stored tasks object-shaped would recreate a private carrier bridge
    immediately next to the typed `LastValue` owner surface.
    Related ADRs:
    `docs/adrs/0168-keep-executeprogram-jsvalue-native.md`,
    `docs/adrs/0212-keep-typed-module-execution-helper-jsvalue-native.md`.

    Issue `autrun-diuvt2ksxyuw-47f4dafb7f` / PR #2635 extended this principle by
    removing the `ConvertJsValueToLegacyObject` bridge helper in `JsEngine.cs` and
    migrating `EvaluateInline`, `EvaluateSyncInternal`, `EnsureModuleEvaluatedAsync`,
    `CompleteEvaluationAfterSynchronousExecution`, and
    `DrainPendingEventLoopAndCompleteAsync` from `object?` to `JsValue`. The bridge
    helper had no legitimate object-carrier callers; it converted `JsValue` results to
    `object?` only for private pipeline steps that immediately needed `JsValue` again.
    Deleting the bridge and typing the pipeline end to end is correct; preserve only
    intentional host/generated-code bridge overloads such as `SetGlobal(string, object?)`
    that cannot be removed without changing code generation. Related ADR:
    `docs/adrs/0281-keep-jsengine-evaluation-pipeline-helpers-jsvalue-native.md`.
21. When a private module namespace or property-key enumeration helper feeds
    JavaScript reflection/object APIs, keep the key sequence typed as
    `JsValue`. Preserve both string keys and symbol keys at the owner boundary;
    do not expose `IEnumerable<object?>`, require every consumer to call
    `JsValue.FromObjectUnsafe(...)`, or filter symbols by raw `JsSymbol`
    pattern matching. Prove the migration with a scoped legacy-signature search
    plus focused `Reflect.ownKeys` and `Object.getOwnPropertySymbols` coverage.
    Related ADR:
    `docs/adrs/0182-keep-module-namespace-own-keys-jsvalue-native.md`.
22. When a runner helper stores a value by `Symbol`, keep the helper contract on
    the `JsValue` path and delete private `object?` compatibility shims once a
    targeted callsite search shows no intentional object-valued JavaScript
    payloads remain. If the slot intentionally carries runner bookkeeping state
    such as a `YieldStarState` or dynamic `with` `JsEnvironment`, wrap that
    specific state object explicitly at the callsite with
    `JsValue.FromObjectUnsafe(...)` and read it back with typed extraction.
    Do not hide both JavaScript values and internal state objects behind a
    generic store helper that accepts `object?`. Related ADR:
    `docs/adrs/0203-keep-runner-symbol-stores-jsvalue-native.md`.
23. When a private predicate or inspector only examines a protocol object after
    callers have already proven the concrete runtime shape, narrow the helper
    parameter to that concrete type, for example `JsObject`, and remove the
    defensive `candidate is JsObject` object-carrier check. Keep the
    `TryGetObject` or symbol-iterator proof at the caller boundary and use
    nullable-flow annotations such as `[NotNullWhen(true)]` on success-returning
    `out` helpers when needed. Do not use this shape to silently drop
    interface-backed object semantics that the old helper accepted; if the
    legacy path accepted multiple object-like shapes, split explicit typed
    branches per rule 11. Prove the slice with the scoped legacy-signature
    search and focused protocol tests.
24. When a private assignment property-write helper receives both the original
    JavaScript target and an extracted runtime object, keep the receiver
    parameter typed as `JsValue` and pass the original target `JsValue` when it
    is the receiver. Do not pass the extracted `JsObject` through an `object?`
    receiver bridge and rewrap it inside the resolver. If the receiver is
    optional, default it to the target in the helper so global/default writes
    preserve existing behavior. WHY: issue `autrun-dit8gewlbljs-23ab36dc9e` /
    PR #2305 removed `AssignObjectProperty(..., object? receiver = null)`;
    setter/proxy receiver identity belongs to the JavaScript value, not to the
    extracted descriptor target. Related ADR:
    `docs/adrs/0220-keep-assignment-property-receivers-jsvalue-native.md`.
25. When a typed-array constructor helper builds the concrete typed-array object
    for a host constructor that returns `JsValue`, keep the helper result typed
    as the concrete `TypedArrayBase` subtype or as `JsValue`; do not return
    `object?` only to rewrap with
    `JsValue.FromObjectUnsafe(ConstructTypedArray(...))` at the callsite. Keep
    all `newTarget`, prototype resolution, from-length, from-buffer,
    from-typed-array, and from-array-like branches on the same shared helper so
    every concrete typed-array constructor follows the same object-identity and
    prototype behavior. WHY: issue `autrun-dit9qcqe3mzs-470462a366` / PR #2317
    found that `ConstructTypedArray(...)` already returned concrete typed-array
    instances with existing `TypedArrayBase` to `JsValue` conversion support,
    but the helper's `object?` return type kept an avoidable constructor result
    bridge alive. Related ADR:
    `docs/adrs/0223-keep-typedarray-constructor-result-jsvalue-native.md`.

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

Issue `autrun-disubnwdoxb4-4df588ff17` / PR #2197 closed that async-specific
Array static helper gap by migrating `ArrayFromAsync(...)` from `object?` to
`JsValue`. The old helper always returned the created promise object and the
attached `fromAsync` host function immediately rewrapped the result with
`JsValue.FromObjectUnsafe(...)`; the delivery moved that wrapping into the
helper return points and returned the helper value directly at the callsite.
Future promise-producing helper cleanup should keep async scheduling and
rejection semantics unchanged while removing only the private object-carrier
bridge. Related ADR:
`docs/adrs/0198-keep-array-fromasync-result-helper-jsvalue-native.md`.

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

Issue #2052 / PR #2060 showed that array length cleanup is incomplete when the
helper signature is typed but fallback descriptors still use
`PropertyDescriptor.Value = (double)_length`. The `Value` setter routes through
`JsValue.FromObjectUnsafe(...)`, so the fix migrated the three `JsArray` length
descriptor initializers to `JsValue = JsValue.FromDouble(_length)` and added a
focused `Object.getOwnPropertyDescriptor(a, "length")` value/attribute
regression. Future array length and descriptor cleanup should include both the
helper signature scan and the owner-file `Value = (double)_length` /
`Value = value` setter scan before claiming the object carrier is gone. Related
ADRs: `docs/adrs/0114-keep-array-length-helper-jsvalue-native.md` and
`docs/adrs/0155-keep-propertydescriptor-data-values-jsvalue-native.md`.

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

Issue `autrun-disqx5obgqv4-52e407689b` / PR #2163 migrated `JsWeakMap` value
storage from `ConditionalWeakTable<object, object?>` plus
`ExtractValueObject(...)`/`JsValue.FromObjectUnsafe(...)` conversions to a
reference wrapper carrying `JsValue`. The weak-key object extraction stayed
intentional because CWT weak identity requires reference keys, and the wrapper
exists only because CWT values must also be reference types. Future weak
collection object-to-`JsValue` cleanup should preserve that split: object
identity for weak keys, `JsValue` for JavaScript values, and `TryGetValue`
presence checks so stored `undefined` is not confused with absence. Related ADR:
`docs/adrs/0191-keep-weakmap-value-storage-jsvalue-native.md`.

Issue `autrun-diu14wsfwgjc-670bfedf98` / PR #2452 deduplicated
`FinalizationRegistry` weak-key validation onto
`JsWeakCollectionHelpers.ExtractWeakKeyObject(...)` and then fixed the shared
helper to reject BigInt primitives. The recurrence risk was `JsValue.ObjectValue`:
BigInt is primitive in JavaScript semantics but backed by a `JsBigInt` object
payload, so helper code that unwraps payloads before checking the `JsValue` tag
can accidentally accept `1n` as weakly held. Future weak-reference cleanup
should keep WeakMap, WeakSet, and FinalizationRegistry on the shared helper and
prove primitive rejection across all three consumers. Related ADR:
`docs/adrs/0244-keep-weak-key-validation-shared-and-primitive-strict.md`.

Issue `autrun-dirph7vxdbdc-edfa353492` / PR #1798 migrated the internal Array
prototype `ReduceLike`/`SomeLike` result helpers from `object?` to `JsValue` and
removed caller-side `JsValue.FromObjectUnsafe(...)` rewraps in `reduce`,
`reduceRight`, and `some`. The old boundary did not preserve host interop or
compatibility; it only converted JavaScript values and primitive booleans
through CLR object carriers before returning to `JsValue` host methods. Issue
`autrun-diuf7ylrunc0-b01d02b8e2` / PR #2568 later deleted `SomeLike` entirely
while keeping the `some` result on a local `JsValue` predicate path. Future
Array prototype helper migrations should keep the helper result typed once the
selected callsites all require `JsValue`; do not reintroduce historical helper
names or object bridges when a local `JsValue` helper already owns the path.
Prove cleanup with a focused legacy-signature/wrapper search. Related ADR:
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

Issue `autrun-dithe2u1zgyg-28b1ea4e79` / PR #2371 completed that
`JsValueExtensions` closeout by deleting the obsolete helper file after focused
reference searches showed no live `JsValueExtensions`, `.ToNumber(...)`,
`.ToJsString(...)`, or `.ToJsStringForArray(...)` callsites outside the file.
Future recurring code-reduction slices should treat error-level obsolete
object-carrier wrappers as temporary tripwires only: after internal callers are
migrated and the scoped signature search is clean, delete the whole obsolete
helper family and prove the closeout with before/after search evidence plus the
canonical quality gate. WHY: leaving a dead wrapper file makes future agents
rediscover or revive stale object-coercion logic instead of using the shared
`JsValue`/`JsOps` conversion operations.

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

Issue `autrun-discmtujl4ko-18deddaa26` / PR #2008 migrated another bounded
`PropertyDescriptor.Value` cluster in `JsOps`, `JsPrototype`, and tagged
template array construction. The accepted delivery removed all scoped
`Value =` descriptor initializers in those files, but deliberately deferred
`[Obsolete(..., true)]` on the compatibility setter because that pressure would
fan out through remaining repo-wide callsites, including generated code, and
turn a nine-line descriptor cleanup into an unbounded migration. Future
descriptor migrations should keep using the scoped before/after setter search,
move only proven JavaScript data values to `JsValue =`, and save strict setter
obsoletion for a dedicated repository-wide closeout. Related ADR:
`docs/adrs/0155-keep-propertydescriptor-data-values-jsvalue-native.md`.

Issue `autrun-disdwrowfjf4-2cfa8decbc` / PR #2024 migrated
`StandardLibrary.DefineConstantProperty` from `object?` to `JsValue`. Review
rejected the first build because the helper signature and fallback
`SetProperty` call were typed, but the descriptor initializer still used
`Value = value`, which invoked the compatibility setter and
`JsValue.FromObjectUnsafe(value)`. Future descriptor-helper migrations must
prove every sink in the helper body, not just the exposed signature or callsite
compilation; include the owner-file `Value = value` /
`FromObjectUnsafe(value)` search in the delivery evidence. Related ADR:
`docs/adrs/0155-keep-propertydescriptor-data-values-jsvalue-native.md`.

Issue `autrun-disf6pjgjrf4-4b2f8950be` / PR #2044 applied the same descriptor
setter policy to async-generator intrinsic setup in
`TypedAstEvaluator.AsyncGeneratorFunctionInvoker`. The important lesson is that
prototype and constructor descriptors created while wiring async-generator
intrinsics are still core JavaScript data values, not host-interop exceptions:
strings, prototypes, and constructors should use `JsValue = ...` directly, and
an invoker self-reference should make the object bridge explicit with
`JsValue.FromObjectUnsafe(this)` instead of hiding it behind `Value = this`.
Future async function/generator intrinsic descriptor cleanup should preserve
writable/enumerable/configurable attributes exactly and prove the selected file
with a scoped legacy-setter search plus focused async-generator tests. Related
ADR: `docs/adrs/0155-keep-propertydescriptor-data-values-jsvalue-native.md`.

Issue `autrun-ditb0akizpyw-733760a3b3` / PR #2331 applied the descriptor setter
policy to `JsEngine` global bootstrap descriptors for `Array`, `BigInt`,
`Infinity`, `NaN`, and `undefined`. The `SetGlobal(...)` calls did not make the
paired `DefineProperty(...)` descriptors public or host-interop boundaries; the
five data descriptors still belong on the `JsValue` setter with attributes
preserved exactly. Future global bootstrap cleanup should prove the scoped
legacy-setter search before and after the edit rather than treating duplicated
global registration as an exception. Related ADR:
`docs/adrs/0155-keep-propertydescriptor-data-values-jsvalue-native.md`.

Issue `autrun-diteietq3894-3b695787e6` / PR #2343 applied the descriptor setter
policy to `StdLib/Intl/IntlHelper`. Intl namespace constructor descriptors,
`supportedLocalesOf` metadata, and the local Temporal.Duration shim are
standard-library JavaScript data descriptors, so `Value = ...` was only a hidden
`JsValue.FromObjectUnsafe(...)` bridge. Future Intl helper cleanup should keep
the scoped before/after legacy-setter search and preserve
`Writable`/`Enumerable`/`Configurable` exactly while migrating only the selected
descriptor cluster. Related ADR:
`docs/adrs/0155-keep-propertydescriptor-data-values-jsvalue-native.md`.

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

Issue `autrun-dispn7u6bsg0-178edda9cb` / PR #2145 completed the follow-up
closeout for `JsObject` by deleting the legacy internal `object? receiver`
lookup family after the active `JsValue` path already owned descriptor, getter,
prototype, and private-field reads. The removed methods included the
context-aware `TryGetProperty(... object? receiver ...)`,
`TryGetOwnProperty(... object? receiver ...)`,
`TryGetPropertyFromPrototypeChain`, and `InvokeGetterWithThrowHandling`
helpers. Keeping that duplicate traversal/getter stack would not protect a
public compatibility boundary; it would preserve a second private runtime path
for future property-read work to drift into. Future property-access closeouts
should prove the `JsValue` path with focused descriptor/private-field coverage,
then delete the full obsolete helper family once the targeted signature search
shows no live callers.

Issue `autrun-dis9sojas0zs-56ecc19548` / PR #1990 removed the obsolete
`JsArray(IEnumerable<object?>)`, `SetElement(..., object?)`, `Push(object?)`,
and `SetLength(object?)` overloads after previous slices had already migrated
the core callers and used error-level obsoletion to expose stragglers. The
important closeout lesson is that once a core runtime type has complete
`JsValue` coverage and a focused signature search shows the old overload family
can disappear, keeping those overloads no longer protects compatibility; it
preserves an accidental object-carrier entry point for future runtime code.

Issue `autrun-diss73i9hwy8-488bb99c17` / PR #2192 applied the same closeout
decision to `IntlBrandExtensions.EnsureBrand`. Intl prototype receivers already
flowed through the `JsValue` overload, and the focused signature search showed
no live `EnsureBrand(this object? ...)` callers. The fix deleted the private
object-carrier receiver overload instead of leaving a permanent obsolete
tripwire. Future brand-validation cleanup should use `[Obsolete(..., true)]`
only to expose real callers or preserve an intentional boundary; when a private
helper already has complete `JsValue` coverage, remove the legacy overload and
prove the result with the scoped before/after search. Related ADR:
`docs/adrs/0196-keep-intl-receiver-brand-validation-jsvalue-native.md`.

Issue #2202 / PR #2207 showed that the shared Intl brand-helper cleanup is not
enough by itself: a prototype owner surface can still preserve a private
object-carrier hop after a `JsValue` brand check. `Intl.Collator` had
`ValidateCollatorReceiver(JsValue)` return `JsObject` and then
`GetSlots(JsObject)` read the slots. Future Intl receiver/slot cleanup should
look for this local two-step shape too, collapse it to an owner-local
`GetSlots(JsValue)` helper when the caller already has a `JsValue`, and prove
borrowed prototype methods for both branded receivers and incompatible
receivers. WHY: otherwise ADR 0196 can appear satisfied while local prototype
helpers keep the object-carrier seam alive. Related ADR:
`docs/adrs/0200-keep-intl-collator-slot-resolution-jsvalue-native.md`.

Issue `autrun-disb2mdhzcvs-0a8e873051` / PR #1996 migrated
`ToObjectForDestructuringJsValue` from a manual primitive unwrap switch plus
`StandardLibrary.TryGetObject(object?, ...)` to
`StandardLibrary.TryGetObject(JsValue, ...)`. The old branch converted the
already-typed destructuring value into CLR booleans/numbers/object payloads
before asking the standard library to box primitives. That was not a public,
host-interop, debugger, or diagnostic boundary; it was a private evaluator
coercion helper. Future destructuring coercion cleanup should stay on the
shared `JsValue` ToObject path and prove the slice with the focused
`ToObjectForDestructuringJsValue`/legacy primitive-switch search plus the
destructuring test pack. Related ADR:
`docs/adrs/0153-keep-destructuring-toobject-coercion-jsvalue-native.md`.

Issue `autrun-disgkh6pbz1k-90662a8047` / PR #2068 migrated
`ResolveSuperConstructorForCall` from a nullable `object?` return to
`TryResolveSuperConstructorForCall(..., out JsValue)`. The old resolver fed
both legacy AST and expression-bytecode `super(...)` callsites, and both
callers immediately rewrapped the nullable object result with
`JsValue.FromObjectUnsafe(dynamicSuperConstructor)`. Absence of a constructor
was the only non-value state, so the fix made absence explicit with a boolean
return while keeping the resolved callable/accessor payload typed. Future
optional resolver migrations should use the same shape instead of treating
`object?` nullability as a private JavaScript-value carrier. Related ADR:
`docs/adrs/0164-keep-super-constructor-resolver-jsvalue-native.md`.

Issue `autrun-disjor8mq4lk-f2e8b924c2` / PR #2087 migrated the private
`JsEngine.ExecuteProgram` seam from `object?` to `JsValue`. The old script/eval
wrapper called `EvaluateProgram(...)`, then direct eval, ShadowRealm evaluate,
the Function constructor, and dynamic generator constructors converted the
result back with `JsValue.FromObjectUnsafe(...)` or equivalent guards. That
boundary was not public interop; it was private execution plumbing. Future
program/eval execution cleanup should keep direct eval and dynamic constructor
callers on `EvaluateProgramJsValue(...)` or a `JsValue`-returning wrapper, while
preserving public `Evaluate*` unwrapping. Module `LastValue` storage has since
been selected and migrated; do not use this older script/eval slice as
permission to keep module last-value storage object-shaped. Related ADR:
`docs/adrs/0168-keep-executeprogram-jsvalue-native.md`.

Issue `autrun-dit4lwg5pkm0-72a23c5a9c` / PR #2260 migrated the selected typed
module execution helper path by adding `ExecuteTypedStatementJsValue(...)` and
`ExecuteTypedExpressionJsValue(...)` backed by `EvaluateProgramJsValue(...)`.
The old private helper path called `TypedAstEvaluator.EvaluateProgram(object?)`
and several async-module callsites immediately rewrapped
`ExecuteTypedExpression(...)` results with `JsValue.FromObjectUnsafe(...)`.
That was not a public or interop boundary; it was private module execution
plumbing. Future module execution cleanup should keep typed helper flows on
`JsValue`, use obsolete error-level wrappers only to expose remaining internal
callers, and keep module last-value storage `JsValue`-native now that the
follow-through slice selected it. Related ADR:
`docs/adrs/0212-keep-typed-module-execution-helper-jsvalue-native.md`.

Issue `gh2372` / PR #2380 selected the remaining typed module last-value owner
surface. The old module completion path stored `ModuleEntry.LastValue`,
`ExecuteModuleBody(...)` local completion values, and
`AsyncModuleBodyRunner._lastValue` as `object?`, even though the values were
private JavaScript statement completions. The fix kept those carriers typed as
`JsValue` and converted back only at public `Evaluate*` and `Task<object?>`
completion boundaries. Future typed module cleanup should treat object-shaped
module last-value storage as a regression, not as deferred work. Related ADR:
`docs/adrs/0212-keep-typed-module-execution-helper-jsvalue-native.md`.

Issue #2263 / PR #2264 showed why the execution-wrapper scan must include
repo-internal harnesses, not just production runtime files. After
`TypedAstEvaluator.EvaluateProgram(object?)` became error-level obsolete, the
BenchmarkDotNet `EvaluationOverheadBenchmarks` direct-AST methods still called
it through a reflection-driven internal evaluator path. That made
`rtk dotnet build Asynkron.JsEngine.sln` red because the benchmark project is
compiled by the default solution build. The fix changed those direct-evaluation
benchmark methods to return `JsValue` and call `EvaluateProgramJsValue(...)`,
leaving the obsolete wrapper as the only remaining `EvaluateProgram(` match.
Related ADR: `docs/adrs/0168-keep-executeprogram-jsvalue-native.md`.

Issue `autrun-disnitlzqrkw-d2a93ff0bf` / PR #2135 migrated
`ModuleNamespace.OwnKeys()` from `IEnumerable<object?>` to
`IEnumerable<JsValue>`. The old helper enumerated module namespace property
keys, but forced `Reflect.ownKeys` to wrap each key with
`JsValue.FromObjectUnsafe(...)` and forced `Object.getOwnPropertySymbols` to
pattern-match the raw key carrier as `JsSymbol`. That was not a public or
interop boundary; it was a private `[[OwnPropertyKeys]]` path feeding
JavaScript reflection APIs. Future module namespace own-key work should keep
string export names and `Symbol.toStringTag` typed through the owner helper,
then prove both mixed string/symbol reflection and symbol-only filtering.
Related ADR:
`docs/adrs/0182-keep-module-namespace-own-keys-jsvalue-native.md`.

Issue `autrun-disvllqpjvx4-8e3b36a053` / PR #2215 removed the execution-plan
runner's private `StoreSymbolValue(..., object?)` compatibility helper. The old
helper hid two different cases behind one object-carrier API: ordinary
JavaScript value stores that already had `JsValue`, and intentional runner
state-object stores for `YieldStarState` and dynamic `with` `JsEnvironment`.
The fix moved existing `JsValue` result-slot stores directly to
`StoreSymbolValueJsValue(...)` and made the unavoidable state-object wrapping
explicit at the two callsites. Future runner symbol-store cleanup should keep
that split visible so object-carrier audits can distinguish intentional internal
state from legacy JavaScript value flow. Related ADR:
`docs/adrs/0203-keep-runner-symbol-stores-jsvalue-native.md`.

Issue `autrun-dit1y716mdag-788f21e7b2` / PR #2226 migrated
`IterationHelper.HasCallableNext` from `object?` to `JsObject`. All callsites
were local to async-iterator helper setup and already held iterator objects
proven by `TryGetObject` or the success-returning symbol-iterator helper, so the
old `candidate is JsObject` guard was only a redundant private object-carrier
seam. Future protocol-predicate cleanup should let the caller boundary prove
object shape, add precise nullable annotations when flow analysis needs them,
and keep evidence to a scoped before/after signature search plus focused
protocol tests instead of widening into unrelated `object?` cleanup.

Issue `autrun-dit3bym4d9y0-f902ea4d39` / PR #2248 migrated the StdLib/Array
descriptor metadata and `Array.prototype.toString` intrinsic fallback path. The
array `name`, `length`, `@@unscopables`, and static method descriptors were
ordinary JavaScript data properties, so `PropertyDescriptor.Value = ...` kept an
avoidable compatibility-setter bridge in standard-library setup. The
`InvokeDefaultObjectToString(object?)` helper also widened a fallback value even
though `%Object.prototype.toString%` already returned `JsValue` to a host method
that required `JsValue`. Future Array/std-library unboxer slices should include
both scoped descriptor-setter searches and helper-signature/rewrap searches
when a fallback helper and descriptor metadata live in the same owner surface.
Related ADR: `docs/adrs/0155-keep-propertydescriptor-data-values-jsvalue-native.md`.

Issue `autrun-dit5vua7unjk-bffd6c8efb` / PR #2273 applied the same descriptor
setter policy to selected RegExp owner files. The migrated `lastIndex`
descriptors and `RegExp.escape` `name`/`length` descriptors were ordinary
JavaScript data descriptors; preserving `PropertyDescriptor.Value = ...` would
have kept an avoidable compatibility-setter bridge even though the descriptor
values were primitive JavaScript values with unchanged descriptor attributes.
Future RegExp and builtin-metadata unboxer slices should include stateful data
slots such as `lastIndex` in the scoped `\bValue\s*=` owner-file search, not
only constructor metadata such as `name` and `length`. Related ADR:
`docs/adrs/0155-keep-propertydescriptor-data-values-jsvalue-native.md`.

Issue `autrun-dit75s3mysnk-3b0697bdb7` / PR #2280 applied the same descriptor
setter policy to selected String standard-library owner files. The string
wrapper `length`, string iterator `next` `name`/`length`, and iterator
`@@toStringTag` descriptors are ordinary JavaScript data descriptors, so
leaving them on `PropertyDescriptor.Value = ...` would keep an avoidable
compatibility-setter bridge in String setup while descriptor attributes remain
unchanged. Future String/std-library unboxer slices should search the selected
owner files for `\bValue\s*=` before and after the edit, record both AC evidence
signals explicitly, and keep the migration limited to proven JavaScript data
values. Related ADR:
`docs/adrs/0155-keep-propertydescriptor-data-values-jsvalue-native.md`.

Issue `autrun-dit8gewlbljs-23ab36dc9e` / PR #2305 migrated the
`AssignObjectProperty` receiver parameter from `object?` to `JsValue?`. The
old helper accepted an extracted object receiver even when the callsite already
held the original target `JsValue`, then normalized it through
`JsValue.FromObjectUnsafe(...)`. The fix made the receiver typed, defaulted it
to the target when omitted, and passed the original target value from the core
assignment callsite. Future assignment-property receiver cleanup should keep
receiver identity on the `JsValue` path and prove the selected slice with a
focused `object? receiver` before/after search plus assignment-reference tests.
Related ADR:
`docs/adrs/0220-keep-assignment-property-receivers-jsvalue-native.md`.

Issue `autrun-dit9qcqe3mzs-470462a366` / PR #2317 migrated the shared
typed-array constructor result helper from `object?` to the generic concrete
typed-array type `T`. The old constructor host-function path immediately
rewrapped `ConstructTypedArray(args, newTarget)` with
`JsValue.FromObjectUnsafe(...)` even though every helper branch returned a
`TypedArrayBase` subtype and `JsValue` already has typed-array object conversion
support. Future typed-array constructor unboxer work should keep the shared
constructor helper typed, return it directly from the host-function invoke path,
and prove the slice with the focused `object? ConstructTypedArray` /
`FromObjectUnsafe(ConstructTypedArray` search plus typed-array constructor
tests. Related ADR:
`docs/adrs/0223-keep-typedarray-constructor-result-jsvalue-native.md`.

Issue `autrun-dithe2vzvzv4-52125cec24` / PR #2370 applied the same descriptor
setter policy to `JsEnvironment.CreateSuppressedError`. The `_errorData`,
`error`, `suppressed`, and `message` descriptors already received `JsValue`
payloads, so `PropertyDescriptor.Value = ...` only kept the compatibility
setter and `JsValue.FromObjectUnsafe(...)` bridge in the path. Future
SuppressedError or local error-object descriptor cleanup should migrate the
owner-file descriptor setters to `JsValue = ...`, keep descriptor attributes
unchanged, and record the exact before/after legacy-setter signal. Related ADR:
`docs/adrs/0155-keep-propertydescriptor-data-values-jsvalue-native.md`.

Issue `autrun-diu14wv8korc-df99b99081` / PR #2449 applied the descriptor setter
policy to object-literal dictionary spread. The first build migrated only
`TypedAstEvaluator.ExecutionPlanRunner.Helpers.ApplyObjectLiteralSpread(...)`
from `Value = value` to `JsValue = JsValue.FromObjectUnsafe(value)`. Review
caught that the mirrored legacy/dynamic spread branch in
`Ast/Legacy/ExpressionNodeExtensions.cs` still assigned `Value = kvp.Value`.
Future object-literal or descriptor cleanup must include both the IR runner and
legacy/dynamic evaluator owner paths in the scoped setter search, because the
dictionary interop source can remain `object?` while the `PropertyDescriptor`
sink still must be `JsValue`-native. Related ADR:
`docs/adrs/0155-keep-propertydescriptor-data-values-jsvalue-native.md`.
