# ParseFloat

FQN:
`Asynkron.JsEngine.Tests.Test262.BuiltInsTests.ParseFloat`

Full test name:

- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.ParseFloat("built-ins/parseFloat/S15.1.2.3_A3_T3.js",False)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.ParseFloat("built-ins/parseFloat/S15.1.2.3_A3_T3.js",True)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.ParseFloat("built-ins/parseFloat/S15.1.2.3_A4_T4.js",False)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.ParseFloat("built-ins/parseFloat/S15.1.2.3_A4_T4.js",True)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.ParseFloat("built-ins/parseFloat/S15.1.2.3_A6.js",False)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.ParseFloat("built-ins/parseFloat/S15.1.2.3_A6.js",True)

---
## Diagnosis (2026-02-03)

**Summary:** `parseFloat` was relying on `double.TryParse`, which accepts lowercase `infinity` and rejects `Infinity` prefixes. Implemented a JS-spec prefix scan with explicit `Infinity` handling.

**Fixes:**
- Scan the longest `StrDecimalLiteral` prefix (digits, optional decimal, optional exponent).
- Handle case-sensitive `Infinity` (including `Infinity` prefixes).
- Reject non-numeric prefixes like `.x`, `+x`, and lowercase `infinity`.

**Tests:**
`dotnet test tests/Asynkron.JsEngine.Tests.Test262 -c Release --settings tests/Asynkron.JsEngine.Tests.Test262/BuiltInsTests.runsettings --filter "Name=ParseInt|Name=ParseFloat|Name=IsNaN|Name=IsFinite"`

** DONE **
