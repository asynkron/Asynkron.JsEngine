# ECMAScript Labeled Statements

When changing labeled statement parsing, keep lexical-declaration early errors
separate from sloppy-mode contextual keyword expression parsing.

## Rules

1. Reject labeled `const` and labeled `class` declarations during parsing.
2. Reject labeled `let` declarations in strict context.
3. In sloppy context, treat labeled `let` as a lexical declaration only when
   the declaration lookahead is on the same line as `let`.
4. Do not use `{`, `[`, or binding-identifier lookahead after `let` across a
   line terminator; automatic semicolon insertion and sloppy identifier
   expression parsing can make that a different statement shape.
5. Keep Annex B labeled function behavior separate from lexical declarations:
   sloppy labeled functions are allowed, strict labeled functions are rejected,
   and labeled generators are rejected.
6. Prove this class with focused local labeled statement regressions plus the
   Test262 method group `Name=Statements_labeled`.

## Why

Issue #1839 / PR #1872 fixed labeled declaration Test262 rows where
`ParseLabeledStatement` needed to reject lexical declarations after labels
without misclassifying sloppy `let` followed by a line terminator. The review
fix made the `let` lookahead line-sensitive so the parser preserves both the
strict lexical-declaration early error and the sloppy expression-statement
path.

Related ADR: `docs/adrs/0135-keep-labeled-let-lookahead-line-sensitive.md`.
