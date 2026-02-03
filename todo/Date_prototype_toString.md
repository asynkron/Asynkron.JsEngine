# Date_prototype_toString

FQN:
`Asynkron.JsEngine.Tests.Test262.BuiltInsTests.Date_prototype_toString`

Full test name:

- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.Date_prototype_toString("built-ins/Date/prototype/toString/negative-year.js",False)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.Date_prototype_toString("built-ins/Date/prototype/toString/negative-year.js",True)

---
## Diagnosis (2026-02-03)

**Summary:** Date string formatting relied on `DateTimeOffset`, which cannot represent negative years, causing year `-1` to serialize as `0001`. We now format negative years using the ECMAScript time algorithms and custom formatting.

**Fixes:**
- Add manual formatting helpers that compute year/month/day/weekday/time from the ECMAScript time value and format signed years with at least four digits.
- Use the manual formatters for out-of-range years in `toString`, `toDateString`, and `toUTCString`.

**Tests:**
`dotnet test tests/Asynkron.JsEngine.Tests.Test262 -c Release --settings tests/Asynkron.JsEngine.Tests.Test262/BuiltInsTests.runsettings --filter "Name=Date_prototype_toDateString|Name=Date_prototype_toString|Name=Date_prototype_toUTCString"`

** DONE **
