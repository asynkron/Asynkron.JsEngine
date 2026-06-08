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
4. When a sync generator declines the resumable unified-bytecode route,
   distinguish explicit `EvaluateResumable(...)` declines from earlier pre-gate
   declines. Only `EvaluateResumable(...)` declines may continue through a
   residue-specific runner bridge, and that bridge must preserve the production
   decline code/reason in classified logging. Non-simple parameters, missing
   plans, root-hoist collection gaps, slot/environment setup failures, and other
   pre-gates that did not reach the production-resumable classifier must fail
   explicitly instead of reusing a generic declined-body runner fallback.
   WHY: issue
   `planitem-planitem-planmanual1780730299657353000-unified-bytecode-remaining-burndo-f6a1994d6c`
   added the `classified-sync-generator-ir-fallback` log for
   `OptionalChainDependency` on `o?.[k]()` while keeping non-simple parameter
   fallback unclassified. That preserves the distinction between a classified
   production-resumable boundary and an invocation-shape pre-gate.
   Issue
   `planitem-planitem-planitem-gh3377-rebaseline-the-finite-bytecode-retirement-inven-fb95233c60`
   / PR #3457 then removed the generic sync-generator declined-body runner
   fallback, renamed the remaining bridge to
   `CreateClassifiedSyncGeneratorDeclinedResidueRunner(...)`, and updated stale
   non-simple-parameter tests to expect an explicit `NotSupportedException`.
   Future sync-generator fallback-retirement slices must source-gate absence of
   generic runner names and fallback log markers so pre-gate declines cannot
   quietly regain runner execution.
5. When an async-generator fallback bridge is retired, update the stale tests in
   the same slice to assert explicit decline failure instead of fallback
   success. Rename affected tests away from `FallsBack...`, assert that the
   rejection message includes the owning decline reason, and source-gate absence
   of old runner bridges and fallback log markers such as `_fallbackRunner`,
   `ExecuteFallbackRunnerStep`, `ExecuteAsyncStep(`, and
   `async-generator-runner-fallback`.
   WHY: issue
   `planitem-planmanual1780730299657353000-unified-bytecode-remaining-burndown-05-fal-4aeda4866f`
   / PR #3387 retired `AsyncGeneratorInvoker`'s IR fallback, but the first
   quality run failed 12 stale tests that still expected declined
   async-generator bodies to complete through the old runner. The build-back
   commit changed those tests to expect explicit rejection and added proof
   manifest source-absence markers so future fallback retirement work does not
   leave route expectations stale.

## Why

Issue #2712 / PR #2718 migrated `UnifiedBytecodeVirtualMachine.CreateIteratorResult`
from `JsObject` allocation to `IteratorResultObject.Create` after finding that
the unified-bytecode path had been left behind when the `ExecutionPlanRunner` path
was earlier optimized. Because the unified-bytecode generator path
(`TryCreateUnifiedBytecodeGenerator`) benefits from the pool only after the pool
is warm (first 64 yields), and the final completion step always uses the singleton,
the improvement is not directly visible in the standard `generator` benchmark
profile. Future generator resumption optimization work must account for both paths.

Issue
`planitem-planitem-planmanual1780730299657353000-unified-bytecode-remaining-burndo-f6a1994d6c`
added classified logging for sync-generator fallbacks that reach
`UnifiedBytecodeProductionEligibility.EvaluateResumable(...)` and decline there.
The durable decision is to expose the real production-resumable decline code for
diagnostics, while leaving earlier invocation pre-gates unclassified so future
burndown work does not confuse missing classifier evidence with an
`EvaluateResumable` result.

Issue
`planitem-planitem-planitem-gh3377-rebaseline-the-finite-bytecode-retirement-inven-fb95233c60`
/ PR #3457 tightened that boundary by removing the generic sync-generator
declined-body runner fallback. The remaining runner bridge is explicitly
residue-owned by production eligibility declines; non-simple parameters and
other pre-gates now fail before a generator object is created. The related proof
manifest row anchors on
`CreateClassifiedSyncGeneratorDeclinedResidueRunner(...)`, while source gates
reject the old `CreateClassifiedGeneratorDeclinedBodyRunner`,
`CreateClassifiedDeclinedBodyRunner`, and `classified-sync-generator-ir-fallback`
tokens.

Issue
`planitem-planmanual1780730299657353000-unified-bytecode-remaining-burndown-05-fal-4aeda4866f`
/ PR #3387 retired the async-generator runner fallback entirely. The durable
lesson is that fallback-retirement slices must update both runtime bridges and
test expectations: old fallback-success tests become explicit-decline tests,
and source-absence proof should ratchet the deleted bridge names and fallback
log marker so a renamed runner bridge cannot quietly reappear.
