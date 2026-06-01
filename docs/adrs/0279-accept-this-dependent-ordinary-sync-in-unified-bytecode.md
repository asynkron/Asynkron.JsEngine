# ADR 0279: Accept this-dependent ordinary sync functions in unified bytecode production

## Status

Accepted

Superseded note: the future-only `HasThisDependency` placeholder described
below was removed after no current production route set it. The ordinary sync
`this` admission decision still stands.

## Context

Faktorial issue #2633 widened unified bytecode production eligibility to accept
ordinary sync functions that reference `this`.

Prior to this slice, `CanUseProductionUnifiedBytecodeFastPath` in
`TypedAstEvaluator.SyncFunctionInvoker` contained a blanket
`_homeObject is not null` rejection. Every class instance method and every
object-literal method with a home object was blocked from reaching the
production unified-bytecode path, regardless of whether the method body
contained any super-property or super-call opcodes.

The plan-level `SuperPropertyDependency` decline was already implemented in
`UnifiedBytecodeProductionEligibility.TryFindExpressionDecline`, handling
`ExpressionOpKind.GetNamedSuperProperty`,
`ExpressionOpKind.GetComputedSuperProperty`,
`ExpressionOpKind.SetNamedSuperProperty`,
`ExpressionOpKind.SetComputedSuperProperty`,
`ExpressionOpKind.UpdateNamedSuperProperty`,
`ExpressionOpKind.UpdateComputedSuperProperty`, and
`ExpressionOpKind.EnsureSuperReference`. The plan-level gate already provided
the required safety net for super-using class methods.

Additionally:
- `TryGetActivationResolvedValue` already returned `true` for
  `ExpressionOpKind.LoadThis`, so `this`-based property reads were
  plan-eligible before the pre-gate was removed.
- `TryInvokeProductionUnifiedBytecode` already computed `boundThis` via
  `CoerceThisValueForNonStrict` and passed it to
  `UnifiedBytecodeVirtualMachine.Execute`, so the VM path was already
  `this`-ready for strict and sloppy invocations.
- `_lexicalThisEnvironment is not null` remains as the arrow-function pre-gate.
- `_superConstructor is not null` and `_superPrototype is not null` remain as
  additional safety pre-gates for class constructor contexts.

The `HasThisDependency` flag in `UnifiedBytecodeProductionActivationDescriptor`
is never set for the ordinary sync path (it defaults to `false`). It is
kept at this ADR point as an explicit gate for future shapes where `this` must
still be declined; that placeholder was removed in a later decline cleanup.

## Decision

Remove the blanket `_homeObject is not null` pre-gate from
`CanUseProductionUnifiedBytecodeFastPath`.

- Class instance methods and plain object literal methods with `_homeObject`
  set but no super-property opcodes in the plan body are admitted to the
  production unified-bytecode path.
- The plan-level `SuperPropertyDependency` decline in
  `UnifiedBytecodeProductionEligibility.TryFindExpressionDecline` remains the
  safety net for class methods that use `super`.
- `_lexicalThisEnvironment is not null`, `_superConstructor is not null`, and
  `_superPrototype is not null` pre-gates remain unchanged.
- `HasThisDependency` was left in `UnifiedBytecodeProductionActivationDescriptor`
  at this ADR point as an explicit future gate; it was currently `false` for
  all ordinary sync descriptors and was removed in a later decline cleanup.

## Consequences

- Ordinary sync class instance methods and object literal methods that read
  `this.prop`, or write `this.prop` within the existing property-write
  boundary (simple assignments such as `this.prop = slot/constant`), now route
  through the production unified-bytecode VM instead of falling back to
  `SyncIrCallTrampoline` or generic `ExecutionPlanRunner`. Compound reads-then-
  writes such as `this.prop = this.prop + n` are still declined by the existing
  `PropertyWriteDependency` boundary.
- Strict and sloppy `this` semantics are preserved: `TryInvokeProductionUnifiedBytecode`
  computes `boundThis` via `CoerceThisValueForNonStrict` before VM entry, and
  `LoadThis` in the program loads the pre-coerced value. Sloppy-mode primitive
  `this` is boxed before the VM sees it.
- Super-using class methods still decline before VM execution via
  `SuperPropertyDependency` in the plan-level expression scan.
- Arrow functions still decline via `IsArrowFunction` and
  `_lexicalThisEnvironment is not null` pre-gates.

## Evidence

- PR #2633.
- Focused proof pack covers strict `this` read, sloppy `this` coercion
  (primitive `this` boxed by `CoerceThisValueForNonStrict`), class instance
  method `this.prop` read using the unified bytecode fast path, plain
  object-method simple `this.prop` write (`this.prop = constant`) within the
  existing property-write boundary, negative super-property class method
  (still declines via `SuperPropertyDependency`), and negative arrow function
  captured `this` (still declines via `IsArrowFunction` pre-gate).

## Related

- ADR 0278: `docs/adrs/0278-keep-unified-bytecode-ordinary-sync-primary-route-source-gated.md`
- ADR 0277: `docs/adrs/0277-keep-resumable-unified-bytecode-state-bounded-and-yield-star-declined.md`
- ADR 0193: `docs/adrs/0193-keep-class-method-simple-ir-activation-super-guarded.md`
- `src/Asynkron.JsEngine/Ast/TypedAstEvaluator.SyncFunctionInvoker.cs`
- `src/Asynkron.JsEngine/Execution/UnifiedBytecode/UnifiedBytecodeProductionEligibility.cs`
- `docs/unified-bytecode-expansion-contract.md`
