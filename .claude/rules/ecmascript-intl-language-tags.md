# ECMAScript Intl Language Tags

When changing Intl language-tag parsing, validation, or canonicalization,
preserve BCP-47 grammar boundaries instead of treating locale tags as generic
dash-separated strings or one broad regular expression.

## Rules

1. Remove only the Unicode locale extension (`-u-...`) when deriving a locale
   matching base name. Do not drop transform (`-t-...`) or private-use
   (`-x-...`) subtags from canonical locale-list results.
2. Stop Unicode extension extraction at private-use data. Private-use content is
   not a Unicode locale keyword source for `resolvedOptions().locale`.
3. Do not emit incomplete non-boolean Unicode keywords in resolved locale
   output. A bare `ca` must be omitted; only spec-allowed boolean keywords such
   as `kn` may survive without a value.
4. Preserve the distinction between canonical tag output and resolved locale
   output. `Intl.getCanonicalLocales` and `supportedLocalesOf` may expose the
   full canonical requested tag, while `resolvedOptions().locale` exposes only
   the locale and usable Unicode option extension.
5. Add focused regression coverage that observes all affected surfaces when a
   shared helper changes: canonical locale list output, supported-locale output,
   and resolved-options locale output. Use the exact Test262 method group or
   file that exposed the bug as the external proof.
6. Keep structural validation bounded for untrusted locale input. Parse
   language, script, region, variant, extension, transform, and private-use
   subtags with monotonic index movement instead of reintroducing a single large
   backtracking regex as the primary validity check.
7. Reject duplicate variants and duplicate extension singletons during
   validation, before canonicalization can sort, normalize, or otherwise hide
   the malformed input shape.
8. Keep grandfathered and complex language alias data in `IntlLocaleData` when a
   canonical mapping is data-owned. Pin representative mappings, such as
   `sgn-GR` to `gss`, against local regressions and host `Intl` behavior.

## Why

Issue #793 / PR #985 fixed `Test262: LanguageTagsCanonicalized failures` after
`IntlUtilities.RemoveUnicodeExtensions` and `ExtractUnicodeExtension` blurred
Unicode, transform, and private-use extension segments for the tag
`cmn-hans-cn-u-ca-t-ca-x-t-u`. The repair preserved sibling transform and
private-use subtags for canonical/supported locale output while omitting the
incomplete `ca` Unicode keyword from `PluralRules.resolvedOptions().locale`.

Issue #1338 / PR #1349 fixed an Intl locale validation crash bucket after a
large generated BCP-47 regex made malformed or very long locale tags able to
consume unbounded validation time. The repair made structural validation
parser-owned and bounded, rejected duplicate variants/singletons before
canonicalization, and added the `sgn-GR` to `gss` complex language mapping from
`IntlLocaleData`.

Future agents should treat BCP-47 extension parsing as structured
canonicalization and validation work, not as broad extension stripping or broad
regex matching.
