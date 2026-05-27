# ADR 0230: Keep derived class constructor IR activation super-owned

## Status

Accepted

## Context

Issue `autrun-dithx7y92pu8-176deb2370` / PR #2384 selected the recurring
optimizer `classdef` profile after the investigation handoff pointed at the
class-definition constructor and `super()` dispatch surface. The fresh full
table still showed `classdef` as a current loss at 997 ms versus Jint at
601 ms, and focused pre-change rows were noisy but averaged about 1577 ms.

The retained change added a simple derived-class-constructor IR activation path
in `SyncFunctionInvoker`. This is close to the base-constructor activation path
from ADR 0225, but the semantic owner is different: derived constructors must
start with uninitialized `this`, and `super()` is the operation that constructs
and initializes the receiver.

Earlier `classdef` attempts had already ruled out broad no-spread construct
shortcuts, generic runner argument-container replacement, simple
parameter-list shortcuts, home-object invalidation changes, and generic
`ThisInitialized` lookup reordering as repeatable wins or safe semantic
boundaries.

## Decision

Keep the derived-class-constructor simple IR activation path guarded by the
derived constructor environment and `super()` ownership contract.

The fast path may run only when:

1. the callable is a non-default derived class constructor;
2. `new.target` is defined;
3. parameters are simple identifiers with no parameter expressions;
4. `arguments` cannot be observed and no arguments binding/object is needed;
5. dynamic identifier lookup, lexical-this capture, home-object semantics,
   private-name scopes, captured private scopes, and instance fields are absent;
6. class evaluation supplied a `super` constructor/prototype binding; and
7. `ExecutionPlan.ActivationSlots` proves the simple IR activation layout and
   parameter slot indices expected by the binder.

The path may create the transient function/body environment pair and bind
simple parameters into the planned activation slots. It must leave the function
environment's `this` uninitialized, define `new.target`, active function, and a
`SuperBinding`, and pass the derived-constructor error realm into the runner.
The existing `SuperConstruct` expression instruction remains the owner of
calling `super(...)`, constructing the receiver, and marking `this`
initialized.

Do not turn this into a construction shortcut. The derived fast path must not
pre-initialize the instance, bypass `super()`, bypass `ReflectHelper.Construct`,
or admit private fields/scopes, home-object `super` property dependencies,
default/rest/destructured parameters, observable `arguments`, async/generator
shapes, or dynamic lookup without separately widening and proving those owners.

## Consequences

- Simple derived constructors can avoid the full invocation activation setup
  while preserving the JavaScript rule that `this` is unusable until
  `super()` initializes it.
- Broader derived constructors continue through the existing path until their
  binder, environment, private-scope, field-initialization, or home-object
  semantics are separately owned and proved.
- Future class-constructor activation work should pair selected-profile timing
  with focused class/super semantics, non-simple-parameter negative coverage,
  the AST-eval seam scan, `forloop --memory`, and the canonical quality gate.
- The performance note
  `docs/performance/classdef-derived-constructor-ir-activation.md` remains the
  measurement evidence; this ADR records the durable ownership policy.

## Related

- `.claude/rules/function-activation-proof-pack.md`
- `.claude/rules/performance-profiling-guardrails.md`
- `docs/performance/classdef-derived-constructor-ir-activation.md`
- `docs/adrs/0193-keep-class-method-simple-ir-activation-super-guarded.md`
- `docs/adrs/0214-keep-classdef-homeobject-and-construct-retries-profile-proven.md`
- `docs/adrs/0217-keep-derived-constructor-this-init-lookup-local-first.md`
- `docs/adrs/0225-keep-base-class-constructor-ir-activation-binder-guarded.md`
