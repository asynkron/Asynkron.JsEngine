# EncodeURI

FQN:
`Asynkron.JsEngine.Tests.Test262.BuiltInsTests.EncodeURI`

Full test name:

- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.EncodeURI("built-ins/encodeURI/S15.1.3.3_A2.3_T1.js",False)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.EncodeURI("built-ins/encodeURI/S15.1.3.3_A2.3_T1.js",True)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.EncodeURI("built-ins/encodeURI/S15.1.3.3_A2.4_T1.js",False)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.EncodeURI("built-ins/encodeURI/S15.1.3.3_A2.4_T1.js",True)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.EncodeURI("built-ins/encodeURI/S15.1.3.3_A2.5_T1.js",False)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.EncodeURI("built-ins/encodeURI/S15.1.3.3_A2.5_T1.js",True)

---
## Diagnosis (2026-02-03)

**Summary:** `encodeURI` behavior already matched the spec; the decode fixes shared the same Global helper code and the encode tests pass.

**Tests:**
`dotnet test tests/Asynkron.JsEngine.Tests.Test262 -c Release --settings tests/Asynkron.JsEngine.Tests.Test262/BuiltInsTests.runsettings --filter "Name=EncodeURI|Name=EncodeURIComponent|Name=DecodeURI|Name=DecodeURIComponent"`

** DONE **
