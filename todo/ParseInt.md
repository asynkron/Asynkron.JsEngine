# ParseInt

FQN:
`Asynkron.JsEngine.Tests.Test262.BuiltInsTests.ParseInt`

Full test name:

- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.ParseInt("built-ins/parseInt/S15.1.2.2_A3.2_T1.js",False)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.ParseInt("built-ins/parseInt/S15.1.2.2_A3.2_T1.js",True)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.ParseInt("built-ins/parseInt/S15.1.2.2_A3.2_T3.js",False)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.ParseInt("built-ins/parseInt/S15.1.2.2_A3.2_T3.js",True)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.ParseInt("built-ins/parseInt/S15.1.2.2_A8.js",False)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.ParseInt("built-ins/parseInt/S15.1.2.2_A8.js",True)

---
## Diagnosis (2026-02-03)

**Summary:** `parseInt` used a raw `(int)` cast for the radix and accepted non-ASCII letters, which broke `ToInt32` modulo behavior and let Unicode letters count as digits.

**Fixes:**
- Use `JsNumericConversions.ToInt32` for the radix argument (modulo `2^32`).
- Restrict digit parsing to ASCII `0-9`, `A-Z`, `a-z` only.

**Tests:**
`dotnet test tests/Asynkron.JsEngine.Tests.Test262 -c Release --settings tests/Asynkron.JsEngine.Tests.Test262/BuiltInsTests.runsettings --filter "Name=ParseInt|Name=ParseFloat|Name=IsNaN|Name=IsFinite"`

** DONE **
