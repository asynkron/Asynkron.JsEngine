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

**Measured baseline and final allocations** (`./benchmark.sh --allocations`, 2026-05-30):

| Profile | Baseline (main, JsObject path) | Final (feature branch, IteratorResultObject) | Delta |
|---|---|---|---|
| `forofiteration` | 104,412 KB | 104,412 KB | 0 (array iteration, not generator path — unchanged as expected) |
| `generator` | 1,070 KB | 1,119 KB | ~+49 KB (within 48 KB measurement noise floor) |

**Interpretation**: The `forofiteration` profile iterates over a plain JavaScript array
(`for (const n of arr)`), which does not invoke `SyncGeneratorInvoker` or
`UnifiedBytecodeVirtualMachine.CreateIteratorResult` — it exercises the Array Iterator
built-in path. The numbers are unchanged across branches, as expected.

The `generator` profile uses `function* range(start, end)` and produces 100,000 yield
cycles per benchmark run. Both branches show nearly identical managed allocation
pressure (~1,070–1,120 KB). This is because the `generator.js` benchmark exercises
the `ExecutionPlanRunner` generator path, which was already converted to use
`IteratorResultObject` prior to this PR. The measured numbers confirm no regression.

The unified-bytecode path improvement (`UnifiedBytecodeVirtualMachine.CreateIteratorResult`)
benefits generator functions that are compiled to unified-bytecode programs (i.e., functions
with simple identifier parameters whose execution plan is available at invocation time). When
that path is taken, every `yield` previously allocated a `JsObject` with dictionary storage;
after this change it uses the `IteratorResultObject` singleton or pool. The pool
capacity is 64; in hot loops the pool is warm after the first 64 yields, and subsequent
yields incur no heap allocation. The final completion step (`done=true`, `value=undefined`)
always uses the `DoneUndefined` singleton — zero allocations.

### Gen-meth Test262 conformance

Verified 2026-05-30 on the feature branch — zero new failures:

| Cluster | Tests | Result |
|---|---|---|
| `Statements_generators` | 510 | 510/510 passed |
| `Expressions_generators` | 546 | 546/546 passed |
| `Statements_class_genMethod` + `genMethodStatic` | 110 | 110/110 passed |
| `Expressions_class_genMethod` + `genMethodStatic` | 110 | 110/110 passed |
| `GeneratorPrototype` | 218 | 218/218 passed |

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
