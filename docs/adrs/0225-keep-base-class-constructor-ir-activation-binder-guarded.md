# ADR 0225: Keep base class constructor IR activation binder-guarded

## Status

Accepted

## Context

Issue `autrun-dit813mc4a00-66ec91b3ab` / PR #2302 selected the recurring
optimizer `classdef` profile. The fresh baseline measured Asynkron at 2390 ms
while Jint measured 419 ms, and repeated CPU profiles kept constructor dispatch
under `ExecuteProgramConstructNoSpread`, `ReflectHelper.Construct`,
`SyncFunctionInvoker.InvokeWithContextSlow`, and `ExecutionPlanRunner.RunSync`.

The retained optimization added a base-class-constructor IR activation fast
path in `SyncFunctionInvoker`. Derived constructors remain on the existing path
because `super(...)` owns uninitialized `this` handling. A non-derived base
constructor can instead build the function/body environment pair directly,
define constructor meta-bindings, and run the already-lowered execution plan.

Review-back found the important boundary: the new fast path originally admitted
rest-parameter constructors even though `BindSimpleIrActivationParameters`
binds only positional simple identifier parameters into planned parameter
slots. The repair added `_hasOnlySimpleIdentifierParameters` to
`CanUseSimpleBaseClassConstructorFastPath` and pinned the regression with
`BaseClassConstructor_RestParameter_BindsRestArray`.

## Decision

Keep the base-class-constructor simple IR activation path guarded by the same
contract as its binder and environment setup.

The fast path may run only when:

1. the callable is a class constructor but not a derived class constructor;
2. `new.target` is defined;
3. parameters are simple identifiers with no parameter expressions;
4. `arguments` cannot be observed and no arguments binding/object is needed;
5. dynamic identifier lookup, home-object semantics, explicit super state, and
   captured private scopes are absent; and
6. `ExecutionPlan.ActivationSlots` proves the root slot layout and parameter
   slot indices expected by `BindSimpleIrActivationParameters`.

Do not widen this path by changing only the eligibility predicate. If rest,
default, destructuring, parameter expressions, observable `arguments`, dynamic
scope, or derived-constructor behavior should become eligible, first widen the
owning binder/environment contract and prove the new shape with focused
regressions.

Instance initialization is still constructor-owned. When the path creates the
instance or receives one from a derived caller, it must preserve `this`,
`new.target`, the active function binding, instance field/private-name
initialization, throw propagation, and the transient environment pooling
ownership boundary.

## Consequences

- The selected `classdef` win is retained without treating the simple binder as
  a generic parameter-instantiation engine.
- Non-simple parameter lists continue through the full constructor invocation
  path until a separate binder implementation proves those semantics.
- Future class-constructor activation work should pair selected-profile timing
  with focused class/super semantics, non-simple-parameter negative coverage,
  the AST-eval seam scan, and the canonical internal quality gate.
- The performance note
  `docs/performance/classdef-base-constructor-ir-activation.md` remains the
  measurement evidence; this ADR records the durable eligibility policy.

## Related

- `.claude/rules/function-activation-proof-pack.md`
- `.claude/rules/performance-profiling-guardrails.md`
- `docs/performance/classdef-base-constructor-ir-activation.md`
- `docs/adrs/0193-keep-class-method-simple-ir-activation-super-guarded.md`
- `docs/adrs/0214-keep-classdef-homeobject-and-construct-retries-profile-proven.md`
