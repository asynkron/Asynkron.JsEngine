# Atomics_add

FQN:
`Asynkron.JsEngine.Tests.Test262.BuiltInsTests.Atomics_add`

Full test name:

- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.Atomics_add("built-ins/Atomics/add/non-shared-int-views-throws.js",False)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.Atomics_add("built-ins/Atomics/add/non-shared-int-views-throws.js",True)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.Atomics_add("built-ins/Atomics/add/validate-arraytype-before-index-coercion.js",False)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.Atomics_add("built-ins/Atomics/add/validate-arraytype-before-index-coercion.js",True)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.Atomics_add("built-ins/Atomics/add/validate-arraytype-before-value-coercion.js",False)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.Atomics_add("built-ins/Atomics/add/validate-arraytype-before-value-coercion.js",True)

---
## Diagnosis (2026-01-22)

**Summary:** Atomics.add validates/coerces index/value before validating shared integer typed arrays, and it does not reject non-shared buffers early.

**Error Pattern:**
```
Asynkron.JsEngine.ThrowSignal: Unhandled JavaScript throw: Expected a TypeError but got a Test262Error
Asynkron.JsEngine.ThrowSignal: Unhandled JavaScript throw:
```

**Analysis:**
`validate-arraytype-before-index-coercion.js` and `validate-arraytype-before-value-coercion.js` fail because the engine coerces `index`/`value` (triggering `valueOf()` and throwing `Test262Error`) before running `ValidateSharedIntegerTypedArray`. The spec requires type/SharedArrayBuffer validation first, so these tests should throw `TypeError` before any coercion. `non-shared-int-views-throws.js` fails because Atomics.add proceeds on integer typed arrays backed by a non-shared `ArrayBuffer` instead of throwing a `TypeError`.

**Fix Direction:**
Ensure `Atomics.add` (and the common Atomics read-modify-write path) calls `ValidateSharedIntegerTypedArray` before any `index`/`value` coercion, and explicitly rejects non-shared buffers for integer typed arrays.
