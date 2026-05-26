# ADR 0194: Keep super constructor ThisInitialized guard boolean-fast-pathed

## Status

Accepted

## Context

Issue #2183 / PR #2185 followed ADR 0193's `classdef` evidence after plain
home-object methods had been allowed onto the simple IR activation path when
their lowered return program contained no `super` operations.

The follow-through profile still showed constructor and `super()` dispatch as
dominant sampled work under `ExecuteProgramSuperConstruct`,
`ExecuteProgramConstructNoSpread`, and `ReflectHelper.Construct`. The safe
retained slice was deliberately smaller than a dispatch rewrite: the
double-super guard for `Symbol.ThisInitialized` usually reads an engine-owned
boolean `JsValue`, but the existing code always routed any non-`undefined`
value through `JsOps.ToBoolean(...)`.

That guard sits inside a semantics-sensitive derived-constructor path. Future
work must not change argument/spread evaluation order, constructor validation,
`new.target`, proxy behavior, uninitialized `this` errors, double-super errors,
or class-field initialization while removing a coercion step.

## Decision

Keep the `Symbol.ThisInitialized` double-super guard in
`ExecuteProgramSuperConstruct` as a kind-guarded boolean fast path:

1. if the guard value is `undefined`, preserve the existing not-yet-initialized
   behavior;
2. if the guard value is a boolean `JsValue`, read it directly with
   `AsBoolean()`;
3. for every non-boolean value, preserve the previous
   `JsOps.ToBoolean(...)` fallback; and
4. keep constructor and `super()` dispatch routed through the existing
   semantic owners unless a separate profile and proof pack justify a wider
   change.

Do not make the check an unconditional `AsBoolean()` read. The slot is
engine-owned in the expected path, but the previous fallback was observable
defensive behavior for any legacy, dynamic, or malformed carrier that reaches
the guard.

## Consequences

- The hot `super()` constructor path avoids one coercion step for the normal
  boolean sentinel case.
- The change is a local fast path, not a broad class constructor throughput or
  parity claim.
- Future class constructor or super-dispatch performance work should start
  from current `classdef` CPU evidence and keep semantic fallbacks intact.
- Proof should include focused class/super semantics tests, expression-program
  lowering coverage for `SuperConstruct`, and the execution-plan AST-eval seam
  scan when touching this area.

## Related

- `.claude/rules/performance-profiling-guardrails.md`
- `.claude/rules/jsvalue-core-values.md`
- `docs/adrs/0164-keep-super-constructor-resolver-jsvalue-native.md`
- `docs/adrs/0171-keep-no-spread-construct-argument-carriers-and-super-spread-order.md`
- `docs/adrs/0193-keep-class-method-simple-ir-activation-super-guarded.md`
- `docs/performance/classdef-homeobject-simple-ir-activation.md`
