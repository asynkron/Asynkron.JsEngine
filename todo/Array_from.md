# Array_from

FQN:
`Asynkron.JsEngine.Tests.Test262.BuiltInsTests.Array_from`

Full test name:

- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.Array_from("built-ins/Array/from/iter-adv-err.js",False)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.Array_from("built-ins/Array/from/iter-adv-err.js",True)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.Array_from("built-ins/Array/from/iter-cstm-ctor-err.js",False)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.Array_from("built-ins/Array/from/iter-cstm-ctor-err.js",True)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.Array_from("built-ins/Array/from/iter-cstm-ctor.js",True)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.Array_from("built-ins/Array/from/iter-get-iter-err.js",False)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.Array_from("built-ins/Array/from/iter-get-iter-err.js",True)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.Array_from("built-ins/Array/from/iter-get-iter-val-err.js",False)

---
## Diagnosis (2026-01-22)

**Summary:** No failing Array.from Test262 cases reproduced; the failing list appears stale or already fixed.

**Error Pattern:**
```
No errors/exceptions. Test Run Successful. Total tests: 90, Passed: 90.
```

**Analysis:**
Running `dotnet test tests/Asynkron.JsEngine.Tests.Test262 -c Release --filter "Name=Array_from" -v n` executed 90 Array_from cases and all passed. This indicates the suspected iterator/constructor error-handling failures are not present in the current codebase/test262 snapshot. If failures were seen previously, they may have already been fixed or tied to a different test262 commit/configuration.

**Fix Direction:**
No code changes indicated; refresh the failing list or confirm the exact test262 commit/environment that previously failed.

** DONE **
