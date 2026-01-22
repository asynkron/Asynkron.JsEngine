# Collator_prototype_resolvedOptions

FQN:
`Asynkron.JsEngine.Tests.Test262.Intl402Tests.Collator_prototype_resolvedOptions`

Full test name:

- Asynkron.JsEngine.Tests.Test262.Intl402Tests.Collator_prototype_resolvedOptions("intl402/Collator/prototype/resolvedOptions/basic.js",False)
- Asynkron.JsEngine.Tests.Test262.Intl402Tests.Collator_prototype_resolvedOptions("intl402/Collator/prototype/resolvedOptions/basic.js",True)
- Asynkron.JsEngine.Tests.Test262.Intl402Tests.Collator_prototype_resolvedOptions("intl402/Collator/prototype/resolvedOptions/ignorePunctuation-default.js",False)
- Asynkron.JsEngine.Tests.Test262.Intl402Tests.Collator_prototype_resolvedOptions("intl402/Collator/prototype/resolvedOptions/ignorePunctuation-default.js",True)
- Asynkron.JsEngine.Tests.Test262.Intl402Tests.Collator_prototype_resolvedOptions("intl402/Collator/prototype/resolvedOptions/resolved-case-first-unicode-extensions-and-options.js",False)
- Asynkron.JsEngine.Tests.Test262.Intl402Tests.Collator_prototype_resolvedOptions("intl402/Collator/prototype/resolvedOptions/resolved-case-first-unicode-extensions-and-options.js",True)
- Asynkron.JsEngine.Tests.Test262.Intl402Tests.Collator_prototype_resolvedOptions("intl402/Collator/prototype/resolvedOptions/resolved-collation-unicode-extensions-and-options.js",False)
- Asynkron.JsEngine.Tests.Test262.Intl402Tests.Collator_prototype_resolvedOptions("intl402/Collator/prototype/resolvedOptions/resolved-collation-unicode-extensions-and-options.js",True)
- Asynkron.JsEngine.Tests.Test262.Intl402Tests.Collator_prototype_resolvedOptions("intl402/Collator/prototype/resolvedOptions/resolved-numeric-unicode-extensions-and-options.js",False)
- Asynkron.JsEngine.Tests.Test262.Intl402Tests.Collator_prototype_resolvedOptions("intl402/Collator/prototype/resolvedOptions/resolved-numeric-unicode-extensions-and-options.js",True)

---
## Diagnosis (2026-01-22)

**Summary:** Intl.Collator resolvedOptions composes locale/option defaults without locale data and without tracking Unicode extension precedence, producing non‑spec locales/values.

**Error Pattern:**
```
Asynkron.JsEngine.ThrowSignal: Unhandled JavaScript throw: Invalid locale: sv-SE
Asynkron.JsEngine.ThrowSignal: Unhandled JavaScript throw:
```

**Analysis:**
`basic.js` fails because `resolvedOptions().locale` (from host default locale, `sv-SE`) is rejected by
`isCanonicalizedStructurallyValidLanguageTag`, meaning our canonicalization/validation path does not
align with `Intl.getCanonicalLocales` expectations for the default locale. The other failures all
stem from spec‑incorrect resolution of options vs Unicode extensions and locale defaults:
- `ignorePunctuation-default.js` expects locale data defaults (Thai true, English/Japanese false) but
  `ignorePunctuation` is always defaulted to false.
- `resolved-*-unicode-extensions-and-options.js` expect `resolvedOptions.locale` to drop `-u-` keys
  when options override extension values or when extension values are unsupported for the locale.
  The implementation always re‑adds `co`/`kn`/`kf` whenever the resolved value is non‑default, and it
  treats collations as globally supported (not per‑locale), so `en` incorrectly accepts `phonebk`
  or `pinyin` and emits them in the locale.

**Fix Direction:**
- Canonicalize and validate the resolved locale using the same path as `Intl.getCanonicalLocales`,
  and fall back to a supported locale (e.g., `en`) if the host default locale fails.
- Introduce locale data for `ignorePunctuation` defaults (at minimum per‑locale overrides used by
  Test262: `th` => true, `en`/`ja` => false).
- Implement `ResolveLocale`/`ResolveLocaleData` behavior for `co`/`kn`/`kf`: track whether each
  resolved value came from Unicode extension vs options; include `-u-` keys in the resolved locale
  only when the extension value is used, and ignore unsupported extension values for a locale.
