# Date_prototype_setHours

FQN:
`Asynkron.JsEngine.Tests.Test262.BuiltInsTests.Date_prototype_setHours`

Full test name:

- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.Date_prototype_setHours("built-ins/Date/prototype/setHours/arg-hour-to-number.js",False)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.Date_prototype_setHours("built-ins/Date/prototype/setHours/arg-hour-to-number.js",True)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.Date_prototype_setHours("built-ins/Date/prototype/setHours/arg-min-to-number.js",False)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.Date_prototype_setHours("built-ins/Date/prototype/setHours/arg-min-to-number.js",True)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.Date_prototype_setHours("built-ins/Date/prototype/setHours/arg-ms-to-number.js",False)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.Date_prototype_setHours("built-ins/Date/prototype/setHours/arg-ms-to-number.js",True)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.Date_prototype_setHours("built-ins/Date/prototype/setHours/arg-sec-to-number.js",False)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.Date_prototype_setHours("built-ins/Date/prototype/setHours/arg-sec-to-number.js",True)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.Date_prototype_setHours("built-ins/Date/prototype/setHours/date-value-read-before-tonumber-when-date-is-invalid.js",False)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.Date_prototype_setHours("built-ins/Date/prototype/setHours/date-value-read-before-tonumber-when-date-is-invalid.js",True)

---
## Diagnosis (2026-02-03)

**Summary:** `setHours` coerced `NaN` (including missing/undefined arguments) into `0` inside `SetTimeComponents`, so `date.setHours()` returned a valid time instead of `NaN`. When the stored time was invalid, the method also overwrote the [[DateValue]] after argument coercions.

**Fixes:**
- Treat explicitly provided `NaN` time components as `NaN` in `SetTimeComponents`.
- When the stored time is `NaN`, return `NaN` after argument coercions without overwriting the date.

**Tests:**
`dotnet test tests/Asynkron.JsEngine.Tests.Test262 -c Release --settings tests/Asynkron.JsEngine.Tests.Test262/BuiltInsTests.runsettings --filter "Name=Date_prototype_setHours"`

** DONE **
