# Date_prototype_setSeconds

FQN:
`Asynkron.JsEngine.Tests.Test262.BuiltInsTests.Date_prototype_setSeconds`

Full test name:

- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.Date_prototype_setSeconds("built-ins/Date/prototype/setSeconds/arg-ms-to-number.js",False)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.Date_prototype_setSeconds("built-ins/Date/prototype/setSeconds/arg-ms-to-number.js",True)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.Date_prototype_setSeconds("built-ins/Date/prototype/setSeconds/arg-sec-to-number.js",False)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.Date_prototype_setSeconds("built-ins/Date/prototype/setSeconds/arg-sec-to-number.js",True)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.Date_prototype_setSeconds("built-ins/Date/prototype/setSeconds/date-value-read-before-tonumber-when-date-is-invalid.js",False)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.Date_prototype_setSeconds("built-ins/Date/prototype/setSeconds/date-value-read-before-tonumber-when-date-is-invalid.js",True)

---
## Diagnosis (2026-02-03)

**Summary:** `setSeconds` overwrote an invalid [[DateValue]] after `ToNumber` coercion, which erased side effects from argument valueOf. It also treated `NaN` components as `0`.

**Fixes:**
- Treat explicitly provided `NaN` time components as `NaN` in `SetTimeComponents`.
- When the stored time is `NaN`, return `NaN` after argument coercions without overwriting the date.

**Tests:**
`dotnet test tests/Asynkron.JsEngine.Tests.Test262 -c Release --settings tests/Asynkron.JsEngine.Tests.Test262/BuiltInsTests.runsettings --filter "Name=Date_prototype_setSeconds"`

** DONE **
