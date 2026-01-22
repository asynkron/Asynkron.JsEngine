# Atomics_compareExchange

FQN:
`Asynkron.JsEngine.Tests.Test262.BuiltInsTests.Atomics_compareExchange`

Full test name:

- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.Atomics_compareExchange("built-ins/Atomics/compareExchange/good-views.js",False)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.Atomics_compareExchange("built-ins/Atomics/compareExchange/good-views.js",True)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.Atomics_compareExchange("built-ins/Atomics/compareExchange/non-shared-int-views-throws.js",False)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.Atomics_compareExchange("built-ins/Atomics/compareExchange/non-shared-int-views-throws.js",True)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.Atomics_compareExchange("built-ins/Atomics/compareExchange/validate-arraytype-before-expectedValue-coercion.js",False)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.Atomics_compareExchange("built-ins/Atomics/compareExchange/validate-arraytype-before-expectedValue-coercion.js",True)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.Atomics_compareExchange("built-ins/Atomics/compareExchange/validate-arraytype-before-index-coercion.js",False)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.Atomics_compareExchange("built-ins/Atomics/compareExchange/validate-arraytype-before-index-coercion.js",True)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.Atomics_compareExchange("built-ins/Atomics/compareExchange/validate-arraytype-before-replacementValue-coercion.js",False)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.Atomics_compareExchange("built-ins/Atomics/compareExchange/validate-arraytype-before-replacementValue-coercion.js",True)

---
## Diagnosis (2026-01-22)

**Summary:** `Atomics.compareExchange` does not implement `ValidateSharedIntegerTypedArray` correctly (accepts non-shared/non-atomic typed arrays), so invalid views are not rejected before coercing arguments and even valid shared views can throw unexpectedly.

**Error Pattern:**
```
Asynkron.JsEngine.ThrowSignal: Unhandled JavaScript throw: Expected a TypeError but got a Test262Error
Asynkron.JsEngine.ThrowSignal: Unhandled JavaScript throw:
```

**Analysis:**
The failures cluster around array-type validation order and shared-buffer requirements. In `src/Asynkron.JsEngine/StdLib/Atomics/AtomicsPrototype.cs`, `CompareExchange` calls `RequireAtomicTypedArray` which currently:
- Allows ArrayBuffer-backed integer typed arrays ("Atomics operations work on both SharedArrayBuffer and ArrayBuffer") instead of requiring shared buffers for compareExchange.
- Only rejects float typed arrays, but does not exclude other non-atomic-friendly typed arrays (e.g., Uint8ClampedArray).

Because invalid views are accepted, the engine proceeds to coerce `index`, `expectedValue`, or `replacementValue`. The Test262 tests intentionally attach `valueOf()` throws to these arguments; that coercion happens too early and results in `Test262Error` instead of the required TypeError. This explains the "Expected a TypeError but got a Test262Error" pattern in the validate-arraytype-before-* tests. The non-shared-int-views tests also fail because ArrayBuffer-backed integer views are not rejected. The remaining `good-views.js` failures show an unexpected throw on valid SharedArrayBuffer integer views, consistent with the same validation mismatch (the engine is not adhering to the spec’s shared-integer typed array rules for compareExchange).

**Fix Direction:**
Update `RequireAtomicTypedArray` (and any callers in `AtomicsPrototype`) to implement `ValidateSharedIntegerTypedArray`:
- Require `typedArray.Buffer.IsShared` for `compareExchange` and other non-wait Atomics ops.
- Reject non-atomic-friendly types (e.g., Uint8ClampedArray, Float32/Float64).
- Ensure this validation happens before `RequireAtomicIndex` and before coercing `expectedValue`/`replacementValue`.
- Use `CreateTypeError`/`ThrowTypeError` with a clear message that matches spec behavior.

** DONE **
