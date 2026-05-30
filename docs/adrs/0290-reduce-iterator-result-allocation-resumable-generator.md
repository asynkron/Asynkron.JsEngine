# ADR 0290 — Reduce Iterator-Result Allocation on Resumable Generator Resume Cycle

## Status

Accepted

## Context

Issue #2712 identified that `UnifiedBytecodeVirtualMachine.CreateIteratorResult`
allocated a fresh `JsObject` on every generator resume cycle (every `yield`
and every completion). The method appeared at profile position 2474 in the
virtual machine and was the sole allocation path for iterator results produced
by the unified-bytecode generator path (`SyncGeneratorInvoker`).

The `ExecutionPlanRunner` path (the non-unified-bytecode generator path) had
already been converted to use `IteratorResultObject` / `IteratorResultObjectPool`
as its `CreateIteratorResult` in `TypedAstEvaluator.ExecutionPlanRunner.Helpers.cs`.
The two `IteratorResultObject` types (`JsTypes/IteratorResultObject.cs` and
`JsTypes/IteratorResultObjectPool.cs`) were already present in the codebase.

The unified-bytecode path had not been updated to match, leaving a per-resume
`JsObject` allocation on the hot yield path.

The `JsObject` implementation uses a dictionary for property storage; every
allocation path for it includes dictionary initialization. Iterator results
only ever have two properties (`value` and `done`), so the full `JsObject`
machinery is wasteful.

`IteratorResultObject` is a fixed two-field lightweight object that:
- Implements `IJsObjectLike` directly with switch-based property access
- Returns a cached `DoneUndefined` singleton when `done=true` and `value=undefined`
- Pools non-done instances via `IteratorResultObjectPool` (capacity: 64) to
  amortize allocations across generator iterations
- Supports the Capture/IsCaptured pattern so results assigned to JS variables
  bypass pooling correctly

## Decision

Replace `UnifiedBytecodeVirtualMachine.CreateIteratorResult` allocation with
`IteratorResultObject.Create(value, done)`:

- **Return type change**: from `JsObject` to `JsValue` — the `IteratorResultObject`
  already exposes `AsJsValue` so callers receive a ready-to-use `JsValue`.
- **Parameter removal**: the `EvaluationContext context` parameter is dropped
  because `IteratorResultObject` does not need the realm's `ObjectPrototype`
  reference; it is a lightweight fixed-slot type, not a regular `JsObject`.
- **Caller update in `SyncGeneratorInvoker`**: the two call sites in
  `ExecuteUnifiedBytecodeGeneratorStep` remove the `JsValue.FromJsObject()`
  wrapping (no longer needed) and the `context` argument.

The pool return points in the VM that already exist (at the `ExecuteIteratorStep`
for-of driver and the `IteratorDriver.Next` fast path) handle `IteratorResultObject`
instances returned from this path transparently via `is IteratorResultObject` checks.

### Allocation impact

The singleton path (`done=true`, `value=undefined`) requires zero allocations.
The pooled path reuses existing `IteratorResultObject` instances once the pool
is warm, reducing Gen 0 pressure on generator-heavy loops.

Baseline and final allocation measurements for `forofiteration` and related
generator benchmarks should be verified via `./benchmark.sh --allocations` and
the allocation regression gate (`tools/check-allocation-regression`) before
merging.

## Consequences

- Generator resume cycles in the unified-bytecode path now share the same
  lightweight allocation strategy as the plan-runner path.
- The API surface of `UnifiedBytecodeVirtualMachine.CreateIteratorResult` is
  narrowed: callers no longer receive a mutable `JsObject` and cannot set
  arbitrary properties on the result. This is intentional — iterator results
  produced by the engine are not intended to be mutated by engine internals
  after construction.
- The pool return discipline (existing `is IteratorResultObject` checks in
  the for-of driver and iterator-driver fast path) applies equally to results
  produced by both generator execution paths.
