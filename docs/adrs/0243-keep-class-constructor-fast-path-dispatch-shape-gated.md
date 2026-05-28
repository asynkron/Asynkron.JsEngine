# ADR 0243: Keep class-constructor fast-path dispatch shape-gated

## Status

Accepted

## Context

Issue `autrun-diu14wtxo3eo-3299efe044` / PR #2456 selected the recurring
optimizer `classdef` profile after the investigation handoff pointed at class
constructor and `super(...)` dispatch. The focused baseline averaged about
930 ms across `1054`, `909`, and `828` ms rows.

ADR 0225 already keeps base-class-constructor simple IR activation
binder-guarded. ADR 0230 already keeps derived-class-constructor simple IR
activation owned by the `super()` instruction. After both retained paths were
present, the generic sync invocation path still tried both class-constructor
helper probes for every lowered sync function with a plan. The `classdef`
workload includes a `dogs.map(d => d.speak())` tail, so ordinary callback and
method invocations paid helper-probe overhead even though non-class callables
can never take either constructor path.

The same run found a local derived-constructor owner lookup cost. The simple
derived constructor environment owned the uninitialized `this` state before
`super(...)`, but it did not bind `Symbol.LexicalThisEnvironment` directly to
that owner. `ExecuteProgramSuperConstruct` therefore had to fall through to
slower constructor-this owner resolution that already exists as a semantic
fallback.

## Decision

Keep class-constructor fast-path dispatch shape-gated before helper entry.

The generic sync invocation path may try:

1. the simple derived class-constructor helper only when the callable is a
   class constructor and `_isDerivedClassConstructor` is true; and
2. the simple base class-constructor helper only when the callable is a class
   constructor and `_isDerivedClassConstructor` is false.

The helper-owned eligibility checks from ADR 0225 and ADR 0230 still remain the
semantic boundary. This decision adds a cheaper dispatch boundary: ordinary
functions, arrows, class methods, and the wrong constructor shape should not
enter helper predicates that can only reject them.

Keep the simple derived constructor environment's lexical-this owner explicit.
Before running the lowered plan, the transient function environment must define
`Symbol.LexicalThisEnvironment` to itself. That environment is the owner of the
uninitialized `this` binding before `super(...)`; the existing
`SuperConstruct` instruction remains responsible for construction,
double-super checks, receiver initialization, and the generic fallback search.

Do not use this as permission to broaden either constructor fast path. Rest,
default, destructured, parameter-expression, observable `arguments`, dynamic
lookup, home-object, private-scope, async/generator, missing-super, field
initialization, and default-derived-constructor cases keep their existing
fallbacks unless their owning binder and semantics are separately widened and
proved.

## Consequences

- Ordinary lowered sync functions no longer pay class-constructor helper probe
  overhead in the hot invocation path.
- The simple derived constructor path resolves its `super(...)`
  this-initialization owner directly while preserving the existing fallback
  chain for other shapes.
- The retained change improved focused `classdef` timing from about 930 ms to
  about 784 ms, roughly 16% faster, clearing the issue's 10% threshold.
- Future constructor/super work should keep call-site dispatch gates, helper
  semantic gates, repeated selected-profile timing, focused class/super
  semantics, the runner AST-eval seam scan, `forloop --memory`, and the
  canonical internal quality gate together.

## Related

- `.claude/rules/function-activation-proof-pack.md`
- `.claude/rules/performance-profiling-guardrails.md`
- `docs/performance/classdef-constructor-dispatch-gating.md`
- `docs/adrs/0214-keep-classdef-homeobject-and-construct-retries-profile-proven.md`
- `docs/adrs/0217-keep-derived-constructor-this-init-lookup-local-first.md`
- `docs/adrs/0225-keep-base-class-constructor-ir-activation-binder-guarded.md`
- `docs/adrs/0230-keep-derived-class-constructor-ir-activation-super-owned.md`
