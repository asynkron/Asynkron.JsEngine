# Date_prototype_valueOf

FQN:
`Asynkron.JsEngine.Tests.Test262.BuiltInsTests.Date_prototype_valueOf`

Full test name:

- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.Date_prototype_valueOf("built-ins/Date/prototype/valueOf/S9.4_A3_T1.js",False)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.Date_prototype_valueOf("built-ins/Date/prototype/valueOf/S9.4_A3_T1.js",True)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.Date_prototype_valueOf("built-ins/Date/prototype/valueOf/S9.4_A3_T2.js",False)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.Date_prototype_valueOf("built-ins/Date/prototype/valueOf/S9.4_A3_T2.js",True)

---
## Diagnosis (2026-02-03)

**Summary:** Current `Date.prototype.valueOf` already matches Test262 expectations.

**Fixes:**
- No code changes required; verified passing tests.

**Tests:**
`dotnet test tests/Asynkron.JsEngine.Tests.Test262 -c Release --settings tests/Asynkron.JsEngine.Tests.Test262/BuiltInsTests.runsettings --filter "Name=Date_prototype_valueOf"`

** DONE **
