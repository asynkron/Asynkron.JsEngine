# Date_prototype_toTimeString

FQN:
`Asynkron.JsEngine.Tests.Test262.BuiltInsTests.Date_prototype_toTimeString`

Full test name:

- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.Date_prototype_toTimeString("built-ins/Date/prototype/toTimeString/format.js",False)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.Date_prototype_toTimeString("built-ins/Date/prototype/toTimeString/format.js",True)

---
## Diagnosis (2026-02-03)

**Summary:** `Date.prototype.toTimeString` output must include a `GMT±HHMM` offset (no colon) and the local time zone name in parentheses. The previous formatting omitted the time zone name and used `±HH:MM`.

**Fixes:**
- Format as `HH:mm:ss GMT±HHMM (Time Zone Name)` using the configured time zone.

**Tests:**
`dotnet test tests/Asynkron.JsEngine.Tests.Test262 -c Release --settings tests/Asynkron.JsEngine.Tests.Test262/BuiltInsTests.runsettings --filter "Name=Date_prototype_toTimeString"`

** DONE **
