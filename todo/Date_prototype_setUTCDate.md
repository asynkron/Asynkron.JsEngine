# Date_prototype_setUTCDate

FQN:
`Asynkron.JsEngine.Tests.Test262.BuiltInsTests.Date_prototype_setUTCDate`

Full test name:

- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.Date_prototype_setUTCDate("built-ins/Date/prototype/setUTCDate/date-value-read-before-tonumber-when-date-is-invalid.js",False)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.Date_prototype_setUTCDate("built-ins/Date/prototype/setUTCDate/date-value-read-before-tonumber-when-date-is-invalid.js",True)

---
## Diagnosis (2026-02-03)

**Summary:** `setUTCDate`, `setUTCMilliseconds`, and `setUTCMonth` must read the stored `[[DateValue]]` before `ToNumber` conversions, but when the stored value is `NaN`, they must return `NaN` without writing back to the date (to preserve side effects in `valueOf`).

**Fixes:**
- After argument conversions, short-circuit on `NaN` time values before updating the internal date.

**Tests:**
`dotnet test tests/Asynkron.JsEngine.Tests.Test262 -c Release --settings tests/Asynkron.JsEngine.Tests.Test262/BuiltInsTests.runsettings --filter "Name=Date_prototype_setUTCDate|Name=Date_prototype_setUTCMilliseconds|Name=Date_prototype_setUTCMonth"`

** DONE **
