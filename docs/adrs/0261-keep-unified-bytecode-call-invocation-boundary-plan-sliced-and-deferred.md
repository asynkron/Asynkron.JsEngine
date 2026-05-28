# ADR 0261: Keep unified bytecode call-invocation boundary plan sliced and deferred

## Status

Accepted

Superseded in part on 2026-05-28 by the first executable no-spread
activation-resolved identifier-call slice. The slicing decision still applies
to named/computed member calls, direct eval, spread calls, construct/super
calls, optional calls, arguments-object dependencies, dynamic lookup, and the
other unproven call-adjacent families.

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

Main has since landed the first executable identifier-call slice, so this ADR
now records the selected boundary and the still-deferred call families rather
than claiming every call invocation remains non-executable.

The remaining unsupported families also still include dynamic lookup, label
control flow, and iterator/destructuring drivers. Mixing those with call
execution would enlarge semantic risk and blur ownership.

## Decision

Select executable call invocation as the next unified-bytecode boundary, and
split the work into strict slices:

1. First slice: direct identifier calls only, where the target resolves to an
   activation slot, arguments are simple one-op operands, spread is absent, and
   direct eval is excluded.
2. Second slice: named member calls using existing prepared call-target
   metadata.
3. Third slice: computed member calls using existing prepared call-target
   metadata.
4. Deferred lane: constructor/super constructor execution remains separate.
5. Deferred lane: spread arguments and direct eval remain separate.
6. Deferred lane: iterator/destructuring drivers, label control flow, and
   dynamic lookup remain separate.

The production rule remains all-or-nothing VM routing: accepted programs must
execute fully in `UnifiedBytecodeVirtualMachine` without ExpressionProgram or
AST fallback.

## Consequences

- The next implementation lane has a clear "who/what/why" shape anchored to
  existing call-target preparation ownership.
- High-risk semantics stay isolated in explicit deferred lanes.
- Parallel follow-on items can be created without collapsing call invocation,
  constructor semantics, and dynamic/runtime lookup into one change.

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
- `docs/unified-bytecode-expansion-contract.md`
- ADR 0250: `docs/adrs/0250-keep-unified-bytecode-call-target-prep-boundary-non-executable.md`
- ADR 0251: `docs/adrs/0251-keep-unified-bytecode-iterator-and-destructuring-drivers-model-first.md`
- ADR 0258: `docs/adrs/0258-keep-unified-bytecode-completed-lanes-integrated-at-production-boundary.md`
