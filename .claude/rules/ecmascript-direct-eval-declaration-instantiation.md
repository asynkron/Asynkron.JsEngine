# ECMAScript Direct Eval Declaration Instantiation

When changing `EvalHostFunction`, direct eval parsing, declaration
instantiation, or scope-collision checks, preserve strict/sloppy eval behavior
and keep `arguments` handling path-specific.

## Rules

1. Keep sloppy direct eval `var arguments;` in ordinary function code allowed.
   Do not route this shape through generic parameter-binding or lexical
   collision rejection just because the caller has an `arguments` binding.
2. Keep strict eval and strict parser binding validation for `arguments`
   separate. A sloppy direct-eval exception must not weaken strict-mode
   `arguments` rejection.
3. Keep non-simple-parameter restrictions as a dedicated guard. If a future
   issue involves parameter-default or non-simple-parameter direct eval, update
   that guard and prove the parameter shape directly instead of widening
   generic lexical-collision behavior.
4. For the special `arguments` binding in eval declaration checks, prefer
   ordinal symbol-name comparison over reference identity. The semantic
   question is the binding name, not whether the symbol instance is interned.
5. Prove changes with both a focused internal regression and the owning
   Test262 filter or fixture. For issue #1834 / PR #1853, the internal guard is
   `TestVariableDeclarations.DirectEvalVarArgumentsInSloppyFunction_ShouldBeAllowed`
   and the external fixture is
   `language/statements/variable/12.2.1-11.js` under `Statements_variable`.
6. Keep arrow parameter environments out of ordinary-function-only
   `arguments` collision rules. Arrows do not create their own arguments
   object, but they can still have an own parameter binding named `arguments`.
   Direct-eval fixes in this area must distinguish those two facts: reject
   sloppy direct-eval `var arguments` only when the parameter environment
   actually has an own `arguments` binding, and prove both the inherited-arrow
   case that must stay allowed and the explicit-parameter case that must throw.

## Why

Issue #1834 / PR #1853 fixed a Test262 variable-statement failure where
sloppy direct eval rejected `eval("var arguments;")` inside an ordinary
function. The bug survived an initial parameter-binding fix because the generic
lexical-collision guard still treated the caller's `arguments` slot as a
collision. Future work in this area should start at direct eval declaration
instantiation and prove the strict/sloppy and parameter-environment split
explicitly, rather than changing parser strictness or generic declaration
execution.

Issue #1918 / PR #1920 revisited the same surface for direct eval in arrow
parameter environments named `arguments`. The exact reported `EvalCode_direct`
rows and the broader method group were already green on current main, so the
durable lesson is to preserve the existing ordinary-function versus arrow
environment split and to stop on current focused proof instead of broadening
direct-eval collision logic from a stale Test262 batch report.

Issue #2002 / PR #2010 turned that stale green closeout into a real edge-case:
the over-broad arrow exception allowed
`(p = eval("var arguments = 'param'"), arguments) => {}` even though the arrow's
parameter environment itself declared `arguments`. The repair kept arrow
inherited-arguments behavior allowed, but narrowed the non-simple-parameter
guard to the actual own `arguments` binding. Future changes on this surface
must not simplify this back to either "all arrow parameter environments are
exempt" or "all arrow parameter environments reject"; the observable split is
own parameter binding versus inherited outer arguments object.

Related ADR:
`docs/adrs/0132-keep-direct-eval-var-arguments-collision-checks-narrow.md`.
