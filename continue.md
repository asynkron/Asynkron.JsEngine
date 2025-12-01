# Language Suite Next Steps

## Current State
- Scientific and engineering notation now reuse a decimal-aware quantity pipeline: doubles are normalized through `decimal` when the value is in range, the mantissa formatter clamps the exponent to 1–3 integer digits, and `maximumFractionDigits` trimming keeps exponents stable.
- `Intl.NumberFormat.prototype.format` and `formatToParts` for scientific/engineering emit spec-shaped parts (`integer`, `decimal`, `fraction`, `exponentSeparator`, `exponentMinusSign`, `exponentInteger`, `minusSign`, `nan`, `infinity`) so the en-US Test262 slices pass.
- NaN/Infinity now honor the active culture’s symbols, exponent fragments include the locale’s minus sign, and the formatter exposes structured results so both `.format` and `.formatToParts` share rounding decisions.
- `BestAvailableLocale` now seeds plain language-region tags when .NET’s culture list only exposes script-specific names (e.g., `zh-Hant-TW`), so requested locales like `zh-TW` resolve to their real NumberFormat data and the zh-TW engineering/scientific cases pass.

## Next Iteration Plan
1. Broaden `formatToParts` coverage beyond scientific/engineering so decimal, percent, currency, and unit styles emit structured pieces instead of a single literal, and add regression tests for those pathways.
2. Audit the augmented locale availability map against CLDR so that DisplayNames, DateTimeFormat, and the rest of Intl observe the same script/region aliases (and add coverage for other languages that only surface script-specific .NET culture names).
3. Finish the pending NumberFormat option matrix: honor `useGrouping: "min2"`/`"always"`, surface scientific notation inside `formatRange`/`formatToParts`, and thread `signDisplay` plus `maximumFractionDigits` overrides through the new decimal quantity helpers.
