# ADR 0079: Keep eval RegExp literal fast path grammar-shaped

## Status

Accepted

## Context

Issue #1047 / PR #1285 fixed the Test262 `Literals_regexp` timeout/crash
cluster. The delivery had two related optimizations:

- `EvalHostFunction` added a fast path for source strings that are exactly a
  RegExp literal, avoiding full parse/analyze in tight Test262 round-trip eval
  loops.
- `JsRegExp` widened deferred initial .NET `Regex` construction from only one
  Annex B escape to flagless literal-only patterns, while still running pattern
  normalization first.

The review cycle exposed the durable boundary. A JavaScript source string that
starts with `//` or `/*` is a comment, not a RegExp literal, even though it also
starts with slash characters. The first fast-path shape misclassified
comment-only eval payloads such as `eval('//foo')`; the follow-up build fixed
that by rejecting comment prefixes and letting normal script parsing return
`undefined`.

## Decision

Keep the eval RegExp literal fast path as a grammar-shaped pre-filter, not a
general slash scanner:

- trim only ordinary source trivia needed to recognize a standalone expression;
- accept only one complete RegExp literal with optional trailing semicolon and
  whitespace;
- reject `//` and `/*` prefixes before scanning for a closing slash;
- reject line terminators, non-letter flags, or trailing non-whitespace source;
- on any ambiguous shape, fall back to normal eval parsing and execution.

Keep RegExp construction deferral separate from eval source recognition.
`JsRegExp` may defer .NET `Regex` creation only after the ECMAScript pattern has
already been normalized and syntax errors have been raised at construction time.

## Consequences

- Eval fast paths preserve comment-only script semantics while still avoiding
  parse/analyze churn for proven standalone RegExp literals.
- Future eval-source optimizations should add negative tests for nearby source
  grammar forms, not only positive tests for the accelerated expression.
- Focused proof for this boundary should include the internal
  `Eval_WithLineCommentOnlyScript_ReturnsUndefined` and
  `Eval_WithBlockCommentOnlyScript_ReturnsUndefined` regressions, plus
  `Name=Literals_regexp` in the Test262 suite.
- This ADR is caused by issue #1047 / PR #1285 and complements
  `.claude/rules/ecmascript-regexp-runtime-bridges.md`.
