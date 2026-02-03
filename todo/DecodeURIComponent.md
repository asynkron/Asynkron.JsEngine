# DecodeURIComponent

FQN:
`Asynkron.JsEngine.Tests.Test262.BuiltInsTests.DecodeURIComponent`

Full test name:

- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.DecodeURIComponent("built-ins/decodeURIComponent/S15.1.3.2_A1.11_T1.js",False)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.DecodeURIComponent("built-ins/decodeURIComponent/S15.1.3.2_A1.12_T1.js",True)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.DecodeURIComponent("built-ins/decodeURIComponent/S15.1.3.2_A1.12_T2.js",False)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.DecodeURIComponent("built-ins/decodeURIComponent/S15.1.3.2_A1.12_T3.js",False)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.DecodeURIComponent("built-ins/decodeURIComponent/S15.1.3.2_A1.2_T1.js",False)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.DecodeURIComponent("built-ins/decodeURIComponent/S15.1.3.2_A1.2_T1.js",True)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.DecodeURIComponent("built-ins/decodeURIComponent/S15.1.3.2_A1.2_T2.js",True)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.DecodeURIComponent("built-ins/decodeURIComponent/S15.1.3.2_A2.2_T1.js",False)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.DecodeURIComponent("built-ins/decodeURIComponent/S15.1.3.2_A2.2_T1.js",True)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.DecodeURIComponent("built-ins/decodeURIComponent/S15.1.3.2_A2.4_T1.js",False)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.DecodeURIComponent("built-ins/decodeURIComponent/S15.1.3.2_A2.4_T1.js",True)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.DecodeURIComponent("built-ins/decodeURIComponent/S15.1.3.2_A2.5_T1.js",False)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.DecodeURIComponent("built-ins/decodeURIComponent/S15.1.3.2_A2.5_T1.js",True)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.DecodeURIComponent("built-ins/decodeURIComponent/throw-URIError.js",False)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.DecodeURIComponent("built-ins/decodeURIComponent/throw-URIError.js",True)

---
## Diagnosis (2026-02-03)

**Summary:** `decodeURIComponent` was incorrectly preserving percent-encoded sequences for unescaped characters and accepting invalid UTF-8 (overlong, surrogate, out-of-range). It now decodes all bytes and rejects malformed UTF-8 sequences.

**Fixes:**
- Decode all `%XX` sequences (no reserved preservation) with strict UTF-8 validation.
- Reject overlong encodings, surrogate code points, invalid continuation bytes, and out-of-range code points.

**Tests:**
`dotnet test tests/Asynkron.JsEngine.Tests.Test262 -c Release --settings tests/Asynkron.JsEngine.Tests.Test262/BuiltInsTests.runsettings --filter "Name=EncodeURI|Name=EncodeURIComponent|Name=DecodeURI|Name=DecodeURIComponent"`

** DONE **
