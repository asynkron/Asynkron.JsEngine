# ADR 0385: Reject plan-level production UBC accepted-program cache

## Status

Accepted

## Context

Faktorial issue `autrun-dj7nbj3c5nnc-f85e3d6d76` and delivery PR #3610
targeted the recurring optimizer benchmark gap for `forofiteration`.

The selected workload re-instantiates the same closure IIFE on every shared
engine script evaluation. Each new function literal creates a fresh
`SyncFunctionInvoker`, so the existing invoker-owned production route cache
starts cold and the runtime repeatedly pays
`UnifiedBytecodeProductionEligibility.Evaluate` plus
`UnifiedBytecodeCompiler.TryCompile` for structurally identical bytecode.

The first implementation moved accepted `UnifiedBytecodeProgram` results and
the script-level `UnifiedBytecodeProductionEligibilityResult` onto
`ExecutionPlan`, keyed by plan plus `newTarget.IsUndefined` and guarded to plain
function/arrow shapes. That produced a large local timing win for
`forofiteration`, but review rejected the ownership model.

ADR 0378 allows `ExecutionPlan` to cache plan-pure dependency facts. ADR 0379
keeps full production route-admission answers and accepted programs on
`SyncFunctionInvoker`, because those answers include descriptor-sensitive
activation inputs and invalidation owned by invoker shape mutators. A
plain-function sharing gate reduces known risk, but it is not a proof that the
accepted program is a plan-global fact.

## Decision

Do not store accepted production unified-bytecode programs, accepted
script-level production eligibility results, or other descriptor-sensitive
route-admission answers on `ExecutionPlan`.

- `ExecutionPlan` may cache only immutable plan facts such as structural
  declines and dependency-scan booleans whose value is a pure function of the
  lowered plan.
- Accepted/rejected route-admission state and accepted `UnifiedBytecodeProgram`
  instances remain owned by `SyncFunctionInvoker`, together with `new.target`
  keying and shape-mutator invalidation.
- A future cross-invoker optimization must first define a narrower immutable
  artifact that is not itself a route-admission answer, then record a new ADR
  and proof boundary before sharing it at plan scope.
- Failed attempts that cross this boundary should be preserved under
  `docs/performance/failed-*.md` so recurring optimizer runs do not rediscover
  the same unsafe cache shape.

## Consequences

- `forofiteration` keeps the repeated compile cost until a safer owner or
  artifact split is designed.
- The successful ADR 0378 and ADR 0379 cache boundaries remain intact:
  plan-pure facts on `ExecutionPlan`, descriptor-sensitive route state on
  `SyncFunctionInvoker`.
- Future performance reviews have an explicit negative precedent for plan-level
  accepted-program caches, even when a local benchmark window shows a large win.
- The retained delivery is evidence-only: no runtime optimization from the
  plan-level accepted-program attempt remains.

## Evidence

- ADR allocation note: `rtk faktorial-api adr-next` was unavailable in this
  runtime (`rtk: No such file or directory`). The Faktorial runtime allocator
  endpoint `POST /api/adrs/next` first returned `{"adr_id":384}`, but
  `docs/adrs/0384-close-e5b-runner-entry-point-tombstones-batch.md` already
  existed on current `origin/main`. A second allocator request returned
  `{"adr_id":385}`, and the `0385` prefix was checked free before writing.
- Delivery PR #3610 merged as commit
  `e6bcdddae9d4907a79f9fd7538b17c6cf28d688d`.
- The rejected implementation was the first PR commit, `bd7a9e42e Cache
  accepted production unified-bytecode program on the plan`.
- The accepted review repair was `8b7eea8af Respect production bytecode cache
  ownership`.
- Build-stage evidence before repair recorded:
  - `forofiteration` controlled A/B median around 1021 ms before the cache.
  - `forofiteration` around 350 ms after the cache, roughly 66% faster in that
    noisy local window.
  - internal tests passed: 7036 passed, 0 failed, 2 skipped.
  - async `run-quality` passed at 2026-06-13T05:34:18Z.
- Review-repair evidence recorded:
  - source scan for
    `CachedAcceptedProductionProgram|ScriptProductionEligibilityResult|_acceptedProductionProgram|_scriptProductionEligibilityResult`
    went from 20 matches to 0 matches.
  - `rtk dotnet build -c Release` passed with 0 errors and 7 existing nullable
    warnings.
  - focused
    `rtk dotnet test tests/Asynkron.JsEngine.Tests -c Release --filter "FullyQualifiedName~UnifiedBytecodeProduction|FullyQualifiedName~ActivationSemanticsProofPackTests"`
    passed 1389 tests.
  - selected benchmark row after reverting the invalid cache showed no retained
    speedup: `forofiteration` 966 ms vs Jint 484 ms.
- Review then reran the full internal suite on the repaired PR head and observed
  7036 tests passed.

## Related

- ADR 0378:
  `docs/adrs/0378-cache-production-ubc-dependency-scans-on-executionplan.md`
- ADR 0379:
  `docs/adrs/0379-cache-production-ubc-route-eligibility-on-sync-invoker.md`
- `docs/performance/failed-forofiteration-plan-level-production-bytecode-program-cache.md`
- `docs/performance/forofiteration-invoker-static-plan-cache.md`
- `docs/rules/performance-profiling-guardrails.md`
- `src/Asynkron.JsEngine/Execution/ExecutionPlan.cs`
- `src/Asynkron.JsEngine/Ast/TypedAstEvaluator.SyncFunctionInvoker.cs`
- `src/Asynkron.JsEngine/Execution/UnifiedBytecode/UnifiedBytecodeProductionEligibility.cs`
