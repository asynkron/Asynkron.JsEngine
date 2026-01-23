# Array_prototype_toLocaleString

FQN:
`Asynkron.JsEngine.Tests.Test262.Intl402Tests.Array_prototype_toLocaleString`

Full test name:

- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.Array_prototype_toLocaleString("built-ins/Array/prototype/toLocaleString/resizable-buffer.js",False)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.Array_prototype_toLocaleString("built-ins/Array/prototype/toLocaleString/resizable-buffer.js",True)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.Array_prototype_toLocaleString("built-ins/Array/prototype/toLocaleString/user-provided-tolocalestring-grow.js",False)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.Array_prototype_toLocaleString("built-ins/Array/prototype/toLocaleString/user-provided-tolocalestring-grow.js",True)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.Array_prototype_toLocaleString("built-ins/Array/prototype/toLocaleString/user-provided-tolocalestring-shrink.js",False)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.Array_prototype_toLocaleString("built-ins/Array/prototype/toLocaleString/user-provided-tolocalestring-shrink.js",True)
- Asynkron.JsEngine.Tests.Test262.Intl402Tests.Array_prototype_toLocaleString("intl402/Array/prototype/toLocaleString/calls-toLocaleString-number-elements.js",False)
- Asynkron.JsEngine.Tests.Test262.Intl402Tests.Array_prototype_toLocaleString("intl402/Array/prototype/toLocaleString/calls-toLocaleString-number-elements.js",True)

---
## Diagnosis (2026-01-22)

**Summary:** `Number.prototype.toLocaleString` (and BigInt) fails when it tries to use `Intl.NumberFormat`, throwing an incompatible receiver error.

**Error Pattern:**
```
Asynkron.JsEngine.ThrowSignal: Unhandled JavaScript throw: 'TypeError': 'Intl.NumberFormat method called on incompatible receiver'
```

**Analysis:**
`Array.prototype.toLocaleString` correctly forwards `locales` and `options` to each element's `toLocaleString`. For numeric elements (Number/BigInt), the implementation calls `TryFormatWithIntlNumberFormatJsValue`, which builds an `Intl.NumberFormat` and reads its `format` getter. The getter throws because the receiver does not pass the `__numberFormat__` brand check, so every numeric `toLocaleString(locales, options)` blows up. This cascades into failures for the intl402 numeric element test and the resizable-buffer/user-provided toLocaleString tests that depend on Number/BigInt formatting.

**Fix Direction:**
Ensure the `Intl.NumberFormat` instance is correctly branded before accessing `format`, and that `TryFormatWithIntlNumberFormatJsValue` calls the constructor with proper construction semantics and uses the instance as the getter receiver. Concretely: verify `IntlNumberFormatConstructor` sets `__numberFormat__` in all call paths, and/or update `TryFormatWithIntlNumberFormatJsValue` to construct via the engine's construct path and retrieve `format` with the correct receiver.

** DONE **
