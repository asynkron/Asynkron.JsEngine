# ADR 0132: Keep direct eval var arguments collision checks narrow

## Status

Accepted

## Context

Issue #1834 / PR #1853 fixed the Test262 variable-statement fixture
`language/statements/variable/12.2.1-11.js`. The fixture is sloppy-only and
expects ordinary function code to complete when it executes:

```js
function testcase() {
    eval("var arguments;");
}
```

The failure was not in parser strict binding validation or generic declaration
execution. It was in direct eval declaration-instantiation checks inside
`EvalHostFunction`: `var arguments` was being routed through broad
parameter-binding and lexical-collision rejection paths. A first fix allowed
the parameter case, but quality still failed because the generic lexical
collision guard treated the caller's `arguments` slot as a collision.

The final delivery kept strict-mode and non-simple-parameter rejection paths in
place, but excluded sloppy direct-eval `var arguments` from the generic
parameter and lexical collision checks. It also compared symbol names
ordinally instead of relying on `Symbol.Arguments` reference identity.

Issue #2002 / PR #2010 found the matching arrow-parameter edge case. A broad
arrow exception in the non-simple-parameter guard restored inherited
`arguments` behavior, but also allowed an arrow parameter list that declares
its own `arguments` binding:

```js
const f = (p = eval("var arguments = 'param'"), arguments) => {};
f();
```

The repair kept inherited arrow `arguments` behavior allowed, while rejecting
direct-eval `var arguments` only when the active parameter environment actually
has an own `arguments` binding.

This decision sits near ADR 0100 and ADR 0124, but owns a different boundary.
Those ADRs cover activation setup and lazy arguments-object materialization.
This ADR covers `EvalDeclarationInstantiation` collision policy for direct
eval source after parsing has succeeded.

## Decision

Keep direct eval `var arguments` handling owned by `EvalHostFunction` and keep
the exception narrow:

1. Sloppy direct eval in ordinary function code must allow `var arguments;`.
2. Generic parameter-binding and lexical-collision guards must not treat the
   caller's own `arguments` slot as an automatic collision for that sloppy
   direct-eval shape.
3. Strict eval and parser strict binding validation must keep rejecting
   `arguments` where ECMAScript requires it.
4. Non-simple-parameter direct-eval restrictions remain a dedicated guard.
   Future changes should adjust that guard directly rather than widening the
   generic lexical-collision path.
5. Name-based checks for this special binding should use ordinal string
   comparison on the symbol name, so behavior does not depend on symbol object
   identity.
6. Arrow parameter environments require a second split: arrows do not create an
   arguments object, but an arrow parameter list can still declare an own
   `arguments` binding. Do not exempt all arrows from the non-simple-parameter
   direct-eval guard, and do not reject inherited-arrow `arguments` cases that
   lack an own binding.

Do not fix this class by weakening parser binding validation, generic var
declaration execution, or all lexical collision detection. The shape is a
direct-eval declaration-instantiation exception for the legacy `arguments`
binding, not permission to make `arguments` generally redeclarable.

## Consequences

- Future direct-eval changes must prove both sides of the split: sloppy
  ordinary-function `eval("var arguments;")` completes, while strict and
  non-simple-parameter cases still reject when required.
- Arrow/non-simple-parameter proof must include both inherited `arguments`
  through arrow defaults and the explicit arrow parameter named `arguments`
  case from issue #2002.
- Focused proof should include the internal regression
  `TestVariableDeclarations.DirectEvalVarArgumentsInSloppyFunction_ShouldBeAllowed`
  and the owning Test262 method/file from issue #1834.
- Activation proof packs remain useful when activation setup changes, but they
  are not a substitute for direct-eval declaration-instantiation coverage.
- If another `arguments` regression appears, inspect `EvalHostFunction`
  collision checks before changing parser strictness or declaration IR.

## Related

- Issue #1834 / PR #1853
- Issue #2002 / PR #2010
- `docs/adrs/0100-keep-observable-arguments-binding-eval-aware-through-arrows.md`
- `docs/adrs/0124-keep-lazy-arguments-object-materialization-observable-and-profile-owned.md`
- `.claude/rules/ecmascript-direct-eval-declaration-instantiation.md`
