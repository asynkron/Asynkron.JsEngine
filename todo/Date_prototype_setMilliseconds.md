# Date_prototype_setMilliseconds

FQN:
`Asynkron.JsEngine.Tests.Test262.BuiltInsTests.Date_prototype_setMilliseconds`

Full test name:

- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.Date_prototype_setMilliseconds("built-ins/Date/prototype/setMilliseconds/arg-to-number.js",False)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.Date_prototype_setMilliseconds("built-ins/Date/prototype/setMilliseconds/arg-to-number.js",True)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.Date_prototype_setMilliseconds("built-ins/Date/prototype/setMilliseconds/date-value-read-before-tonumber-when-date-is-invalid.js",False)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.Date_prototype_setMilliseconds("built-ins/Date/prototype/setMilliseconds/date-value-read-before-tonumber-when-date-is-invalid.js",True)

---
## Diagnosis (2026-02-03)

**Summary:** `setMilliseconds` coerced `NaN` into `0` and overwrote invalid dates after argument coercion.

**Fixes:**
- Treat explicit `NaN` components as `NaN` in `SetTimeComponents`.
- Return `NaN` after argument coercions when the stored time is invalid, without overwriting the date.

**Tests:**
`dotnet test tests/Asynkron.JsEngine.Tests.Test262 -c Release --settings tests/Asynkron.JsEngine.Tests.Test262/BuiltInsTests.runsettings --filter "Name=Date_prototype_setMilliseconds"`

** DONE **
