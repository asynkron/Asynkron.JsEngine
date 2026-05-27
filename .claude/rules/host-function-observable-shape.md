# Host Function Observable Shape

When creating or generating `HostFunction` built-ins, keep engine-owned callable
shape corrections separate from ordinary JavaScript property operations.

## Rules

1. If generated host-function metadata says `DeletePrototype`, remove the own
   `prototype` property with the internal force-delete path, not
   descriptor-respecting JavaScript delete semantics.
2. Do not broaden this into normal `delete` behavior. User-visible property
   deletion must continue to respect configurability and existing ECMAScript
   semantics.
3. Treat `ForceDeleteOwnProperty` as an internal construction/registration
   escape hatch for callable shape setup, similar to `HostFunction`'s own
   prototype-data-property cleanup.
4. When reading a host function's observable `[[Prototype]]`, use backing
   prototype state instead of the `HostFunction.Prototype` convenience
   accessor if the operation must preserve explicit `null` prototypes. The
   accessor may lazily rehydrate `Function.prototype` for callable defaults,
   which is wrong for `Reflect.getPrototypeOf` after
   `Object.setPrototypeOf(fn, null)`.
5. When changing built-in function object shape, add focused tests for the
   exact observable property and any aliases that share the same function
   object.
6. When deduplicating built-in `length`/`name` metadata setup, extract only the
   identical descriptor creation. Preserve the existing name/arity decisions,
   descriptor attributes, and call-site ordering; prove the touched built-ins
   with focused observable-shape tests instead of relying on line-count
   reduction alone.
7. When deduplicating attribute-decorated host method bodies, keep each
   `[JsHostMethod]`, `[JsConstructorMethod]`, or `[JsSymbolMethod]` member as
   the generator-visible entrypoint. Extract only the shared implementation
   behind thin adapters, and prove name, length, routing, prefixes, context, and
   return behavior with focused tests.

## Prototype Constructor Setup

When deduplicating prototype constructor bases that configure `HostFunction`
construction behavior, extract only the invariant construction workflow. Keep
per-family observable seams as explicit hooks:

- `newTarget` callable extraction can differ by constructor family; do not
  replace distinct `TryGetCallable` and `TryGetObject<IJsCallable>` paths with
  one broader helper unless focused tests prove the observable shape is the
  same.
- Post-allocation behavior can differ; for collection constructors, population
  order is part of the constructor contract and belongs in the family hook.
- Preserve the existing `Constructor X requires 'new'` TypeError boundary when
  the invocation is not a construction call or `newTarget` cannot be resolved
  through that family's accepted path.

WHY: issue `autrun-dis8iqog56lc-7528af1da1` / PR #1982 deduplicated
`SimpleInstanceConstructorBase<TInstance>` and
`CollectionConstructorBase<TInstance>` by introducing
`ConstructingInstanceConstructorBase<TInstance>`. The safe extraction kept
shared `new` enforcement, prototype resolution, and instance materialization in
the base while preserving simple-vs-collection `newTarget` extraction and
collection population as hooks. Future cleanup should keep that split instead
of merging constructor semantics under the appearance of duplicate code.

Related ADR:
`docs/adrs/0149-keep-prototype-constructor-newtarget-hooks-split.md`.

## Direct Factory Wrappers

When deduplicating registered host helpers and public/direct `HostFunction`
factory wrappers, extract the shared behavior behind explicit runtime
dependencies and keep each live entrypoint as a thin adapter. Do not delete an
obsolete factory, change its `HostFunction` overload, or force it through a
realm-aware registered-helper path while local callers still invoke the returned
function directly.

WHY: issue `autrun-disq6cxaox20-ce13f02487` / PR #2153 reduced duplicate async
iteration helper code in `IterationHelper`. The safe extraction introduced
`GetAsyncIteratorCore(..., JsEngineInstance)` and
`IteratorNextCore(..., JsEngineInstance)`, then kept
`CreateGetAsyncIteratorHelper` and `CreateIteratorNextHelper` as direct wrappers
for the `JsEngine` top-level-await bridge. Future host-helper deduplication
should preserve caller-visible wrapper shape until the bridge or caller has
moved to a different owner with focused proof.

Related ADR:
`docs/adrs/0187-keep-async-iteration-helper-entrypoints-core-shared.md`.

## Built-In Metadata Helpers

When moving repeated built-in function property setup into a shared helper,
keep the helper at the descriptor level: `length` and `name` should remain
non-writable, non-enumerable, configurable data properties, with each call site
still choosing the correct name and arity.

WHY: issue `autrun-dit9miwwhejc-c786be511d` / PR #2312 deduplicated Promise
built-in metadata setup by moving identical `SetBuiltInFunctionProperties`
definitions from `PromiseConstructor` and `PromisePrototype` into
`PromiseHelper`. The safe slice preserved descriptor attributes and all
call-site names/lengths, stayed scoped to Promise files, and used focused
Promise tests plus static-analysis evidence. Future built-in metadata
deduplication should keep that observable boundary instead of hiding
constructor/prototype-specific shape choices behind a broader helper.

## Attributed Method Forwarders

When repeated host prototype methods differ only in generator metadata or small
routing details, keep the attribute-decorated methods in place and make them
thin adapters to a private helper. Do not replace them with dictionary dispatch,
rename the methods, or move metadata to the helper; host registration and source
generation depend on the individual method attributes.

WHY: issue `autrun-diteieopg220-189a1ded07` / PR #2341 deduplicated
`ConsolePrototype` `log`, `error`, `warn`, `info`, and `debug` forwarding by
introducing `WriteConsoleLine`. The safe extraction kept each
`[JsHostMethod(..., Length = 0d)]` on its original public method, preserved
stdout/stderr routing, `Warning: ` and `Debug: ` prefixes, and `undefined`
return. Future host-method code reduction should keep generator-visible methods
as adapters and prove focused behavior instead of hiding per-method metadata
behind a generic dispatcher.

## Why

Issue #816 / PR #1016 fixed global `parseInt` and `Number.parseInt` after the
source generator emitted `Properties.Delete("prototype")` for
`DeletePrototype`. `HostFunction` had created a non-configurable own
`prototype` data property, so ordinary deletion correctly left it in place and
Test262 could observe it with `hasOwnProperty`. The durable rule is that
generated non-constructor built-ins need internal shape cleanup; JavaScript
delete semantics are the wrong abstraction for removing engine-created
non-configurable prototype data properties.

Related ADR: `docs/adrs/0029-keep-host-function-prototype-removal-internal.md`.

Issue #1057 / PR #1287 fixed `Reflect.getPrototypeOf` for host functions after
the generic prototype path read `HostFunction.Prototype` and masked an explicit
`Object.setPrototypeOf(hostFn, null)` by rehydrating the realm's
`Function.prototype`. The durable lesson is that HostFunction default callable
shape and user-mutated observable prototype state are distinct: prototype-query
operations must read the backing state when `null` is observable, while default
construction helpers may still use the convenience accessor where rehydration is
intended.
