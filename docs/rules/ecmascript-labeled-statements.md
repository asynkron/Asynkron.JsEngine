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
7. For labeled `break` / `continue` validation, classify the ultimate labeled
   target. Unwrap nested `LabeledStatement` chains before deciding whether a
   label targets an iteration statement. For example, `continue outer` remains
   valid in `outer: inner: for (...) { continue outer; }`.
8. Treat function and class bodies as static-control-flow boundaries. Labels,
   loop depth, and switch depth outside that body must not make an inner
   `break` or `continue` valid, and inner labels must not resolve outside that
   boundary.
9. Keep automatic semicolon insertion separate from labeled-control validation.
   `break` followed by a line terminator and then a label token is an unlabeled
   `break` plus a following statement, not a labeled break.

## Why

Issue #1839 / PR #1872 fixed labeled declaration Test262 rows where
`ParseLabeledStatement` needed to reject lexical declarations after labels
without misclassifying sloppy `let` followed by a line terminator. The review
fix made the `let` lookahead line-sensitive so the parser preserves both the
strict lexical-declaration early error and the sloppy expression-statement
path.

Related ADR: `docs/adrs/0135-keep-labeled-let-lookahead-line-sensitive.md`.

## Break/continue static validation

Issue #1836 / PR #1858 fixed `Statements_break` rows where illegal
`break`/`continue` reached IR missing-target failures instead of parse-time
syntax validation. The review fix also caught a valid nested-label shape that
was rejected because the validator classified the immediate labeled wrapper
instead of the ultimate loop target.

WHY: labeled control-flow static semantics are structural. A label can name a
nested labeled statement whose final target is an iteration statement, while
function/class bodies reset label and loop context. Future fixes should update
the shared `ControlFlowSyntaxValidator`, keep ASI statement-shape behavior in
the parser, and prove both valid nested-label continues and invalid
cross-boundary break/continue cases.

Related ADR:
`docs/adrs/0138-keep-break-continue-static-validation-shared-and-label-aware.md`.
