# Array_prototype_keys

FQN:
`Asynkron.JsEngine.Tests.Test262.BuiltInsTests.Array_prototype_keys`

Full test name:

- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.Array_prototype_keys("built-ins/Array/prototype/keys/resizable-buffer-grow-mid-iteration.js",False)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.Array_prototype_keys("built-ins/Array/prototype/keys/resizable-buffer-grow-mid-iteration.js",True)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.Array_prototype_keys("built-ins/Array/prototype/keys/resizable-buffer-shrink-mid-iteration.js",True)

---
## Diagnosis (2026-01-22)

**Summary:** Tests pass; no failing Array.prototype.keys cases reproduced.

**Error Pattern:**
```
N/A (no failures; test run successful)
```

**Analysis:**
The filtered Test262 run for `Name=Array_prototype_keys` completed with 24/24 passing.
The previously listed resizable-buffer tests now pass, so the failure list appears stale
or was already fixed in the current branch. No runtime exception or spec deviation was observed.

**Fix Direction:**
None identified. If failures reappear, re-run with the same filter and capture
the failing test output to narrow the spec gap.

** DONE **
