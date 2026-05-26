# ADR 0149: Keep prototype constructor newTarget hooks split

## Status

Accepted

## Context

Issue `autrun-dis8iqog56lc-7528af1da1` / PR #1982 reduced duplicated setup in
`SimpleInstanceConstructorBase<TInstance>` and
`CollectionConstructorBase<TInstance>`. Both bases enforced `new`, resolved the
constructed prototype through `newTarget`, materialized a typed runtime object,
and configured the backing `HostFunction` invocation path.

The duplication was structural, but the two constructor families did not have
identical observable behavior. Simple instance constructors accepted
`newTarget` through `TryGetCallable`, while collection constructors accepted it
through `TryGetObject<IJsCallable>`. Collections also needed an instance
population step before returning the created object.

Flattening those paths into a single generic constructor helper without
semantic hooks would risk changing which `newTarget` shapes are accepted and
which TypeError path reports `Constructor X requires 'new'`.

## Decision

Keep shared constructor setup in
`ConstructingInstanceConstructorBase<TInstance>`, but make the observable
differences explicit hook points:

- `TryGetNewTargetCallable(...)` owns each constructor family's callable
  extraction semantics.
- `BuildReturnValue(...)` owns post-allocation return behavior, including
  collection population.
- The shared base owns only the invariant workflow: `new` enforcement,
  constructor initialization, `newTarget` prototype resolution, typed instance
  materialization, and prototype assignment.

Do not deduplicate future prototype constructor bases by erasing these hook
boundaries or by replacing the distinct callable extraction paths with one
broader helper.

## Consequences

- Future constructor-base refactors should classify shared construction
  workflow separately from per-family observable argument and `newTarget`
  behavior.
- Focused proofs for this class should include both simple and collection
  constructor surfaces, such as Map/Set plus DisposableStack/AsyncDisposableStack
  constructor tests.
- Duplicate-code tools can identify the structural overlap, but review must
  still preserve TypeError wording, `newTarget` acceptance, prototype fallback,
  and collection population order.
- This ADR is caused by issue `autrun-dis8iqog56lc-7528af1da1` / PR #1982.
