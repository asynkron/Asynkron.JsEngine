# Array_prototype_entries

FQN:
`Asynkron.JsEngine.Tests.Test262.BuiltInsTests.Array_prototype_entries`

Full test name:

- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.Array_prototype_entries("built-ins/Array/prototype/entries/resizable-buffer-grow-mid-iteration.js",False)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.Array_prototype_entries("built-ins/Array/prototype/entries/resizable-buffer-grow-mid-iteration.js",True)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.Array_prototype_entries("built-ins/Array/prototype/entries/resizable-buffer.js",False)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.Array_prototype_entries("built-ins/Array/prototype/entries/resizable-buffer.js",True)

---
## Diagnosis (2026-01-22)

**Summary:** No failing tests; all `Array_prototype_entries` cases pass in this run.

**Error Pattern:**
```
None. Test run successful (24/24 passed).
```

**Analysis:**
The specified `Name=Array_prototype_entries` filter executed 24 Test262 cases, including the resizable-buffer variants listed in this todo. Every case passed with no runtime exceptions or assertion failures. This indicates the engine currently handles `Array.prototype.entries` behavior for resizable buffers and iteration correctly in this environment. The failing list appears stale or was fixed by prior changes.

**Fix Direction:**
Refresh the failing test inventory. If failures reappear in another environment, capture that specific output and compare harness/test262 versions.

** DONE **
