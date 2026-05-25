# ADR 0117: Keep legacy octal literal strictness parser-owned

## Status

Accepted

## Context

Issue #1749 / PR #1787 fixed the Test262
`language/literals/numeric/legacy-octal-integer.js` failure where sloppy-mode
legacy octal numeric literals were rejected during lexing.

The failing fixture is `noStrict` and expects literals such as `00`, `01`,
`070`, and `077` to parse and evaluate as legacy octal integer values. The
prior implementation rejected pure legacy octal spellings inside
`JsLexer.ReadNumber()` before the parser could know whether a directive
prologue, forced strict parse, or nested function body made the source strict.

Simply removing the lexer rejection was not enough. If `070` fell through the
ordinary decimal path, it would parse as decimal `70` instead of the required
octal value `56`. The repair therefore had to keep tokenization permissive for
pure legacy octal integer forms while still preserving strict-mode rejection.

The review/verification re-entry for the delivery found only a stale-base
classification; after rebasing onto current `origin/main`, the branch stayed
clean and the effective code change remained the lexer/parser/test update from
PR #1787.

## Decision

Keep legacy octal integer literal tokenization independent from strict-mode
context. `JsLexer.ReadNumber()` may classify a pure leading-zero integer as a
numeric token and compute its base-8 value, but it must not unconditionally
throw because strictness is parser context, not lexer context.

Keep strict rejection in parser numeric-literal validation. The parser owns
`InStrictContext`, including directive prologues and forced strict parses, so it
must reject legacy octal integer lexemes when strict mode applies.

Apply that validation at every parser surface that consumes `TokenType.Number`
as source syntax, not only expression primaries. Object property keys, binding
property names, and other numeric-literal consumers must not bypass strict
legacy-octal rejection.

Do not solve this class in runtime evaluation, Test262 harness policy, or
generated Test262 files. The value of a sloppy legacy octal literal is decided
while reading the numeric token, and the strict early error is decided while
parsing source under strict context.

## Consequences

- Future numeric-literal changes should separate lexical value decoding from
  parser-only strictness decisions.
- Strict-mode checks for numeric syntax should be audited across all
  `TokenType.Number` consumers, not only the most common expression path.
- Focused proof for this class should include local sloppy and strict legacy
  octal regressions plus the owning Test262 filter:
  `FullyQualifiedName~Literals_numeric&FullyQualifiedName~legacy-octal-integer.js`.
- This ADR is caused by issue #1749 / PR #1787 and is enforced by
  `.claude/rules/ecmascript-numeric-literals.md`.
