# Boolean_prototype

FQN:
`Asynkron.JsEngine.Tests.Test262.BuiltInsTests.Boolean_prototype`

Full test name:

- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.Boolean_prototype("built-ins/Boolean/prototype/S15.6.3.1_A2.js",False)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.Boolean_prototype("built-ins/Boolean/prototype/S15.6.3.1_A2.js",True)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.Boolean_prototype("built-ins/Boolean/prototype/S15.6.3.1_A3.js",True)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.Boolean_prototype("built-ins/Boolean/prototype/S15.6.3.1_A4.js",False)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.Boolean_prototype("built-ins/Boolean/prototype/S15.6.3.1_A4.js",True)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.Boolean_prototype("built-ins/Boolean/prototype/S15.6.4_A2.js",False)

---
## Diagnosis (2026-01-22)

**Summary:** Tests currently pass; failing list appears stale or already fixed.

**Error Pattern:**
```
No failures in current run; all 9 Boolean_prototype tests passed.
```

**Analysis:**
The targeted Test262 Boolean.prototype cases (S15.6.3.1_A1/A2/A3/A4 and S15.6.4_A2, both strict and non-strict variants) all pass. The earlier "failing" list likely reflects an older engine state or different baseline; no runtime exception or spec mismatch appeared in this run.

**Fix Direction:**
Remove Boolean_prototype from the failing list or re-run against the original failing revision to reproduce.
