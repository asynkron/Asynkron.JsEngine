# ADR 0294: Cache Plan-Level Production Eligibility Permanent Decline Across IIFE Invokers

Date: 2026-05-30
Issue: autrun-diwap9usj808-20f5ecd5ca
PR: #2774

## Context

`SyncFunctionInvoker` is a short-lived object created once per `FunctionExpression` evaluation.
For IIFE-style workloads (e.g. `simplearithmetic`) the same `FunctionExpression` node is evaluated
10,000 times, producing a new invoker — and discarding its caches — on every iteration.

The per-invoker eligibility cache in `TryGetProductionUnifiedBytecodeProgram` correctly avoids
re-running `UnifiedBytecodeProductionEligibility.Evaluate` for repeated calls to the same invoker
instance. But for IIFE-style workloads the instance is never reused.

`UnifiedBytecodeProductionEligibility.Evaluate` runs `TryBuildActiveWithDepths` (an Array.Fill
plus instruction walk) and `TryFindExpressionDecline` on every call. For functions whose execution
plan structurally declines (e.g. contains `Math.sqrt(16)` / `Math.pow(2, 10)` calls not in the
first boundary), this work is identical on every invocation and never changes — it is a pure
function of the immutable `ExecutionPlan`.

## Decision

Add a `private volatile bool _productionEligibilityPermanentDecline` field to `ExecutionPlan`
(a `sealed record`) with accessor helpers `IsProductionEligibilityPermanentDecline` and
`MarkProductionEligibilityPermanentDecline()`.

When `TryGetProductionUnifiedBytecodeProgram` receives a structural decline from `Evaluate`,
it marks the plan permanently declined. On the next invocation — even from a different
`SyncFunctionInvoker` instance on the same `FunctionExpression` — the plan-level flag is checked
first and `Evaluate` is skipped entirely.

### Structural vs. descriptor-dependent declines

Not all decline codes are safe to cache at the plan level. Codes such as
`AsyncLikeFunction`, `GeneratorFunction`, `CapturedOrDynamicActivation`,
`ArgumentsObjectDependency`, `ThisDependency`, `NewTargetDependency`,
`ArrowLexicalThisDependency`, `ClassConstructorActivation`,
and `MaterializedActivationDependency`
depend on the closure or activation descriptor seen by each concrete invoker,
and could vary for a different closure context over the same plan.

`IsPlanStructuralDecline` excludes those descriptor-owned codes; all other codes represent
declines that are purely a function of the plan structure and are safe to cache globally on
the `ExecutionPlan`.

### Thread safety

The flag uses `volatile` semantics. Multiple threads racing to mark the plan declined all derive
the same answer from the same immutable plan, so the race is benign: the last writer wins, and
any reader that sees `true` is correct.

### CallDependency vs. CallInvocationBoundary disambiguation

A pre-existing ambiguity: `TryFindExpressionDecline` was using `CallDependency` as the decline
code for out-of-boundary call shapes (e.g. `Math.sqrt(16)`). This is the same code used by
the descriptor-level `HasCallDependency` decline (`_hasNonParameterCalleeCall && !canUseDynamic`).
Since `IsPlanStructuralDecline` treated `CallDependency` as descriptor-dependent (not structural),
the plan-structural decline path was never cached.

Changed `TryFindExpressionDecline` to emit `CallInvocationBoundary` (an existing but previously
unused enum member) for plan-structural out-of-boundary call declines. Direct eval keeps
`CallDependency` because its decline depends on caller context.

After `CanUseProductionUnifiedBytecodeFastPath` passes, the descriptor-level `CallDependency`
path (`_hasNonParameterCalleeCall && !canUseDynamic`) is already gated out, so the codes do not
collide at runtime.

## Consequences

- 10,000-iteration IIFE workloads skip `TryBuildActiveWithDepths` + `TryFindExpressionDecline`
  after the first invocation pays the one-time evaluation cost.
- `ExecutionPlan` gains one mutable `volatile bool` field. Records in C# can hold mutable fields;
  the field is explicitly documented as a post-construction cache and is semantically safe because
  the underlying plan structure that drives the eligibility result is immutable.
- Future eligibility change work must preserve `IsPlanStructuralDecline` accuracy: any new
  descriptor-dependent decline code must be added to its exclusion list. Any new plan-structural
  decline code benefits from this cache automatically.
- The `CallInvocationBoundary` / `CallDependency` disambiguation must be preserved: out-of-boundary
  call shapes in `TryFindExpressionDecline` must use `CallInvocationBoundary`, not `CallDependency`,
  so that structural call declines are cacheable. Direct eval must remain `CallDependency`.

## Measurement

`simplearithmetic` baseline (3-run average): 286ms (Jint ratio 3.64x)
`simplearithmetic` final (3-run average): 252ms (Jint ratio 3.14x)
Delta: -34ms / -11.9% improvement. Cleared the required ≥10% bar.

4708 internal tests pass. Performance doc: `docs/performance/simplearithmetic-iife-call-eligibility-plan-cache.md`.
