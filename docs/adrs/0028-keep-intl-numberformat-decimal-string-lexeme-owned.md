# ADR 0028: Keep Intl NumberFormat decimal-string lexemes owned

## Status

Accepted

## Context

Issue #807 / PR #1001 fixed the Test262
`Intl402Tests.NumberFormat_prototype_format` failures for decimal-string,
negative-number, and unit-format cases. The decimal-string failures exposed a
boundary that the generic numeric conversion path cannot represent: ECMA-402
allows `Intl.NumberFormat.prototype.format` to receive a string whose
mathematical value must be formatted without first losing precision through a
binary `double`.

The first repair correctly moved decimal-string handling into the Intl
formatter, but review found a cap-boundary blocker. The exact
`BigInteger`-based decimal parser rejected strings such as `1e-1001` when the
scale exceeded the parser's bounded exact-decimal cap, then fell back to
generic `ToNumeric` conversion. For scientific notation that fallback underflow
turned the original nonzero decimal-string lexeme into `0`, even though the
formatter can preserve the exponent without materializing an enormous fixed
decimal string.

The final delivery kept coefficient size and positive-exponent materialization
bounded, reused the ECMAScript whitespace set, and made large positive scale
preservation context-aware for scientific and engineering notation only.

Issue #808 / PR #1004 exposed the same boundary on the range helpers after
`formatRangeToParts` still converted endpoints through the older numeric path
and then composed range parts independently from `formatRange`. That split could
drop decimal-string precision on range endpoints and let locale/range-affix
rules drift between the string result and the observable parts result. The
repair routed both helpers through the same `FormatNumericForRange` endpoint
formatting, kept currency affix sharing sign-compatible, and scoped the
Portuguese hyphen separator override to `pt-PT` instead of every Portuguese
locale.

## Decision

Keep `Intl.NumberFormat` string input handling in an Intl-owned decimal-string
path before generic numeric conversion. The path should preserve the source
lexeme's mathematical value when formatter notation can represent it, while
keeping exact decimal materialization bounded.

For future `Intl.NumberFormat` work:

1. parse string inputs with ECMAScript whitespace trimming, not host
   culture/default trimming;
2. preserve decimal-string precision before falling back to `ToNumeric` and
   `double` conversion;
3. bound coefficient digits and positive-exponent `BigInteger` growth before
   appending powers of ten;
4. allow very large positive decimal scale only for notations that can preserve
   the exponent without materializing the full fixed decimal, currently
   scientific and engineering notation;
5. keep ordinary decimal/compact formatting on the bounded exact-decimal path
   so huge fixed output does not create unbounded allocation or CPU work; and
6. keep `formatRange` and `formatRangeToParts` on the same endpoint numeric
   formatting and range-composition path so decimal-string precision, affix
   sharing, separator choice, and parts boundaries cannot drift; and
7. prove cap boundaries with focused regressions, including values just beyond
   the exact-decimal cap such as `1e-1001`.

## Consequences

- Future decimal-string fixes should extend `IntlNumberFormatter` and
  `NumericStringParser` rather than changing global `ToNumeric` semantics for
  all string-to-number conversions.
- Scientific and engineering notation can preserve large negative exponents as
  exponents; ordinary notation still needs bounded exact-decimal materialization
  or a deliberate separate design.
- Review should look for fallback paths that silently reintroduce `double`
  precision loss after an exact decimal parse rejects a cap boundary.
- Tests should include local Intl regressions plus the focused
  `Name=NumberFormat_prototype_format` Test262 method group when the issue came
  from that cluster.
- Range-helper changes should also include local equality/parts-shape
  regressions plus the focused `Name=NumberFormat_prototype_formatRange` and
  `Name=NumberFormat_prototype_formatRangeToParts` Test262 method groups when
  the issue crosses both range surfaces.
- This ADR is caused by issue #807 / PR #1001 and complements the root
  `.claude/rules/ecmascript-numeric-coercions.md` rule for numeric lexeme
  preservation boundaries. It was extended by issue #808 / PR #1004 for the
  range-helper recurrence.
