# Collator

FQN:
`Asynkron.JsEngine.Tests.Test262.Intl402Tests.Collator`

Full test name:

- Asynkron.JsEngine.Tests.Test262.Intl402Tests.Collator("intl402/Collator/legacy-regexp-statics-not-modified.js",False)
- Asynkron.JsEngine.Tests.Test262.Intl402Tests.Collator("intl402/Collator/legacy-regexp-statics-not-modified.js",True)
- Asynkron.JsEngine.Tests.Test262.Intl402Tests.Collator("intl402/Collator/taint-Object-prototype.js",False)
- Asynkron.JsEngine.Tests.Test262.Intl402Tests.Collator("intl402/Collator/taint-Object-prototype.js",True)

---
## Diagnosis (2026-01-22)

**Summary:** Collator construction mutates RegExp legacy statics and returns a default locale (`sv-SE`) that fails canonicalization checks.

**Error Pattern:**
```
Unhandled JavaScript throw: RegExp has unexpected property $1 with value .
Unhandled JavaScript throw: Collator returns invalid locale sv-SE.
```

**Analysis:**
Both strict and non-strict variants of `legacy-regexp-statics-not-modified.js` fail after `new Intl.Collator("de-DE-u-co-phonebk")` because internal locale parsing/validation updates `RegExp.$1`, which the Test262 harness treats as a forbidden change to legacy RegExp statics. The `taint-Object-prototype.js` failures show `resolvedOptions().locale` returning the host default locale (`sv-SE`) that does not pass `isCanonicalizedStructurallyValidLanguageTag`, implying the default locale resolution/canonicalization path is not producing a structurally valid BCP-47 tag accepted by `Intl.getCanonicalLocales`.

**Fix Direction:**
Avoid JS RegExp side effects in Intl locale parsing by using non-RegExp parsing or saving/restoring legacy RegExp match state around internal regex usage. Canonicalize and validate the host default locale (via the same path as `Intl.getCanonicalLocales`) before storing it in Collator internal slots/resolvedOptions.

** DONE **
