# ADR 0021: Keep Intl language-tag extension canonicalization segmented

## Status

Accepted

## Context

Issue #793 fixed the Test262 `LanguageTagsCanonicalized` failures for
`intl402/language-tags-canonicalized.js`. The failing case
`cmn-hans-cn-u-ca-t-ca-x-t-u` mixed a Unicode locale extension, a transform
extension, and private-use subtags in one language tag.

The previous canonicalization helpers treated extension removal and extraction
too broadly. Removing Unicode extensions also consumed sibling extension
content, and resolved-locale extraction could carry an incomplete Unicode
keyword such as bare `ca` into `resolvedOptions().locale`. That made
`Intl.getCanonicalLocales`, `PluralRules.supportedLocalesOf`, and
`PluralRules.resolvedOptions().locale` disagree with ECMA-402/Test262 on the
same canonicalized input.

## Decision

Keep language-tag canonicalization segmented by extension singleton.

For Intl locale tags:

1. remove only the `-u-` Unicode locale extension when deriving locale-match
   base names;
2. preserve sibling extensions such as transform `-t-` and private-use `-x-`
   when returning canonical tags from locale-list APIs;
3. extract only the Unicode extension that appears before private-use data when
   resolving locale options;
4. omit incomplete non-boolean Unicode keywords from resolved locale output,
   while preserving boolean keywords that ECMA-402 allows without a value; and
5. prove mixed-extension fixes across the caller surfaces that can observe the
   split: canonical locale list output, supported-locale output, and
   resolved-options locale output.

## Consequences

- Future `IntlUtilities` changes must parse BCP-47 extension boundaries instead
  of using broad string slicing or "drop all extensions" helpers.
- `supportedLocalesOf`-style APIs may need the full canonical requested tag
  even while locale matching uses a Unicode-extension-free base name.
- `resolvedOptions().locale` must reflect only the usable Unicode extension
  keywords that survive ECMA-402 resolution; transform and private-use subtags
  are not locale option keywords.
- Focused regressions for language-tag canonicalization should include mixed
  Unicode, transform, and private-use subtags when the fix touches shared
  locale helpers.
- This ADR is caused by issue #793 / PR #985 and is enforced by
  `.claude/rules/ecmascript-intl-language-tags.md`.
