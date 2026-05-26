# ADR 0187: Keep async iteration helper entrypoints core-shared

## Status

Accepted

## Context

Issue `autrun-disq6cxaox20-ce13f02487` / PR #2153 reduced duplicate code in
`IterationHelper`. The duplicated behavior existed in two entrypoint families:

- registered `[JsHostFunction]` helpers for `__getAsyncIterator` and
  `__iteratorNext`, which receive `RealmState` and resolve the current engine
  from it;
- obsolete public factory helpers,
  `CreateGetAsyncIteratorHelper(JsEngineInstance)` and
  `CreateIteratorNextHelper(JsEngineInstance)`, which are still called by the
  `JsEngine` top-level-await `for await` bridge and return directly invoked
  `HostFunction` wrappers.

The code-reduction goal was valid, but deleting the obsolete factories or
forcing the direct factory path through the registered-host-function shape would
have changed a live compatibility surface. The TLA bridge invokes the returned
`HostFunction` directly with explicit arguments, so the wrapper overload and
captured engine remain part of the local contract until that bridge is replaced.

## Decision

Keep one shared private core implementation per async iteration helper
behavior, and keep both entrypoint families as thin adapters.

`GetAsyncIteratorCore(args, engine)` owns get-async-iterator behavior.
`IteratorNextCore(args, engine)` owns iterator-next behavior. The registered
host functions resolve `realm.Engine` and delegate to those cores. The obsolete
factory helpers capture the supplied `JsEngineInstance` and delegate to the same
cores through the same direct `HostFunction` invocation shape expected by the
TLA bridge.

Do not remove the obsolete factories, change their invocation overload, or
introduce a fake `RealmState` path for direct callers unless the TLA bridge has
first moved to a different owner with focused proof.

## Consequences

- Future async iteration fixes only update one implementation body for each
  helper while preserving both live entrypoint shapes.
- Code-reduction slices in host helper areas should extract invariant behavior
  behind explicit runtime dependencies instead of deleting compatibility
  wrappers that still have callers.
- Proof for this seam should include a caller scan for the factory helpers and
  registered helper names, a focused async-iteration test filter, `git diff
  --check`, and a duplicate/code-size signal when the work is part of recurring
  code reduction.
- If the TLA bridge later stops using these factories, removal should be a
  separate compatibility cleanup with its own call-surface search.

## Related

- Issue `autrun-disq6cxaox20-ce13f02487`
- PR #2153
- `src/Asynkron.JsEngine/StdLib/Iteration/IterationHelper.cs`
- `src/Asynkron.JsEngine/JsEngine.cs`
- `.claude/rules/host-function-observable-shape.md`
