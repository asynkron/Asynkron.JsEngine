# ADR 0087: Keep Intl language-tag validation bounded

## Status

Accepted

## Context

Issue #1338 / PR #1349 fixed the Intl locale-list and language-tag
canonicalization crash bucket for representative Test262 fixtures including
`intl402/Intl/getCanonicalLocales/canonicalized-tags.js`,
`complex-region-subtag-replacement.js`, `invalid-tags.js`, and the
`language-tags-canonicalized.js` / `language-tags-invalid.js` pair.

The previous `IntlUtilities.IsStructurallyValidLanguageTag` path depended on a
single large generated regular expression plus duplicate-detection regular
expressions. That made malformed or very long locale input able to consume
unbounded validation time before the engine could return the required
JavaScript `RangeError`.

The repair replaced the regex validator with a bounded BCP-47 parser over
dash-separated subtags, while keeping canonicalization data-driven:

- language, script, region, variant, Unicode extension, transform extension,
  generic extension, and private-use segments are parsed explicitly;
- duplicate variants and duplicate extension singletons are rejected during
  structural validation;
- Unicode and transform extension payload rules remain distinct; and
- grandfathered/complex language mappings are kept in `IntlLocaleData`, including
  the `sgn-GR` to `gss` canonical mapping verified against host `Intl`.

## Decision

Keep Intl language-tag structural validation bounded and parser-owned. Do not
reintroduce one large regex as the primary validity check for locale tags.

For future Intl language-tag work:

1. parse untrusted locale input subtag-by-subtag with monotonic index movement;
2. classify BCP-47 segments by grammar before canonical output rewriting;
3. reject duplicate variants and duplicate extension singletons during
   validation, before canonicalization can hide the malformed shape;
4. keep Unicode extension, transform extension, generic extension, and
   private-use rules separate; and
5. prove long-invalid input with a local timeout-bounded regression plus the
   focused Test262 files or method groups that exposed the behavior.

## Consequences

- Future fixes should add small parser helpers or data-table entries, not
  broaden the validator into a backtracking pattern.
- Long invalid tags should fail as JavaScript `RangeError` within the normal
  test budget, rather than relying on Test262 or harness timeout exceptions.
- Canonical language alias data belongs in `IntlLocaleData` so representative
  mappings such as `sgn-GR` stay reviewable and can be checked against host
  `Intl`.
- This ADR is caused by issue #1338 / PR #1349 and is enforced by
  `.claude/rules/ecmascript-intl-language-tags.md`.
