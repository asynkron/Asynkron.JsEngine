# BuiltIns_RegExp

FQN:
`Asynkron.JsEngine.Tests.Test262.AnnexBTests.BuiltIns_RegExp`

Full test name:

- Asynkron.JsEngine.Tests.Test262.AnnexBTests.BuiltIns_RegExp("annexB/built-ins/RegExp/RegExp-leading-escape-BMP.js",False)
- Asynkron.JsEngine.Tests.Test262.AnnexBTests.BuiltIns_RegExp("annexB/built-ins/RegExp/RegExp-leading-escape-BMP.js",True)
- Asynkron.JsEngine.Tests.Test262.AnnexBTests.BuiltIns_RegExp("annexB/built-ins/RegExp/RegExp-trailing-escape-BMP.js",False)
- Asynkron.JsEngine.Tests.Test262.AnnexBTests.BuiltIns_RegExp("annexB/built-ins/RegExp/RegExp-trailing-escape-BMP.js",True)

---
## Diagnosis (2026-01-22)

**Summary:** No failing BuiltIns_RegExp tests observed; the current run passes all cases.

**Error Pattern:**
```
None. Test Run Successful (20/20 passed).
```

**Analysis:**
The targeted BuiltIns_RegExp tests passed in Release mode with the specified filter, including the BMP leading/trailing escape cases. This suggests the failure list is stale or the previously failing behavior has already been fixed in the current codebase. No ECMAScript behavior regressions were reproducible in this run.

**Fix Direction:**
No code change indicated from this run. If failures appear in another environment, capture the exact failure output and re-run the same filter to isolate differences (e.g., runtime version, generated test inputs, or cached artifacts).

** DONE **
