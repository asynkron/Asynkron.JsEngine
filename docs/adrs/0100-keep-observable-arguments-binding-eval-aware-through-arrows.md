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

## Decision

Keep `NeedsArgumentsBinding` as the single activation-time question for whether
an ordinary/generator function must materialize an observable `arguments`
binding.

That decision must include:

1. direct syntactic `arguments` references in the function body;
2. `arguments` references in parameter defaults or binding patterns;
3. direct eval or `with` dynamic-scope behavior that can observe the binding;
4. nested arrow bodies and arrow parameter defaults because arrows inherit the
   enclosing `arguments`; and
5. a hard stop at nested non-arrow function bodies and declarations.

Do not decide lazy arguments creation from only direct body identifier scans, and
do not treat all nested functions equally. The relevant semantic split is
lexical-arrow inheritance versus non-arrow function activation ownership.

## Consequences

- Function activation optimization can still skip arguments-object allocation
  for non-observable calls, but the skip must be guarded by the eval-aware
  observable-binding decision.
- Dynamic-scope and arguments-reference visitors must traverse nested arrows
  while preserving non-arrow function boundaries.
- Regression proof for future activation work should include ordinary body
  references, parameter defaults, direct eval in the function body, direct eval
  in parameter defaults, nested-arrow `arguments` references, and nested-arrow
  direct eval for both ordinary and generator functions.
- This complements ADR 0099: ADR 0099 owns activation slot-shape metadata,
  while this ADR owns the semantic condition for creating the observable
  `arguments` binding.

## Related

- `docs/adrs/0099-keep-function-activation-slot-shape-plan-owned.md`
- `.claude/rules/function-activation-proof-pack.md`
