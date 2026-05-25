# ADR 0100: Keep observable arguments binding eval-aware through arrows

## Status

Accepted

## Context

Issue
`planitem-planmanual1779530433702731000-reduce-function-call-activation-overhead-p-3c8725f1f9`
/ PR #1637 lazy-created `arguments` objects so ordinary function calls avoid
allocating an arguments object unless the binding is observable.

The optimization exposed an ECMAScript boundary: direct eval can observe the
caller function's `arguments` binding even when the function body does not
syntactically reference `arguments`. Review then found the nested-arrow variant:
an arrow has no own `arguments` binding, so direct eval inside a nested arrow
body or arrow default parameter still observes the enclosing ordinary or
generator function activation. Nested non-arrow functions remain a boundary
because they own their own `arguments` semantics.

Issue #1750 / PR #1789 exposed a second boundary in the opposite direction.
A sloppy function can need the spec-created arguments object even when
`NeedsArgumentsBinding` would otherwise say the binding is not observable from
executed code. In `function f() { return typeof arguments; var arguments = 42;
}`, the unreachable `var arguments` declaration still participates in function
declaration instantiation, but it must not replace the arguments object with
`undefined` before the return observes it. The repair created the arguments
object from the spec `argumentsObjectNeeded` decision, wrote the binding into
the body/execution environment as well as parameter/function environments, and
made var hoisting preserve an existing `arguments` binding.

## Decision

Keep the spec `argumentsObjectNeeded` decision separate from the optimization
question answered by `NeedsArgumentsBinding`.

`argumentsObjectNeeded` owns whether an ordinary/generator activation must
create the arguments object and reserve/protect the `arguments` binding during
function declaration instantiation. It must be honored even when the only
syntactic occurrence is a hoisted `var arguments` declaration with no executed
initializer.

`NeedsArgumentsBinding` owns whether lazy-allocation and fast-call optimizations
can skip materializing an observable binding for ordinary execution. That
decision must include:

1. direct syntactic `arguments` references in the function body;
2. `arguments` references in parameter defaults or binding patterns;
3. direct eval or `with` dynamic-scope behavior that can observe the binding;
4. nested arrow bodies and arrow parameter defaults because arrows inherit the
   enclosing `arguments`; and
5. a hard stop at nested non-arrow function bodies and declarations.

Do not decide lazy arguments creation from only direct body identifier scans, and
do not treat all nested functions equally. The relevant semantic split is
lexical-arrow inheritance versus non-arrow function activation ownership.

Do not let var hoisting overwrite an already-created non-lexical `arguments`
binding with `undefined`. Function declarations named `arguments` remain a
separate sloppy-function declaration-instantiation behavior and can still
override as covered by existing hoisting tests.

## Consequences

- Function activation optimization can still skip arguments-object allocation
  for non-observable calls, but the skip must be guarded by the eval-aware
  observable-binding decision.
- Fast activation paths must bail out when `argumentsObjectNeeded` is true
  unless they can prove the same body-environment binding and hoist-preservation
  semantics as the full activation path.
- Dynamic-scope and arguments-reference visitors must traverse nested arrows
  while preserving non-arrow function boundaries.
- Regression proof for future activation work should include ordinary body
  references, parameter defaults, direct eval in the function body, direct eval
  in parameter defaults, nested-arrow `arguments` references, nested-arrow
  direct eval for both ordinary and generator functions, and sloppy
  `return typeof arguments; var arguments = ...` hoisting.
- This complements ADR 0099: ADR 0099 owns activation slot-shape metadata,
  while this ADR owns the semantic conditions for creating and preserving the
  `arguments` binding/object across activation environments.

## Related

- `docs/adrs/0099-keep-function-activation-slot-shape-plan-owned.md`
- `.claude/rules/function-activation-proof-pack.md`
