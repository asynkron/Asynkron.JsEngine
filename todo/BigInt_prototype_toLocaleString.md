# BigInt_prototype_toLocaleString

FQN:
`Asynkron.JsEngine.Tests.Test262.Intl402Tests.BigInt_prototype_toLocaleString`

Full test name:

- Asynkron.JsEngine.Tests.Test262.Intl402Tests.BigInt_prototype_toLocaleString("intl402/BigInt/prototype/toLocaleString/de-DE.js",False)
- Asynkron.JsEngine.Tests.Test262.Intl402Tests.BigInt_prototype_toLocaleString("intl402/BigInt/prototype/toLocaleString/de-DE.js",True)
- Asynkron.JsEngine.Tests.Test262.Intl402Tests.BigInt_prototype_toLocaleString("intl402/BigInt/prototype/toLocaleString/default-options-object-prototype.js",False)
- Asynkron.JsEngine.Tests.Test262.Intl402Tests.BigInt_prototype_toLocaleString("intl402/BigInt/prototype/toLocaleString/default-options-object-prototype.js",True)
- Asynkron.JsEngine.Tests.Test262.Intl402Tests.BigInt_prototype_toLocaleString("intl402/BigInt/prototype/toLocaleString/en-US.js",False)
- Asynkron.JsEngine.Tests.Test262.Intl402Tests.BigInt_prototype_toLocaleString("intl402/BigInt/prototype/toLocaleString/en-US.js",True)
- Asynkron.JsEngine.Tests.Test262.Intl402Tests.BigInt_prototype_toLocaleString("intl402/BigInt/prototype/toLocaleString/returns-same-results-as-NumberFormat.js",False)
- Asynkron.JsEngine.Tests.Test262.Intl402Tests.BigInt_prototype_toLocaleString("intl402/BigInt/prototype/toLocaleString/returns-same-results-as-NumberFormat.js",True)

---
## Diagnosis (2026-01-22)

**Summary:** `BigInt.prototype.toLocaleString` fails because Intl.NumberFormat is accessed with the wrong receiver, and Intl.NumberFormat.format does not handle BigInt values (casts JsValue to double).

**Error Pattern:**
```
Asynkron.JsEngine.ThrowSignal: Unhandled JavaScript throw: 'TypeError': 'Intl.NumberFormat method called on incompatible receiver'

System.InvalidCastException: Unable to cast object of type 'Asynkron.JsEngine.JsTypes.JsValue' to type 'System.Double'.
   at Asynkron.JsEngine.StdLib.Intl.IntlNumberFormatPrototype.FormatNumberResult(...)
```

**Analysis:**
`BigInt.prototype.toLocaleString` routes through `StandardLibrary.TryFormatWithIntlNumberFormatJsValue`. That helper constructs `Intl.NumberFormat` and then grabs the `"format"` property via `IJsPropertyAccessor.TryGetProperty` without a receiver, which triggers the `Intl.NumberFormat` getter with an incompatible `this` and throws the `TypeError`. This breaks `default-options-object-prototype`, `de-DE`, and `en-US` tests before formatting occurs.

Separately, `Intl.NumberFormat.prototype.format` fails with BigInt because `FormatNumberResult` uses `JsOps.ToNumericAsJsValue`, stores the result as `object`, then attempts `(double)numericValue`. When the numeric value is a `JsValue` (or a BigInt wrapped in `JsValue`), the cast fails with `InvalidCastException`. This surfaces in `returns-same-results-as-NumberFormat` when calling `new Intl.NumberFormat(...).format(bigint)`.

**Fix Direction:**
Ensure `TryFormatWithIntlNumberFormatJsValue` retrieves `format` using the proper receiver (e.g., `accessor.TryGetProperty("format", formatter, out var formatValue)` or equivalent) so `ValidateNumberFormatReceiver` sees a branded NumberFormat instance. In `IntlNumberFormatPrototype.FormatNumberResult`, handle BigInt and number values explicitly by operating on a `JsValue` (e.g., `TryGetBigInt` / `TryGetNumber`) or switching to a numeric conversion helper that returns `JsBigInt` or `double` rather than a raw `JsValue`.
