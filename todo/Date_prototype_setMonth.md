# Date_prototype_setMonth

FQN:
`Asynkron.JsEngine.Tests.Test262.BuiltInsTests.Date_prototype_setMonth`

Full test name:

- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.Date_prototype_setMonth("built-ins/Date/prototype/setMonth/date-value-read-before-tonumber-when-date-is-invalid.js",False)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.Date_prototype_setMonth("built-ins/Date/prototype/setMonth/date-value-read-before-tonumber-when-date-is-invalid.js",True)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.Date_prototype_setMonth("built-ins/Date/prototype/setMonth/this-value-valid-date-month.js",False)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.Date_prototype_setMonth("built-ins/Date/prototype/setMonth/this-value-valid-date-month.js",True)

---
## Diagnosis (2026-02-03)

**Summary:** `setMonth` overwrote invalid dates after argument coercion, erasing side effects from argument `valueOf` on `NaN` dates.

**Fixes:**
- After coercing arguments, return `NaN` if the stored time value is `NaN` (preserving side effects without overwriting).

**Tests:**
`dotnet test tests/Asynkron.JsEngine.Tests.Test262 -c Release --settings tests/Asynkron.JsEngine.Tests.Test262/BuiltInsTests.runsettings --filter "Name=Date_prototype_setMonth"`

** DONE **
