# ADR 0116: Keep array push single-arg host-call shortcut runtime-guarded

## Status

Accepted

## Context

Issue `autrun-dirmopwawwc8-6c6cc5263e` / PR #1786 continued the focused
`arrayops` performance work after ADR 0103 moved dense result-array writes and
length storage into `JsArray` storage-owned helpers.

The required pre-edit benchmark selected `arrayops` again:

```text
arrayops  asynkron_ms=6295  jint_ms=1319  Jint 4.77x faster
```

The focused CPU profile,
`rtk ./tools/profile arrayops --cpu --calltree-depth 40 --calltree-width 40`,
showed that the remaining hot owner was not descriptor-backed array storage.
The initial `arr.push(i)` loop resolved to the generated native
`Array.prototype.push` host function, but the expression runner still invoked it
through the generic single-argument host-call path. That path carried the
argument through an `IReadOnlyList<JsValue>` boundary and showed
`CastHelpers.Box` under the single-argument push call tree.

The accepted delivery added a direct one-argument `push` shortcut in
`ExecutionPlanRunner` for generated native host functions named `push`, then
delegated the semantic decision to `JsArray.TryPushSingleFast`. The array helper
only appends directly for plain arrays when indexed descriptors, prototype
index overrides or proxies, non-extensibility, non-writable length, and the
maximum array length boundary cannot be observed. Everything else falls back to
the existing `Array.prototype.push` implementation.

Repeated final benchmark runs after a Release rebuild were:

```text
arrayops  asynkron_ms=3059  jint_ms=1036  Jint 2.95x faster
arrayops  asynkron_ms=2673  jint_ms=880   Jint 3.04x faster
arrayops  asynkron_ms=1892  jint_ms=652   Jint 2.90x faster
```

The focused semantic pack
`rtk dotnet test tests/Asynkron.JsEngine.Tests --filter "FullyQualifiedName~ArrayBuiltinsSpecTests"`
passed, including coverage that inherited numeric setters still route through
the observable fallback.

## Decision

Keep the `Array.prototype.push(value)` shortcut runtime-guarded at two layers:

1. the expression runner may bypass generic host-call argument materialization
   only for the generated native one-argument `push` host-function shape; and
2. `JsArray` storage remains responsible for deciding whether the receiver is a
   plain array whose append cannot observe ordinary property semantics.

Do not move this optimization into a broad built-in rewrite or into generic
single-argument host-call invocation. The performance problem was specific to
the combination of a known native `push` function, a plain `JsArray` receiver,
and one argument. The semantic proof belongs at the array storage boundary
because `push` is specified in terms of indexed property creation and length
updates.

## Consequences

- The common `arr.push(value)` loop avoids `SingleValueArgs` boxing and generic
  `IReadOnlyList<JsValue>` host invocation.
- Modified arrays, inherited numeric accessors, prototype proxies, frozen or
  sealed receivers, non-writable length, non-array receivers, spread calls, and
  multi-argument calls continue to use the existing observable implementation.
- Future array built-in call-boundary shortcuts need both a CPU call tree that
  identifies host-call argument materialization as the owner and focused
  regressions for descriptor, prototype, extensibility, length-writability, and
  maximum-length fallback behavior.
- If more native built-ins need similar treatment, add explicit engine-owned
  metadata or dedicated identity checks rather than widening the shortcut into
  user-visible function-name heuristics.

## Related

- `docs/performance/arrayops-push-single-arg-fast-path.md`
- `docs/adrs/0103-keep-array-dense-writes-storage-owned.md`
- `docs/adrs/0114-keep-array-length-helper-jsvalue-native.md`
- `.claude/rules/performance-profiling-guardrails.md`
