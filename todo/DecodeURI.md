# DecodeURI

FQN:
`Asynkron.JsEngine.Tests.Test262.BuiltInsTests.DecodeURI`

Full test name:

- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.DecodeURI("built-ins/decodeURI/S15.1.3.1_A1.10_T1.js",False)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.DecodeURI("built-ins/decodeURI/S15.1.3.1_A1.10_T1.js",True)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.DecodeURI("built-ins/decodeURI/S15.1.3.1_A1.11_T1.js",False)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.DecodeURI("built-ins/decodeURI/S15.1.3.1_A1.11_T1.js",True)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.DecodeURI("built-ins/decodeURI/S15.1.3.1_A1.11_T2.js",False)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.DecodeURI("built-ins/decodeURI/S15.1.3.1_A1.11_T2.js",True)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.DecodeURI("built-ins/decodeURI/S15.1.3.1_A1.12_T1.js",False)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.DecodeURI("built-ins/decodeURI/S15.1.3.1_A1.12_T1.js",True)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.DecodeURI("built-ins/decodeURI/S15.1.3.1_A1.2_T1.js",False)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.DecodeURI("built-ins/decodeURI/S15.1.3.1_A1.2_T1.js",True)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.DecodeURI("built-ins/decodeURI/S15.1.3.1_A1.2_T2.js",False)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.DecodeURI("built-ins/decodeURI/S15.1.3.1_A1.2_T2.js",True)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.DecodeURI("built-ins/decodeURI/S15.1.3.1_A1.7_T1.js",True)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.DecodeURI("built-ins/decodeURI/S15.1.3.1_A1.8_T1.js",True)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.DecodeURI("built-ins/decodeURI/S15.1.3.1_A1.8_T2.js",False)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.DecodeURI("built-ins/decodeURI/S15.1.3.1_A1.8_T2.js",True)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.DecodeURI("built-ins/decodeURI/S15.1.3.1_A2.2_T1.js",False)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.DecodeURI("built-ins/decodeURI/S15.1.3.1_A2.2_T1.js",True)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.DecodeURI("built-ins/decodeURI/S15.1.3.1_A2.4_T1.js",False)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.DecodeURI("built-ins/decodeURI/S15.1.3.1_A2.4_T1.js",True)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.DecodeURI("built-ins/decodeURI/S15.1.3.1_A2.5_T1.js",False)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.DecodeURI("built-ins/decodeURI/S15.1.3.1_A2.5_T1.js",True)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.DecodeURI("built-ins/decodeURI/S15.1.3.1_A3_T2.js",False)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.DecodeURI("built-ins/decodeURI/S15.1.3.1_A3_T2.js",True)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.DecodeURI("built-ins/decodeURI/S15.1.3.1_A3_T3.js",False)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.DecodeURI("built-ins/decodeURI/S15.1.3.1_A3_T3.js",True)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.DecodeURI("built-ins/decodeURI/S15.1.3.1_A4_T2.js",False)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.DecodeURI("built-ins/decodeURI/S15.1.3.1_A4_T2.js",True)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.DecodeURI("built-ins/decodeURI/S15.1.3.1_A4_T4.js",False)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.DecodeURI("built-ins/decodeURI/S15.1.3.1_A4_T4.js",True)

---
## Diagnosis (2026-02-03)

**Summary:** `decodeURI` was preserving percent-encoded sequences for all unescaped characters and uppercasing them, and it grouped consecutive `%XX` sequences. It now only preserves reserved characters (`; / ? : @ & = + $ , #`) and keeps the original percent-escape casing, while decoding other ASCII bytes individually and validating UTF-8 sequences strictly.

**Fixes:**
- Preserve only reserved characters for `decodeURI`, using the original percent-encoded substring.
- Decode single-byte ASCII escapes individually (no grouping across reserved characters).
- Added strict UTF-8 sequence validation shared with `decodeURIComponent`.

**Tests:**
`dotnet test tests/Asynkron.JsEngine.Tests.Test262 -c Release --settings tests/Asynkron.JsEngine.Tests.Test262/BuiltInsTests.runsettings --filter "Name=EncodeURI|Name=EncodeURIComponent|Name=DecodeURI|Name=DecodeURIComponent"`

** DONE **
