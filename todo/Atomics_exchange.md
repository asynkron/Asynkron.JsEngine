# Atomics_exchange

FQN:
`Asynkron.JsEngine.Tests.Test262.BuiltInsTests.Atomics_exchange`

Full test name:

- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.Atomics_exchange("built-ins/Atomics/exchange/good-views.js",True)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.Atomics_exchange("built-ins/Atomics/exchange/non-shared-int-views-throws.js",False)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.Atomics_exchange("built-ins/Atomics/exchange/non-shared-int-views-throws.js",True)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.Atomics_exchange("built-ins/Atomics/exchange/non-views.js",True)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.Atomics_exchange("built-ins/Atomics/exchange/nonshared-int-views.js",False)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.Atomics_exchange("built-ins/Atomics/exchange/nonshared-int-views.js",True)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.Atomics_exchange("built-ins/Atomics/exchange/not-a-constructor.js",False)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.Atomics_exchange("built-ins/Atomics/exchange/not-a-constructor.js",True)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.Atomics_exchange("built-ins/Atomics/exchange/validate-arraytype-before-index-coercion.js",False)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.Atomics_exchange("built-ins/Atomics/exchange/validate-arraytype-before-index-coercion.js",True)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.Atomics_exchange("built-ins/Atomics/exchange/validate-arraytype-before-value-coercion.js",False)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.Atomics_exchange("built-ins/Atomics/exchange/validate-arraytype-before-value-coercion.js",True)

---
## Diagnosis (2026-01-22)

**Summary:** `Atomics.exchange` validates typed arrays too loosely (allows non-shared buffers and non-Atomics-friendly element types), so TypeError checks happen late or not at all, letting index/value coercion run first.

**Error Pattern:**
```
built-ins/Atomics/exchange/non-shared-int-views-throws.js
Asynkron.JsEngine.ThrowSignal: Unhandled JavaScript throw:

built-ins/Atomics/exchange/nonshared-int-views.js
Asynkron.JsEngine.ThrowSignal: Unhandled JavaScript throw:

built-ins/Atomics/exchange/validate-arraytype-before-index-coercion.js
Expected a TypeError but got a Test262Error

built-ins/Atomics/exchange/validate-arraytype-before-value-coercion.js
Expected a TypeError but got a Test262Error
```

**Analysis:**
`RequireAtomicTypedArray` only rejects float typed arrays and explicitly allows ArrayBuffer-backed views. That means `Atomics.exchange` accepts non-shared buffers (violating `ValidateSharedIntegerTypedArray`) and also lets through non-Atomics-friendly element types like `Uint8ClampedArray`. For those invalid types, the call proceeds to `RequireAtomicIndex`/`JsOps.ToNumber` and triggers user-defined `valueOf()` during index/value coercion, causing `Test262Error("index/value coerced")` instead of the required early TypeError. The failures are consistent with missing shared-buffer enforcement and incomplete typed-array type validation before coercion.

**Fix Direction:**
Implement spec-level `ValidateSharedIntegerTypedArray` in `RequireAtomicTypedArray`: reject non-shared buffers for all Atomics ops (not just wait/notify) and restrict element types to Int8/Uint8/Int16/Uint16/Int32/Uint32 (plus BigInt64/BigUint64 when allowed). Ensure this validation runs before any index/value coercion so `valueOf()` is never called for invalid typed arrays.
