# ADR 0265: Keep arguments lazy index descriptors observable and integrity-synced

## Status

Accepted

## Context

Issue `autrun-diubg9b2tezc-a6f082fc8b` / PR #2546 continued the recurring
optimizer work on the `activation-arguments-lite` profile. The profile still
showed `JsArgumentsObject.ctor` cost under `CreateArgumentsObject`, with eager
numeric index descriptor creation and descriptor tracking visible in the call
tree.

The retained delivery made initial `arguments` numeric index properties lazy:
direct `arguments[i]` reads use the arguments object's own storage and mapped
parameter state without pre-creating backing `PropertyDescriptor` entries for
every supplied argument. Slow observable APIs still synthesize or materialize
the same properties as ordinary own properties.

The first retained change cleared the optimizer gate, reducing focused
`activation-arguments-lite` timing from a 755.3 ms baseline average to a
633.0 ms final average, a 16.2% improvement. Review/build-back then exposed two
important boundaries:

- non-canonical numeric strings such as `"00"` must not alias canonical index
  `"0"` just because integer parsing succeeds; and
- integrity-level operations such as `Object.seal(arguments)` and
  `Object.freeze(arguments)` mutate descriptor flags, so any tracked lazy index
  descriptor cache must be refreshed from the backing object after those
  mutations.

## Decision

Keep lazy initial `arguments` index descriptors, but treat them as virtual own
properties only until an observable slow path needs real descriptor state.

`JsArgumentsObject` owns this boundary:

1. Direct numeric reads may use the storage/mapped-parameter fast path when the
   initial index still exists and has not been deleted or converted to an
   accessor.
2. String-key slow paths must accept only canonical index property names such
   as `"0"`. Parsed numeric aliases such as `"00"` are ordinary properties and
   must not observe or mutate the lazy index.
3. Descriptor, enumeration, assignment, delete, `defineProperty`, and
   extensibility operations must synthesize or materialize the affected initial
   index before delegating to ordinary backing-object behavior.
4. `PreventExtensions`, `Seal`, and `Freeze` style integrity operations must
   materialize all remaining initial indices before mutating integrity state.
   After `Seal` or `Freeze`, tracked descriptors must be refreshed from the
   backing object so `Object.getOwnPropertyDescriptor`, `Object.isSealed`, and
   `Object.isFrozen` observe current configurable/writable flags.
5. Deletion still unmaps the corresponding sloppy mapped parameter and lets
   later reads fall back through the prototype chain.

Do not reintroduce eager per-index descriptor and index-name arrays to fix a
slow-path semantics bug. Fix the slow path at the lazy-descriptor boundary and
keep the direct read path owned by `JsArgumentsObject`.

## Consequences

- `activation-arguments-lite` keeps the retained descriptor allocation/timing
  win without treating arguments objects as dense arrays.
- Future arguments-object work must include canonical-string, descriptor,
  enumeration, delete/prototype fallback, and integrity-level tests whenever it
  changes lazy index storage or descriptor tracking.
- The focused activation proof pack is the local confidence gate for this
  surface; Test262 can widen confidence, but it should not replace the owner
  proof for mapped/unmapped arguments semantics.
- Adjacent property-access optimizations should route numeric computed reads
  into `JsArgumentsObject.TryGetIndex`, but descriptor materialization and
  integrity synchronization stay inside `JsArgumentsObject`.

## Evidence

- PR #2546 merged as commit
  `7fc71857f3312908e7b982cfa02ceed7de64ccc9`.
- Build-stage commits:
  - `65c3e52e Optimize arguments index descriptors`
  - `4e3df467 Fix canonical arguments index handling`
  - `60d28262 Fix arguments integrity descriptors`
- Performance note:
  `docs/performance/activation-arguments-lazy-index-descriptors.md`.
- Final focused timing rows averaged 633.0 ms for Asynkron versus a 755.3 ms
  focused baseline average, a 16.2% improvement.
- Final allocation row for `activation-arguments-lite` recorded
  `asynkron_kb=776971.2`, lower than the checked-in 2026-05-28 evidence row of
  `asynkron_kb=969158.7`.
- Build-stage verification passed `rtk git diff --check`, Release engine
  build, and the focused Release
  `ActivationSemanticsProofPackTests` filter after each build-back repair.

## Related

- `.claude/rules/function-activation-proof-pack.md`
- `docs/adrs/0211-keep-arguments-object-index-reads-storage-owned-and-descriptor-aware.md`
- `docs/adrs/0233-keep-activation-loop-scope-template-retries-performance-gated.md`
- `docs/performance/activation-arguments-index-read-fast-path.md`
- `docs/performance/failed-activation-arguments-loop-scope-template.md`
