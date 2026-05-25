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

## Why

Issue #1834 / PR #1853 fixed a Test262 variable-statement failure where
sloppy direct eval rejected `eval("var arguments;")` inside an ordinary
function. The bug survived an initial parameter-binding fix because the generic
lexical-collision guard still treated the caller's `arguments` slot as a
collision. Future work in this area should start at direct eval declaration
instantiation and prove the strict/sloppy and parameter-environment split
explicitly, rather than changing parser strictness or generic declaration
execution.

Related ADR:
`docs/adrs/0132-keep-direct-eval-var-arguments-collision-checks-narrow.md`.
