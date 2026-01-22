# Collator_prototype_compare

FQN:
`Asynkron.JsEngine.Tests.Test262.Intl402Tests.Collator_prototype_compare`

Full test name:

- Asynkron.JsEngine.Tests.Test262.Intl402Tests.Collator_prototype_compare("intl402/Collator/prototype/compare/canonically-equivalent-strings.js",False)
- Asynkron.JsEngine.Tests.Test262.Intl402Tests.Collator_prototype_compare("intl402/Collator/prototype/compare/canonically-equivalent-strings.js",True)
- Asynkron.JsEngine.Tests.Test262.Intl402Tests.Collator_prototype_compare("intl402/Collator/prototype/compare/compare-function-length.js",False)
- Asynkron.JsEngine.Tests.Test262.Intl402Tests.Collator_prototype_compare("intl402/Collator/prototype/compare/compare-function-length.js",True)
- Asynkron.JsEngine.Tests.Test262.Intl402Tests.Collator_prototype_compare("intl402/Collator/prototype/compare/compare-function-name.js",False)
- Asynkron.JsEngine.Tests.Test262.Intl402Tests.Collator_prototype_compare("intl402/Collator/prototype/compare/compare-function-name.js",True)
- Asynkron.JsEngine.Tests.Test262.Intl402Tests.Collator_prototype_compare("intl402/Collator/prototype/compare/compare-function-property-order.js",False)
- Asynkron.JsEngine.Tests.Test262.Intl402Tests.Collator_prototype_compare("intl402/Collator/prototype/compare/compare-function-property-order.js",True)
- Asynkron.JsEngine.Tests.Test262.Intl402Tests.Collator_prototype_compare("intl402/Collator/prototype/compare/ignorePunctuation.js",False)
- Asynkron.JsEngine.Tests.Test262.Intl402Tests.Collator_prototype_compare("intl402/Collator/prototype/compare/ignorePunctuation.js",True)
- Asynkron.JsEngine.Tests.Test262.Intl402Tests.Collator_prototype_compare("intl402/Collator/prototype/compare/non-normative-phonebook.js",False)
- Asynkron.JsEngine.Tests.Test262.Intl402Tests.Collator_prototype_compare("intl402/Collator/prototype/compare/non-normative-phonebook.js",True)

---
## Diagnosis (2026-01-22)

**Summary:** Intl.Collator.prototype.compare returns a plain HostFunction without spec-defined bound properties, and the compare algorithm relies on .NET CompareInfo without canonical normalization or Intl options (ignorePunctuation/collation), so several Intl402 expectations fail.

**Error Pattern:**
```
Unhandled JavaScript throw: obj should have an own property length
Unhandled JavaScript throw: obj should have an own property name
Actual [] and expected [length, name] should have the same contents.
Compare to space Expected SameValue(0, -1) to be true
Actual [A, \u00c4, Ab, Af, b, \u00f6, od, off] and expected [A, Ab, \u00c4, Af, b, od, \u00f6, off] should have the same contents.
Unhandled JavaScript throw: (canonically-equivalent-strings.js assert.sameValue(..., 0) fails)
```

**Analysis:**
The compare accessor creates a new HostFunction every time and does not define own "length" or "name" properties; Object.getOwnPropertyNames(compareFn) is empty, so property-order, name, and length tests fail. The comparison path uses CompareInfo.Compare with no Unicode normalization, so canonical-equivalence pairs compare non-zero. ignorePunctuation maps to CompareOptions.IgnoreSymbols only, which does not ignore whitespace, so compare("", " ") returns -1 even when ignorePunctuation is true. The collation option (co=phonebk) is dropped when resolving culture, so phonebook sorting is never applied.

**Fix Direction:**
Add a cached [[BoundCompare]] on the collator and return it from the getter; create a bound function once, then DefinePropertyOrThrow for "length" (2) and "name" ("") with the required attributes and definition order. Normalize inputs (e.g., NFC) in CompareStrings (or use an ICU-backed collator) to guarantee canonical equivalence. Implement ignorePunctuation by filtering Unicode punctuation/whitespace or a collation engine that supports it. Preserve collation extensions (phonebk) when selecting the comparer or implement a dedicated phonebook collation path.
