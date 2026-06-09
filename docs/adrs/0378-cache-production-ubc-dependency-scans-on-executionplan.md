# ADR 0378: Cache production UBC dependency scans on ExecutionPlan

## Status

Accepted

## Context

Faktorial issue `autrun-dj42gyidvt1k-b99293a5fe` and delivery PR #3528
targeted the recurring optimizer benchmark gap for `functioncalls`.

The carried build summary and retained performance note showed that repeated
CPU profiles named the same owner:

```text
UnifiedBytecodeVirtualMachine.ExecutePreparedCall
  TypedAstEvaluator.SyncFunctionInvoker.InvokeWithContextSlow
    TypedAstEvaluator.SyncFunctionInvoker.CanUseProductionUnifiedBytecodeFastPath
      UnifiedBytecodeProductionEligibility.TryGetExpressionProgram
      UnifiedBytecodeProductionEligibility.ContainsOrdinaryDynamicIdentifierExpression
```

The cost was not the compiled unified-bytecode program itself. The hot work was
re-scanning immutable `ExecutionPlan` instructions and expression programs while
the production selector checked whether the plan had ordinary dynamic identifier
or implicit `arguments` object dependencies. Those answers depend on the plan's
instruction stream and activation slot shape. They do not depend on current
call arguments, closure state, descriptor-owned activation blockers, compiled
program results, or live environment values.

The repo already had ADR 0294 and rule 30a for caching structural production
declines on `ExecutionPlan`. This delivery is the adjacent positive fact case:
memoize pure plan-derived dependency booleans that feed production eligibility
without converting descriptor-dependent or runtime-dependent decisions into
plan-global state.

## Decision

Cache production unified-bytecode dependency-scan facts on the owning
`ExecutionPlan` when the fact is a pure function of immutable plan data.

- Cache only plan-derived facts such as ordinary dynamic identifier dependency
  and only implicit `arguments` object dynamic identifier dependency.
- Keep descriptor-owned activation blockers, closure/caller state, runtime
  environment values, call arguments, and compiled `UnifiedBytecodeProgram`
  results out of these caches.
- Store nullable boolean state as an explicit tri-state on `ExecutionPlan`.
  Reads and writes use volatile semantics; racing writers are benign only
  because every writer derives the same value from the same immutable plan.
- Keep the scan helpers themselves as the semantic owner. The cache should skip
  repeated scans after the first evaluation, not replace the eligibility
  algorithm with source-shape guesses or selector-side shortcuts.
- Future production eligibility dependency scans must first classify whether
  their result is plan-pure, descriptor-dependent, or runtime-dependent before
  adding plan-level memoization.

## Consequences

- IIFE-style workloads that create a fresh `SyncFunctionInvoker` for the same
  `FunctionExpression` no longer pay these dependency scans on every call.
- The positive cache is safe to share across invoker instances because the
  dependency facts are attached to the immutable lowered plan, not to a specific
  invocation.
- New dependency-scan cache proposals need current profile ownership and
  repeated selected-profile timing proof, because a cleaner call tree alone is
  not enough to retain a performance edit.
- Future agents must not generalize this ADR into caching descriptor-owned
  production eligibility, compiled programs, or environment-sensitive outcomes
  at plan scope.

## Evidence

- ADR allocation note: the local `rtk faktorial-api adr-next` wrapper was not
  available in this runtime (`rtk: No such file or directory`), so this learn
  pass used the host HTTP allocator endpoint `POST /api/adrs/next`, which
  returned `{"adr_id":378}`.
- Delivery PR #3528 merged as commit
  `9d4840c13f5f23bbd6ac62837953e5848b77578d`.
- The carried delivery commit was
  `715e648cb Cache functioncalls dependency scans`.
- The delivery changed:
  - `docs/performance/functioncalls-plan-dependency-scan-cache.md`
  - `src/Asynkron.JsEngine/Execution/ExecutionPlan.cs`
  - `src/Asynkron.JsEngine/Execution/UnifiedBytecode/UnifiedBytecodeProductionEligibility.cs`
- Build-stage signal recorded:
  - baseline `functioncalls` Asynkron rows: 6015, 6059, 6054 ms; average
    6043 ms.
  - final `functioncalls` Asynkron rows: 5296, 5123, 5135 ms; average
    5179 ms.
  - delta: 6043 ms to 5179 ms, 864 ms faster, about 14.3%.
- Build-stage proof recorded:
  - `rtk dotnet build -c Release` passed.
  - `rtk dotnet test tests/Asynkron.JsEngine.Tests --filter "FullyQualifiedName~UnifiedBytecodeProduction|FullyQualifiedName~ActivationSemanticsProofPackTests"`
    passed 1381 tests.
  - AST seam scan found no `EvaluateExpression(` or
    `ProfileEvaluateExpression(` matches in
    `src/Asynkron.JsEngine/Ast/TypedAstEvaluator.ExecutionPlanRunner*`.
  - `rtk ./tools/profile forloop --memory` reported 968.27 MB.
  - `rtk git diff --check` passed.

## Related

- ADR 0294:
  `docs/adrs/0294-cache-plan-level-production-eligibility-permanent-decline-cross-invoker.md`
- `docs/performance/functioncalls-plan-dependency-scan-cache.md`
- `docs/rules/performance-profiling-guardrails.md`
- `src/Asynkron.JsEngine/Execution/ExecutionPlan.cs`
- `src/Asynkron.JsEngine/Execution/UnifiedBytecode/UnifiedBytecodeProductionEligibility.cs`
