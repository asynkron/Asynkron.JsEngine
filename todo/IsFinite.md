# IsFinite

FQN:
`Asynkron.JsEngine.Tests.Test262.BuiltInsTests.IsFinite`

Full test name:

- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.IsFinite("built-ins/isFinite/tonumber-operations.js",False)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.IsFinite("built-ins/isFinite/tonumber-operations.js",True)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.IsFinite("built-ins/isFinite/toprimitive-not-callable-throws.js",False)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.IsFinite("built-ins/isFinite/toprimitive-not-callable-throws.js",True)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.IsFinite("built-ins/isFinite/toprimitive-result-is-object-throws.js",False)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.IsFinite("built-ins/isFinite/toprimitive-result-is-object-throws.js",True)

---
## Diagnosis (2026-02-03)

**Summary:** `isFinite` was treating string inputs with `double.TryParse`, which does not match `ToNumber` semantics (e.g., empty string should become `+0`). Switched to `ToNumber` for all inputs.

**Fixes:**
- Always convert via `JsOps.ToNumber` before the finite check.

**Tests:**
`dotnet test tests/Asynkron.JsEngine.Tests.Test262 -c Release --settings tests/Asynkron.JsEngine.Tests.Test262/BuiltInsTests.runsettings --filter "Name=ParseInt|Name=ParseFloat|Name=IsNaN|Name=IsFinite"`

** DONE **
