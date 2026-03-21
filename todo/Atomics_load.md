# Atomics_load

FQN:
`Asynkron.JsEngine.Tests.Test262.BuiltInsTests.Atomics_load`

Full test name:

- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.Atomics_load("built-ins/Atomics/load/non-shared-int-views-throws.js",False)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.Atomics_load("built-ins/Atomics/load/non-shared-int-views-throws.js",True)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.Atomics_load("built-ins/Atomics/load/validate-arraytype-before-index-coercion.js",False)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.Atomics_load("built-ins/Atomics/load/validate-arraytype-before-index-coercion.js",True)

---
## Diagnosis (2026-01-22)

**Summary:** Atomics.load validates index coercion before fully rejecting non-shared/unsupported typed arrays, so it misses required TypeError checks (SharedArrayBuffer + allowed integer types).

**Error Pattern:**
```
Asynkron.JsEngine.ThrowSignal: Unhandled JavaScript throw:
...
Asynkron.JsEngine.ThrowSignal: Unhandled JavaScript throw: Expected a TypeError but got a Test262Error
Test262Error("index coerced")
```

**Analysis:**
`Atomics.load` should run `ValidateIntegerTypedArray` (integer typed array only, no Uint8Clamped/float, and backed by SharedArrayBuffer) before `ToIndex`. Current `RequireAtomicTypedArray` only rejects Float32/Float64 and explicitly allows ArrayBuffer-backed views, so non-shared integer views do not throw TypeError and `Uint8ClampedArray` slips through. That lets `ToIndex` coerce `index` and triggers the `Test262Error("index coerced")` path, violating the spec-mandated validation order.

**Fix Direction:**
Tighten `RequireAtomicTypedArray` to reject non-atomics-friendly typed arrays (including `Uint8ClampedArray`) and require `Buffer.IsShared` for Atomics.load (and other atomic read/modify ops). Keep this validation before `RequireAtomicIndex` so `ToIndex` is not invoked on invalid typed arrays.

** DONE **
