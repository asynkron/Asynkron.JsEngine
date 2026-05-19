# ADR 0022: Keep Intl Locale canonicalization grammar owned

## Status

Accepted

## Context

Issue #795 / PR #988 fixed the Test262 `Intl402Tests.Locale` failures for the
`intl402/Locale` constructor, getter, Unicode extension, and likely-subtags
fixtures. The failing cases were not isolated to one option setter. They exposed
that `Intl.Locale` canonicalization needs BCP47 grammar ownership in the shared
Locale path:

- variant option parsing had accepted empty, dash-damaged, and duplicate
  variants because splitting removed empty entries and canonicalization was
  delegated too late;
- four-character digit-leading subtags such as `1901` were being treated as a
  script in Locale getter parsing, even though they are variants;
- Locale base-name extraction stopped only at selected extensions and therefore
  failed to preserve arbitrary singleton extensions through
  `maximize()`/`minimize()`; and
- duplicate Unicode extension keywords overwrote the first value, while the
  observable canonical form keeps the first keyword value and drops later
  duplicates.

The fix kept the change in `IntlLocaleConstructor` and `IntlUtilities`, added
focused `IntlLocaleDebugTests`, and proved the result with the issue's narrow
Test262 method group instead of widening to the full Test262 suite.

## Decision

Keep `Intl.Locale` tag normalization and option merging grammar-owned inside the
Locale/Intl helper layer. Do not treat BCP47 subtags as simple dash-separated
strings after the tag has been accepted.

For future `Intl.Locale` work:

1. preserve empty subtags while validating user-provided option sequences so
   leading, trailing, and doubled separators remain observable errors;
2. validate duplicate-sensitive subtags before canonical output sorting, using
   the normalized subtag as the duplicate key;
3. classify script, region, variant, and extension singleton boundaries by
   BCP47 subtag grammar, not by fixed string searches such as `-u-` only;
4. keep digit-leading four-character variants out of script parsing;
5. preserve arbitrary extension singletons when extracting the Locale
   `baseName` for likely-subtags operations; and
6. when parsing Unicode extension keywords, keep the first duplicate keyword
   value and ignore later duplicates unless a spec change proves otherwise.

## Consequences

- Future Locale fixes should extend shared parsing/canonicalization helpers or
  tightly scoped constructor option validation, not add fixture-specific output
  branches.
- Tests should include local regressions for the grammar edge and the focused
  `Name=Locale` Test262 method group when the issue came from Locale fixtures.
- Locale helper changes can affect other Intl constructors that reuse language
  tag canonicalization, so broadening helper behavior needs fixture-driven proof
  for the owning Test262 cluster.
- This ADR is caused by issue #795 / PR #988 and complements the root
  `.claude/rules/ecmascript-abstract-operations.md` rule for future Intl
  implementation work.
