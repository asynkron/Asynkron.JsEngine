# Array_prototype_forEach

FQN:
`Asynkron.JsEngine.Tests.Test262.BuiltInsTests.Array_prototype_forEach`

Full test name:

- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.Array_prototype_forEach("built-ins/Array/prototype/forEach/15.4.4.18-7-c-ii-1.js",False)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.Array_prototype_forEach("built-ins/Array/prototype/forEach/15.4.4.18-7-c-ii-1.js",True)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.Array_prototype_forEach("built-ins/Array/prototype/forEach/15.4.4.18-7-c-ii-12.js",False)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.Array_prototype_forEach("built-ins/Array/prototype/forEach/15.4.4.18-7-c-ii-13.js",False)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.Array_prototype_forEach("built-ins/Array/prototype/forEach/15.4.4.18-7-c-ii-13.js",True)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.Array_prototype_forEach("built-ins/Array/prototype/forEach/15.4.4.18-7-c-ii-16.js",False)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.Array_prototype_forEach("built-ins/Array/prototype/forEach/15.4.4.18-7-c-ii-16.js",True)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.Array_prototype_forEach("built-ins/Array/prototype/forEach/15.4.4.18-7-c-ii-17.js",False)

---
## Diagnosis (2026-01-22)

**Summary:** Array length reduction throws in sloppy mode when a non-configurable element blocks deletion, so `forEach` aborts instead of visiting index 2.

**Error Pattern:**
```
Failed Array_prototype_forEach("built-ins/Array/prototype/forEach/15.4.4.18-7-b-16.js",False)
Asynkron.JsEngine.ThrowSignal: Unhandled JavaScript throw: 'TypeError': 'Invalid array length'
```

**Analysis:**
`15.4.4.18-7-b-16.js` defines a non-configurable accessor at index 2, then a getter at index 1 that does `arr.length = 2`. Per spec, reducing length should attempt deletions; if a non-configurable property blocks deletion, the length write should fail silently in non-strict mode (leaving the property and length unchanged or set back to `failedIndex + 1`). `Array.prototype.forEach` takes `len` up front, so it should still visit index 2. The engine throws `TypeError: Invalid array length` during the length assignment, which is incorrect for `noStrict`.

**Fix Direction:**
Adjust the Array length setter/`ArraySetLength` implementation so failed deletions of non-configurable elements return `false` without throwing in sloppy mode, restore length to `failedIndex + 1`, and allow `forEach` to continue using the original `len`.
