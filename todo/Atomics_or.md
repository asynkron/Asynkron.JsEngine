# Atomics_or

FQN:
`Asynkron.JsEngine.Tests.Test262.BuiltInsTests.Atomics_or`

Full test name:

- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.Atomics_or("built-ins/Atomics/or/good-views.js",False)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.Atomics_or("built-ins/Atomics/or/good-views.js",True)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.Atomics_or("built-ins/Atomics/or/non-shared-int-views-throws.js",False)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.Atomics_or("built-ins/Atomics/or/non-shared-int-views-throws.js",True)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.Atomics_or("built-ins/Atomics/or/validate-arraytype-before-index-coercion.js",False)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.Atomics_or("built-ins/Atomics/or/validate-arraytype-before-index-coercion.js",True)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.Atomics_or("built-ins/Atomics/or/validate-arraytype-before-value-coercion.js",False)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.Atomics_or("built-ins/Atomics/or/validate-arraytype-before-value-coercion.js",True)

---
## Diagnosis (2026-01-22)

**Summary:** `Atomics.or` does not implement ValidateSharedIntegerTypedArray correctly (allows non-shared/invalid typed arrays) and uses non-JS numeric conversions for bitwise operands, causing wrong coercion order and incorrect results.

**Error Pattern:**
```
built-ins/Atomics/or/good-views.js
Asynkron.JsEngine.ThrowSignal: Unhandled JavaScript throw:

built-ins/Atomics/or/non-shared-int-views-throws.js
Asynkron.JsEngine.ThrowSignal: Unhandled JavaScript throw:

built-ins/Atomics/or/validate-arraytype-before-index-coercion.js
Asynkron.JsEngine.ThrowSignal: Unhandled JavaScript throw: Expected a TypeError but got a Test262Error

built-ins/Atomics/or/validate-arraytype-before-value-coercion.js
Asynkron.JsEngine.ThrowSignal: Unhandled JavaScript throw: Expected a TypeError but got a Test262Error
```

**Analysis:**
`RequireAtomicTypedArray` only rejects Float32/Float64 and explicitly allows ArrayBuffer-backed views, so `Uint8ClampedArray` and non-shared integer typed arrays are treated as valid. That means `RequireAtomicIndex`/`ToNumber` runs before the required ValidateSharedIntegerTypedArray checks, so the `index`/`value` coercion throws `Test262Error` instead of the expected `TypeError`. In `AtomicBitwiseOperation`, operands are cast with `(int)`/`(double)` instead of JS `ToInt32`/`ToUInt32` semantics, which breaks Uint32Array cases using values like `0xF0F0F0F0`/`0xF7F7F7F7`, leading to assertion failures in `good-views.js`.

**Fix Direction:**
Implement ValidateSharedIntegerTypedArray for Atomics read/modify/write ops: require `typedArray.Buffer.IsShared`, reject `Uint8ClampedArray` and non-integer typed arrays, and perform these checks before any index/value coercion. For `Atomics.or` (and other bitwise ops), use JS numeric conversions (`JsNumericConversions.ToInt32`/`ToUInt32`) based on the typed array kind and apply the operation on the correctly coerced 32-bit value before storing back to the typed array element type.
