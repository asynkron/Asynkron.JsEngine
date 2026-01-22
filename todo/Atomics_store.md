# Atomics_store

FQN:
`Asynkron.JsEngine.Tests.Test262.BuiltInsTests.Atomics_store`

Full test name:

- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.Atomics_store("built-ins/Atomics/store/good-views.js",False)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.Atomics_store("built-ins/Atomics/store/good-views.js",True)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.Atomics_store("built-ins/Atomics/store/non-shared-int-views-throws.js",False)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.Atomics_store("built-ins/Atomics/store/non-shared-int-views-throws.js",True)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.Atomics_store("built-ins/Atomics/store/validate-arraytype-before-index-coercion.js",False)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.Atomics_store("built-ins/Atomics/store/validate-arraytype-before-index-coercion.js",True)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.Atomics_store("built-ins/Atomics/store/validate-arraytype-before-value-coercion.js",False)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.Atomics_store("built-ins/Atomics/store/validate-arraytype-before-value-coercion.js",True)

---
## Diagnosis (2026-01-22)

**Summary:** Atomics.store diverges from ValidateSharedIntegerTypedArray (non-shared/Uint8ClampedArray allowed) and returns the stored element instead of ToInteger(value), leading to wrong exceptions and return values.

**Error Pattern:**
```
Asynkron.JsEngine.ThrowSignal: Unhandled JavaScript throw:
Asynkron.JsEngine.ThrowSignal: Unhandled JavaScript throw: Expected a TypeError but got a Test262Error
```

**Analysis:**
- `good-views.js` fails because `Atomics.store` returns `typedArray.GetValueForIndex` after writing; for unsigned arrays the returned value is the wrapped element (e.g., -5 -> 251) instead of `ToInteger(val)` expected by the test.
- `non-shared-int-views-throws.js` expects a TypeError for ArrayBuffer-backed integer views; `RequireAtomicTypedArray` explicitly allows non-shared buffers, so no TypeError is raised.
- `validate-arraytype-before-index-coercion.js` and `validate-arraytype-before-value-coercion.js` expect typed array validation before coercion. `RequireAtomicTypedArray` only rejects float arrays, so invalid types like Uint8ClampedArray slip through, index/value coercion runs, and the Test262Error from `valueOf` wins.

**Fix Direction:**
- Implement spec-aligned `ValidateSharedIntegerTypedArray` for `Atomics.store`: require `SharedArrayBuffer`, reject `Uint8ClampedArray` (and float arrays), and keep typed array validation ahead of `ToIndex`/`ToNumber`/`ToBigInt`.
- Return the converted `ToInteger`/`ToBigInt` value from `Atomics.store` instead of reading back the stored element.
