# IsNaN

FQN:
`Asynkron.JsEngine.Tests.Test262.BuiltInsTests.IsNaN`

Full test name:

- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.IsNaN("built-ins/isNaN/toprimitive-not-callable-throws.js",False)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.IsNaN("built-ins/isNaN/toprimitive-not-callable-throws.js",True)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.IsNaN("built-ins/isNaN/toprimitive-result-is-object-throws.js",False)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.IsNaN("built-ins/isNaN/toprimitive-result-is-object-throws.js",True)

---
## Diagnosis (2026-02-03)

**Summary:** `isNaN` already followed `ToNumber` semantics; tests passed as-is.

**Tests:**
`dotnet test tests/Asynkron.JsEngine.Tests.Test262 -c Release --settings tests/Asynkron.JsEngine.Tests.Test262/BuiltInsTests.runsettings --filter "Name=ParseInt|Name=ParseFloat|Name=IsNaN|Name=IsFinite"`

** DONE **
