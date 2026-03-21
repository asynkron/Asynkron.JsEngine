# Array_prototype_values

FQN:
`Asynkron.JsEngine.Tests.Test262.BuiltInsTests.Array_prototype_values`

Full test name:

- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.Array_prototype_values("built-ins/Array/prototype/values/resizable-buffer.js",False)

---
## Diagnosis (2026-01-22)

**Summary:** No failures; the Array_prototype_values suite passes, so the todo entry looks stale.

**Error Pattern:**
```
No errors; dotnet test reported 24/24 passed for Name=Array_prototype_values.
```

**Analysis:**
The only listed failing test (resizable-buffer.js) passed in both strict and non-strict variants. This suggests Array.prototype.values iteration (including resizable array buffer semantics) is already behaving per spec, or the failing list is out of date for this repo state.

**Fix Direction:**
Refresh the failing-test inventory and remove this entry if it stays green; if failures reappear, capture the specific Test262 assertion/output for targeted fixes.

** DONE **
