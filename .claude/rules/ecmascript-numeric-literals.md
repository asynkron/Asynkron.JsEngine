# ECMAScript Numeric Literals

When changing JavaScript numeric literal lexing or parser consumption, keep
lexical value decoding separate from parser-only strict-mode validation.

## Rules

1. Do not reject legacy octal integer spellings such as `00`, `01`, `070`, or
   `077` in `JsLexer.ReadNumber()`. The lexer does not know whether the source
   is sloppy or strict after directive prologues and forced strict parse modes
   are considered.
2. Pure legacy octal integer forms must still decode as octal values when they
   are tokenized. Do not let a permissive lexer fallback parse `070` as decimal
   `70`; sloppy mode must observe the legacy octal value.
3. Keep strict-mode rejection for legacy octal numeric literals in parser
   validation that can see `InStrictContext`.
4. Apply strict numeric-literal validation to every parser surface that consumes
   `TokenType.Number`, including expression primaries, object property keys,
   and binding property names. Do not fix only the common expression path.
5. Preserve modern numeric-literal behavior independently: `0o`/`0O`,
   `0b`/`0B`, `0x`/`0X`, decimal fractions, exponents, numeric separators, and
   BigInt suffix handling must not drift because of legacy-octal handling.
6. Prove this class with local strict/sloppy numeric-literal regressions plus
   the owning Test262 filter
   `FullyQualifiedName~Literals_numeric&FullyQualifiedName~legacy-octal-integer.js`.

## Why

Issue #1749 / PR #1787 fixed a Test262 `noStrict` failure where
`JsLexer.ReadNumber()` unconditionally rejected sloppy legacy octal literals
before parser strictness was known. The fix accepted pure legacy octal integer
tokens with base-8 values, then moved strict rejection into parser validation
and applied it across numeric-token consumers. Future fixes in this area must
preserve that boundary so sloppy Annex B numeric literal semantics and strict
early errors both remain correct.

Related ADR: `docs/adrs/0117-keep-legacy-octal-literal-strictness-parser-owned.md`.
