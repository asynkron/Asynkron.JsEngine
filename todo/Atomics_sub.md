# Atomics_sub

FQN:
`Asynkron.JsEngine.Tests.Test262.BuiltInsTests.Atomics_sub`

Full test name:

- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.Atomics_sub("built-ins/Atomics/sub/non-shared-int-views-throws.js",False)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.Atomics_sub("built-ins/Atomics/sub/non-shared-int-views-throws.js",True)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.Atomics_sub("built-ins/Atomics/sub/validate-arraytype-before-index-coercion.js",False)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.Atomics_sub("built-ins/Atomics/sub/validate-arraytype-before-index-coercion.js",True)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.Atomics_sub("built-ins/Atomics/sub/validate-arraytype-before-value-coercion.js",False)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.Atomics_sub("built-ins/Atomics/sub/validate-arraytype-before-value-coercion.js",True)

---
## Diagnosis (2026-01-22)

**Summary:** `Atomics.sub` allows non-shared and non-Atomics-friendly typed arrays, so invalid inputs aren't rejected before index/value coercion.

**Error Pattern:**
```
Asynkron.JsEngine.ThrowSignal: Unhandled JavaScript throw:
Asynkron.JsEngine.ThrowSignal: Unhandled JavaScript throw: Expected a TypeError but got a Test262Error
```

**Analysis:**
The failing tests exercise `ValidateSharedIntegerTypedArray` requirements. In `AtomicsPrototype.RequireAtomicTypedArray`, the implementation explicitly allows ArrayBuffer-backed typed arrays and only rejects floating-point arrays, which means:
- Non-shared integer views do not throw a TypeError, so `assert.throws` fails.
- Non-Atomics-friendly types (e.g., `Uint8ClampedArray`) pass the type check, and `Atomics.sub` coerces `index`/`value` (triggering the `Test262Error` from the test) instead of throwing a TypeError first.

**Fix Direction:**
Align `RequireAtomicTypedArray` with spec `ValidateSharedIntegerTypedArray` for `Atomics.sub`: require `SharedArrayBuffer` for arithmetic operations and reject non-Atomics-friendly typed arrays (including `Uint8ClampedArray`, `Float32Array`, `Float64Array`) before any index/value coercion. This keeps type validation ahead of `ToIndex`/`ToNumber` and prevents the Test262Error cases.

** DONE **
