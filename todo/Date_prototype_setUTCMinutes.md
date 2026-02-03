# Date_prototype_setUTCMinutes

FQN:
`Asynkron.JsEngine.Tests.Test262.BuiltInsTests.Date_prototype_setUTCMinutes`

Full test name:

- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.Date_prototype_setUTCMinutes("built-ins/Date/prototype/setUTCMinutes/date-value-read-before-tonumber-when-date-is-invalid.js",False)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.Date_prototype_setUTCMinutes("built-ins/Date/prototype/setUTCMinutes/date-value-read-before-tonumber-when-date-is-invalid.js",True)

---
## Diagnosis (2026-02-03)

**Summary:** For invalid dates, `setUTC*` methods must still call `ToNumber` on arguments, but if the original `[[DateValue]]` is `NaN`, they must return `NaN` without overwriting the date (so side effects in `valueOf` are preserved).

**Fixes:**
- After argument conversions, short-circuit on `NaN` time values before writing back to the date object.

**Tests:**
`dotnet test tests/Asynkron.JsEngine.Tests.Test262 -c Release --settings tests/Asynkron.JsEngine.Tests.Test262/BuiltInsTests.runsettings --filter "Name=Date_prototype_setUTCMinutes|Name=Date_prototype_setUTCHours|Name=Date_prototype_setUTCSeconds"`

** DONE **
