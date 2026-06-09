# ADR 0379: Cache production UBC route eligibility on SyncFunctionInvoker

## Status

Accepted

## Context

Faktorial issue #3543 and delivery PR #3547 continued the measured
`functioncalls` production call-dispatch work after ADR 0378 / PR #3528 cached
plan-pure production dependency scans on `ExecutionPlan` and the PR #3537
dynamic call-target symbol-cache trial was reverted as too small to retain.

The required CPU profile still named the route-admission path as the selected
owner:

```text
UnifiedBytecodeVirtualMachine.ExecutePreparedCall
  TypedAstEvaluator.SyncFunctionInvoker.InvokeWithContextSlow
    TypedAstEvaluator.SyncFunctionInvoker.CanUseProductionUnifiedBytecodeFastPath
      UnifiedBytecodeProductionEligibility.TryGetExpressionProgram
```

This residual was not another plan-pure dependency scan. The repeated work was
the full production route eligibility decision for the current sync invoker:
same lowered `ExecutionPlan`, same activation descriptor shape, same accepted
or declined compiled program result, and the same ordinary-call `new.target`
state. Those inputs are not all `ExecutionPlan` facts. Descriptor-owned
activation blockers and the accepted `UnifiedBytecodeProgram` must remain
invoker-owned, while `newTarget.IsUndefined` must be part of the route cache
key because ordinary calls and construct-like calls do not share the same
eligibility decision.

`SyncFunctionInvoker` also has shape mutators for private-name scope, captured
private-name scopes, super metadata, caller constructor state, home object, and
prototype state. Any cached route-admission answer must be discarded when those
mutators can change the activation descriptor or other production eligibility
inputs.

## Decision

Cache the production unified-bytecode route-admission answer on
`SyncFunctionInvoker`, separate from `ExecutionPlan` plan-pure dependency
caches.

- Reuse the existing per-invoker accepted/rejected production eligibility state
  only when the cached `ExecutionPlan` reference matches and the cached
  `newTarget.IsUndefined` value matches the current call.
- Let repeated ordinary sync calls go through
  `CanUseCachedOrEvaluateProductionUnifiedBytecodeFastPath(...)` before simple
  IR and runner fallback, so accepted and rejected route decisions skip the full
  descriptor/eligibility scan after the first evaluation.
- Keep ADR 0378's plan-pure dependency scans on `ExecutionPlan`. Do not move
  descriptor-owned activation blockers, compiled `UnifiedBytecodeProgram`
  results, closure or caller state, call arguments, runtime lookups, or
  `new.target`-sensitive decisions to plan scope.
- Invalidate the invoker cache from shape mutators that can change production
  eligibility inputs.
- Pin the boundary with source-gate coverage that verifies the ordinary sync
  route uses the cached guard and that the guard is explicitly
  `newTarget.IsUndefined` aware.

## Consequences

- Repeated `functioncalls` dispatch no longer rescans route eligibility for the
  same invoker, plan, and `new.target` kind.
- The accepted-program cache stays local to the invoker that owns the relevant
  activation descriptor, avoiding a cross-invoker cache that would conflate
  plan-pure facts with descriptor-sensitive route admission.
- Future production UBC route caches must classify each input before choosing a
  storage owner: immutable plan facts belong on `ExecutionPlan`; descriptor or
  accepted-program decisions belong on the invoker; live runtime values and call
  arguments should not be cached as route admission facts.
- Future cache-key changes must include explicit invalidation points for every
  mutator that can change the cached inputs, and must keep `new.target`-aware
  route decisions from reusing ordinary-call answers for construct-like calls.

## Evidence

- ADR allocation note: `rtk faktorial-api adr-next` was unavailable in this
  runtime (`rtk: No such file or directory`), so this learn pass used the same
  Faktorial runtime allocator through `POST /api/adrs/next`, which returned
  `{"adr_id":379}`. No `0379-*` ADR existed before writing this file.
- Delivery PR #3547 merged as commit
  `71d1a72136c803331d99937bcdda2e443c42f7c7`.
- The carried build-stage commit was
  `aa33272b2 Cache production bytecode eligibility route checks`.
- The delivery changed:
  - `src/Asynkron.JsEngine/Ast/TypedAstEvaluator.SyncFunctionInvoker.cs`
  - `tests/Asynkron.JsEngine.Tests/UnifiedBytecodeProductionInvocationTests.cs`
- Build-stage signal recorded:
  - baseline `functioncalls` CPU `ExecutePreparedCall` filtered time:
    3657.91 ms.
  - final `functioncalls` CPU `ExecutePreparedCall` filtered time: 119.29 ms.
  - delta: 3538.62 ms less filtered time in the selected owner sample.
  - `CanUseProductionUnifiedBytecodeFastPath` /
    `UnifiedBytecodeProductionEligibility.TryGetExpressionProgram` no longer
    appeared in the final top functions.
- Build-stage proof recorded:
  - `rtk ./tools/profile functioncalls --cpu --calltree-depth 40 --calltree-width 40`
    before and after.
  - Focused tests passed 80 tests, covering
    `UnifiedBytecodeProductionInvocationTests`, `CallTargetAdmissionTests`,
    `ComplexCallArgumentAdmissionTests`, and
    `ActivationSemanticsProofPackTests`.
  - AST seam scan found no `EvaluateExpression(` or
    `ProfileEvaluateExpression(` matches in
    `src/Asynkron.JsEngine/Ast/TypedAstEvaluator.ExecutionPlanRunner*`.
  - `rtk ./tools/profile forloop --memory` reported 968.27 MB.
  - `rtk git diff --check` passed.

## Related

- Issue #3543
- PR #3547
- ADR 0201:
  `docs/adrs/0201-keep-unified-bytecode-production-routing-decline-first.md`
- ADR 0204:
  `docs/adrs/0204-keep-unified-bytecode-sync-production-routing-slot-bridged.md`
- ADR 0378:
  `docs/adrs/0378-cache-production-ubc-dependency-scans-on-executionplan.md`
- `docs/performance/functioncalls-plan-dependency-scan-cache.md`
- `docs/performance/failed-functioncalls-dynamic-symbol-cache.md`
- `docs/rules/performance-profiling-guardrails.md`
- `src/Asynkron.JsEngine/Ast/TypedAstEvaluator.SyncFunctionInvoker.cs`
- `tests/Asynkron.JsEngine.Tests/UnifiedBytecodeProductionInvocationTests.cs`
