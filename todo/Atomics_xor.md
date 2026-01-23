# Atomics_xor

FQN:
`Asynkron.JsEngine.Tests.Test262.BuiltInsTests.Atomics_xor`

Full test name:

- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.Atomics_xor("built-ins/Atomics/xor/good-views.js",False)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.Atomics_xor("built-ins/Atomics/xor/good-views.js",True)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.Atomics_xor("built-ins/Atomics/xor/non-shared-int-views-throws.js",False)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.Atomics_xor("built-ins/Atomics/xor/non-shared-int-views-throws.js",True)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.Atomics_xor("built-ins/Atomics/xor/validate-arraytype-before-index-coercion.js",False)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.Atomics_xor("built-ins/Atomics/xor/validate-arraytype-before-index-coercion.js",True)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.Atomics_xor("built-ins/Atomics/xor/validate-arraytype-before-value-coercion.js",False)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.Atomics_xor("built-ins/Atomics/xor/validate-arraytype-before-value-coercion.js",True)

---
## Diagnosis (2026-01-22)

**Summary:** Atomics.xor validates the typed array too loosely and uses unsafe int casts, so invalid arrays don’t throw TypeError before coercion and some xor results are incorrect.

**Error Pattern:**
```
Asynkron.JsEngine.ThrowSignal: Unhandled JavaScript throw:
Asynkron.JsEngine.ThrowSignal: Unhandled JavaScript throw: Expected a TypeError but got a Test262Error
throw new Test262Error("index coerced");
throw new Test262Error("value coerced");
```

**Analysis:**
`AtomicsPrototype.RequireAtomicTypedArray` allows non-SharedArrayBuffer views and only rejects Float32/Float64 arrays, so `Uint8ClampedArray` (and other non-atomics-friendly arrays) slip through. That causes `RequireAtomicIndex` / `JsOps.ToNumber` to coerce `index` and `value` before the array type is validated, which triggers the Test262Error (“index/value coerced”) instead of the required TypeError. The `good-views.js` failures line up with `AtomicBitwiseOperation` using `(int)` casts for the old value and mask; for Uint32 values like `0xF0F0F0F0`, the cast overflows and yields the wrong xor result, so `assert.sameValue` throws.

**Fix Direction:**
Implement a `ValidateSharedIntegerTypedArray` equivalent that rejects non-shared buffers and non-integer typed arrays (including Uint8ClampedArray) before any index/value coercion. In `AtomicBitwiseOperation`, replace the `(int)` casts with JS-correct `ToInt32`/`ToUint32` (or element-type-aware conversion) so xor on Uint32Array handles values above `int.MaxValue` correctly.

** DONE **
