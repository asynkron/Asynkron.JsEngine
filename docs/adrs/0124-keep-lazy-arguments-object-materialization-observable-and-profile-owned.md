# ADR 0124: Keep lazy arguments object materialization observable and profile-owned

## Status

Accepted

## Context

Issue `autrun-dirquxckeg74-0fe6957821` / PR #1811 selected `classdef` from the
performance automation baseline and confirmed the remaining hot owner with a
focused CPU call tree. Constructor, `super(...)`, method, and `Array.map`
callback activation repeatedly entered
`ExecutionPlanRunner.CreateExecutionEnvironment`, where the runner created a
`JsArgumentsObject` for functions that never referenced `arguments` and had no
direct-eval or dynamic-scope path that could observe the binding.

The delivery changed the IR runner to combine the existing spec-shaped
`argumentsObjectNeeded` decision with the existing observable-binding analysis
from `NeedsArgumentsBinding(_function)`. The runtime still takes the full
arguments-object path when ordinary code, parameter defaults, direct eval,
dynamic scope, or nested arrows can observe `arguments`, but plain constructors,
methods, and callbacks avoid the allocation and property-definition work.

This decision sits near ADR 0100's semantic boundary. ADR 0100 keeps
`argumentsObjectNeeded` separate from `NeedsArgumentsBinding` so hoisting,
direct eval, parameter defaults, and nested arrows do not lose observable
`arguments` semantics. PR #1811 adds the performance side of that boundary:
only materialize the object when the spec decision and the observable-binding
decision both require runtime allocation on the current path.

## Decision

Keep lazy arguments-object materialization owned by activation setup and guarded
by both conditions:

1. the spec-shaped activation decision says an ordinary function can have an
   arguments object for this parameter/body shape; and
2. the observable-binding analysis says execution can observe the `arguments`
   binding or object.

Do not collapse these into a direct identifier scan. `NeedsArgumentsBinding`
must remain eval-aware, parameter-default-aware, dynamic-scope-aware, and
nested-arrow-aware, with nested non-arrow functions as the activation boundary.

Do not use a broad benchmark table alone to justify arguments-object laziness.
Future edits to this path need a focused activation or selected-profile CPU
call tree that shows arguments-object creation as the hot owner, then the named
activation proof pack before claiming the semantic boundary is preserved.

When this optimization skips materialization, it is skipping the concrete
`JsArgumentsObject` allocation and related environment binding work for an
unobservable path. It is not permission to merge the spec decision with the
optimization decision, to weaken direct-eval or nested-arrow detection, or to
let a hoisted `var arguments` replace an observable non-lexical arguments
binding.

## Consequences

- Class-heavy activation paths can avoid repeated `JsArgumentsObject`
  allocation when `arguments` is provably unobservable.
- Observable `arguments` cases stay on the full activation path documented by
  ADR 0100.
- Future activation performance work should pair profile ownership with
  `ActivationSemanticsProofPackTests`, relevant eval/arguments tests, and a
  focused class or activation semantics pack when constructor paths are touched.
- Performance notes such as
  `docs/performance/classdef-lazy-arguments-object.md` remain measurement
  evidence, while this ADR records the semantic policy.

## Related

- `docs/adrs/0100-keep-observable-arguments-binding-eval-aware-through-arrows.md`
- `docs/adrs/0099-keep-function-activation-slot-shape-plan-owned.md`
- `.claude/rules/function-activation-proof-pack.md`
- `.claude/rules/performance-profiling-guardrails.md`
- `docs/performance/classdef-lazy-arguments-object.md`
