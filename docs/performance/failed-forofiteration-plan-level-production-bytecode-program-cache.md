# failed forofiteration - plan-level production bytecode program cache

## Summary

The `forofiteration` benchmark exposed a real repeated-work owner: the shared-engine
run re-instantiates the same IIFE on every script evaluation, creating a fresh
`SyncFunctionInvoker` and repeatedly paying `UnifiedBytecodeProductionEligibility.Evaluate`
plus `UnifiedBytecodeCompiler.TryCompile` for structurally identical bytecode.

The attempted optimization cached accepted compiled `UnifiedBytecodeProgram` results on
`ExecutionPlan` so sibling invokers could reuse a program compiled by an earlier invoker.
That produced a large local timing win, but it was not retained. `ExecutionPlan` is the
owner for plan-pure facts only; accepted programs and descriptor-sensitive route decisions
belong to `SyncFunctionInvoker` under rule 30b/30c and ADR 0378.

No runtime optimization from this attempt remains.

## Evidence from the reverted attempt

Baseline rows from the original build-stage attempt:

- `forofiteration` baseline: about 1021 ms median in the noisy controlled A/B window.
- `functioncalls` control: about 8000 ms and flat across baseline/final windows.

With the plan-level compiled-program cache, the same build-stage attempt measured:

- `forofiteration`: about 350 ms median, roughly 66% faster in that local window.
- internal tests: 7036 passed, 0 failed, 2 skipped.
- async `run-quality`: passed at 2026-06-13T05:34:18Z.

The performance result was real enough to record, but the implementation crossed the
cache-ownership boundary:

- `ExecutionPlan` cached the script-level `UnifiedBytecodeProductionEligibilityResult`,
  which includes the compiled program.
- `ExecutionPlan` cached accepted function-level `UnifiedBytecodeProgram` instances keyed by
  `newTarget.IsUndefined`.
- `SyncFunctionInvoker` then bypassed the normal route decision from that plan-owned accepted
  program.

Review rejected that ownership because production-route acceptance can depend on activation and
descriptor inputs. A guard that excludes known post-construction mutators is not enough proof to
make accepted compiled programs plan-global.

## Result

The runtime edit was reverted. Future work on this owner should avoid storing accepted
`UnifiedBytecodeProgram` results on `ExecutionPlan`. Safer directions are:

- cache only plan-pure dependency facts on `ExecutionPlan`;
- keep descriptor-sensitive route decisions and accepted programs on `SyncFunctionInvoker`;
- reduce `UnifiedBytecodeCompiler.TryCompile` cost itself, or cache a narrower immutable
  compile artifact only after a new ADR/rule explicitly owns its semantics and proof boundary.

## Related

- `docs/adrs/0378-cache-production-ubc-dependency-scans-on-executionplan.md`
- `docs/rules/performance-profiling-guardrails.md`
- `docs/performance/forofiteration-invoker-static-plan-cache.md`
