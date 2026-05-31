# Generator Execution Path Parity

The JsEngine has two separate generator execution paths:
- **`ExecutionPlanRunner`** (plan-based): used when a generator function runs through the standard IR runner.
- **`SyncGeneratorInvoker` / `UnifiedBytecodeVirtualMachine`**: used when `TryCreateUnifiedBytecodeGenerator` succeeds (generator compiled to a unified-bytecode program).

## Rules

1. When applying an allocation or performance optimization to generator resumption
   in one execution path, audit the parallel path for the same opportunity.
   An optimization applied only to `ExecutionPlanRunner` while
   `UnifiedBytecodeVirtualMachine` retains the old allocation pattern creates a
   silent divergence that existing benchmark profiles do not directly measure.
   WHY: issue #2712 / PR #2718 found that `IteratorResultObject` /
   `IteratorResultObjectPool` had been added to the
   `ExecutionPlanRunner.CreateIteratorResult` path in an earlier slice, but
   `UnifiedBytecodeVirtualMachine.CreateIteratorResult` still allocated a fresh
   `JsObject` (with dictionary storage) on every yield cycle. The divergence was
   not caught by standard benchmark profiles because neither `forofiteration.js`
   nor `generator.js` exercises the unified-bytecode generator route.

2. When both paths define a helper with the same name (e.g. `CreateIteratorResult`),
   check that the return type, allocation strategy, and lightweight-type usage
   are consistent. A shared method name does not guarantee implementation parity.

3. The `IteratorResultObject.Create(value, done)` / `IteratorResultObjectPool`
   pattern is the established approach for iterator-result allocation in both
   generator execution paths:
   - Returns the `DoneUndefined` singleton for `done=true, value=undefined` —
     zero allocations on the final completion step.
   - Pools non-done instances (capacity: 64) to amortize allocations across
     generator iterations.
   - Supports `Capture` / `IsCaptured` for pool bypass when a result is
     assigned to a JavaScript variable.
   Before introducing a new `JsObject`-based path for iterator results in any
   generator execution path, verify that `IteratorResultObject` does not already
   apply. Related ADR: `docs/adrs/0299-reduce-iterator-result-allocation-resumable-generator.md`.

## Why

Issue #2712 / PR #2718 migrated `UnifiedBytecodeVirtualMachine.CreateIteratorResult`
from `JsObject` allocation to `IteratorResultObject.Create` after finding that
the unified-bytecode path had been left behind when the `ExecutionPlanRunner` path
was earlier optimized. Because the unified-bytecode generator path
(`TryCreateUnifiedBytecodeGenerator`) benefits from the pool only after the pool
is warm (first 64 yields), and the final completion step always uses the singleton,
the improvement is not directly visible in the standard `generator` benchmark
profile. Future generator resumption optimization work must account for both paths.
