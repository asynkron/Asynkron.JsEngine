# ADR 0035: Keep block redeclaration early errors parser-scoped

## Status

Accepted

## Context

Issue #1022 / PR #1095 fixed the Test262
`BlockScope_syntax_redeclaration` crash for the negative parse-phase fixture
`fn-scope-var-name-redeclaration-attempt-with-generator.js`. The fixture shape
was a function body containing a nested block with a block-level generator
function declaration and a `var` declaration of the same name.

The durable bug was a missing parser-time early error. `ParseBlock` created the
block statement list without checking the ECMAScript static-semantics boundary
between a block `StatementList`'s lexical declarations and var-declared names.
That allowed code that should fail during parsing to reach later execution
paths.

The review bounce exposed the second durable boundary. Ordinary blocks should
treat direct block function declarations as lexical names for this early-error
check, but function bodies and class static blocks are not identical:

- function bodies still permit a function declaration and a `var` declaration
  with the same name in the body;
- class static blocks still reject `let`/`const`/`class` declarations that
  conflict with `var`; and
- class static blocks permit `function f() {}` followed by `var f`.

## Decision

Keep block redeclaration early-error validation in the parser, immediately after
the block's statement list is parsed and before returning the `BlockStatement`.

The parser-level check compares the current block `StatementList`'s lexical
names with its var-declared names. Var-declared names are collected through
same-function statement bodies such as nested blocks, branches, loops, switch
cases, and try/catch/finally blocks, but the collector must not enter nested
function bodies or class bodies.

Treat direct block function declarations as lexical names for ordinary
non-function blocks. Keep function-body compatibility by skipping this block
check for function bodies. For class static blocks, keep the lexical-vs-var
check for `let`/`const`/`using`/`await using`/`class` declarations while
excluding direct function declarations from the lexical-name side.

Do not solve this class in runtime hoisting, IR lowering, Test262 harness
policy, or generated Test262 files. The failure is a static parse-phase
semantics boundary, so the parser should reject the invalid source before later
execution machinery can observe it.

## Consequences

- Future parser changes around block declarations should preserve the
  three-way distinction between ordinary blocks, function bodies, and class
  static blocks.
- Scope-analysis and hoist helpers are useful references, but parser-time
  static semantics should remain narrow enough to avoid runtime Annex B
  overreach.
- Focused proof for this class should include local block-scope parser
  regressions plus the exact Test262 method group
  `Name=BlockScope_syntax_redeclaration`.
- This ADR is caused by issue #1022 / PR #1095 and complements
  `.claude/rules/ecmascript-annex-b-block-functions.md`.
