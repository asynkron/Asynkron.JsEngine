# ADR 0261: Keep unified bytecode call-invocation boundary plan sliced and deferred

## Status

Accepted

Superseded in part on 2026-05-28 by the first executable no-spread
activation-resolved identifier-call slice, by issue #2530 / PR #2534 for direct
named member calls, and by issue #2531 / PR #2535 for direct computed member
calls. The slicing decision still applies to direct eval, spread calls,
construct/super calls, optional calls, arguments-object dependencies, dynamic
lookup, and the other unproven call-adjacent families.

## Context

Faktorial issue
`planitem-planmanual1779943568009120000-batch-1-shared-bytecode-surface-and-parall-833518a41c`
requested the next boundary selection after the broad shared-bytecode parallel
batch. The current source-of-truth surfaces show call-target preparation is now
owned, and at the time of selection executable call invocation still declined
before VM execution:

- `UnifiedBytecodeCompiler` emits `PrepareIdentifierCallTarget`,
  `PrepareNamedCallTarget`, `PrepareComputedCallTarget`, and
  `CallInvocationBoundary`.
- `UnifiedBytecodeProductionEligibility` declines call invocation under
  `CallInvocationBoundary` / `CallDependency`.
- `UnifiedBytecodeVirtualMachine` treats `CallInvocationBoundary` as
  non-executable.

Main has since landed the first executable identifier-call slice, the direct
named member-call slice, and the direct computed member-call slice, so this ADR
records those slices as completed baselines and scopes the remaining call
invocation widening lanes.

The remaining unsupported families also still include dynamic lookup, label
control flow, and iterator/destructuring drivers. Mixing those with call
execution would enlarge semantic risk and blur ownership.

## Decision

Select executable call invocation as the next unified-bytecode boundary, and
split the work into strict slices:

1. Baseline (already landed): direct identifier calls where the target resolves
   to an activation slot, arguments are simple one-op operands, spread is
   absent, and direct eval is excluded.
2. Baseline (already landed): direct named member calls whose receiver chain is
   activation-resolved and whose arguments are simple one-op operands, using
   existing prepared call-target metadata while preserving the receiver as
   `this`.
3. Baseline (already landed): direct computed member calls whose receiver chain
   is activation-resolved, whose computed key is a simple operand, and whose
   arguments are simple one-op operands, using existing prepared call-target
   metadata while preserving the receiver as `this`.
4. Deferred lane: constructor/super constructor execution remains separate.
5. Deferred lane: spread arguments and direct eval remain separate.
6. Deferred lane: iterator/destructuring drivers, label control flow, and
   dynamic lookup remain separate.

The production rule remains all-or-nothing VM routing: accepted programs must
execute fully in `UnifiedBytecodeVirtualMachine` without ExpressionProgram or
AST fallback.

## Consequences

- The named/computed member-call lanes are completed as direct no-spread
  receiver-preserving production boundaries, and the already-landed
  identifier-call baseline remains explicit.
- High-risk semantics stay isolated in explicit deferred lanes.
- Parallel follow-on items can be created without collapsing call invocation,
  constructor semantics, and dynamic/runtime lookup into one change.

## Learn-Stage Guardrail

The review/build-back for PR #2515 found that the follow-on plan still started
with direct identifier-call execution after issue #2495 / PR #2501 had already
made no-spread activation-resolved identifier calls executable. Future
call-invocation plan edits must rebase lane order against current `main`,
`docs/unified-bytecode-expansion-contract.md`, and the current ADR/rule
boundaries before preserving any previous batch list.

If the first planned slice is already current support, record it as baseline,
promote the next unsupported family to the first remaining lane, and update the
Faktorial plan body and ADR wording together. After issue #2531 / PR #2535,
constructor/super, spread/direct eval, dynamic lookup, iterator/destructuring,
and labels remain deferred.

## Proof Guidance

Use these proofs for the implementation slices; do not claim performance wins
without current-worktree measurements:

```bash
rtk dotnet test tests/Asynkron.JsEngine.Tests --filter "FullyQualifiedName~UnifiedBytecodeProductionEligibilityTests"
rtk dotnet test tests/Asynkron.JsEngine.Tests --filter "FullyQualifiedName~UnifiedBytecodeProductionInvocationTests"
rtk rg "EvaluateExpression\\(|ProfileEvaluateExpression\\(" src/Asynkron.JsEngine/Ast/TypedAstEvaluator.ExecutionPlanRunner*
rtk ./tools/profile forloop --memory
```

## Related

- Issue
  `planitem-planmanual1779943568009120000-batch-1-shared-bytecode-surface-and-parall-833518a41c`
- Follow-on Faktorial plan
  `planmanual1779961785446650000` ("Widen unified bytecode production to executable call invocation boundary")
- `docs/unified-bytecode-expansion-contract.md`
- ADR 0250: `docs/adrs/0250-keep-unified-bytecode-call-target-prep-boundary-non-executable.md`
- ADR 0251: `docs/adrs/0251-keep-unified-bytecode-iterator-and-destructuring-drivers-model-first.md`
- ADR 0258: `docs/adrs/0258-keep-unified-bytecode-completed-lanes-integrated-at-production-boundary.md`
- ADR 0262: `docs/adrs/0262-keep-unified-bytecode-named-member-call-receiver-owned.md`
- ADR 0263: `docs/adrs/0263-keep-unified-bytecode-computed-member-call-key-and-receiver-owned.md`
