# Date_prototype_setMinutes

FQN:
`Asynkron.JsEngine.Tests.Test262.BuiltInsTests.Date_prototype_setMinutes`

Full test name:

- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.Date_prototype_setMinutes("built-ins/Date/prototype/setMinutes/arg-min-to-number.js",False)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.Date_prototype_setMinutes("built-ins/Date/prototype/setMinutes/arg-min-to-number.js",True)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.Date_prototype_setMinutes("built-ins/Date/prototype/setMinutes/arg-ms-to-number.js",False)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.Date_prototype_setMinutes("built-ins/Date/prototype/setMinutes/arg-ms-to-number.js",True)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.Date_prototype_setMinutes("built-ins/Date/prototype/setMinutes/arg-sec-to-number.js",False)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.Date_prototype_setMinutes("built-ins/Date/prototype/setMinutes/arg-sec-to-number.js",True)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.Date_prototype_setMinutes("built-ins/Date/prototype/setMinutes/date-value-read-before-tonumber-when-date-is-invalid.js",False)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.Date_prototype_setMinutes("built-ins/Date/prototype/setMinutes/date-value-read-before-tonumber-when-date-is-invalid.js",True)

---
## Diagnosis (2026-02-03)

**Summary:** `setMinutes` coerced `NaN` arguments to `0` via `SetTimeComponents`, so `date.setMinutes()` produced a valid time instead of `NaN`. Invalid dates were also overwritten after argument coercion.

**Fixes:**
- Treat explicitly provided `NaN` time components as `NaN` in `SetTimeComponents`.
- When the stored time is `NaN`, return `NaN` after argument coercions without overwriting the date.

**Tests:**
`dotnet test tests/Asynkron.JsEngine.Tests.Test262 -c Release --settings tests/Asynkron.JsEngine.Tests.Test262/BuiltInsTests.runsettings --filter "Name=Date_prototype_setMinutes"`

** DONE **
