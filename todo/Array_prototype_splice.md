# Array_prototype_splice

FQN:
`Asynkron.JsEngine.Tests.Test262.BuiltInsTests.Array_prototype_splice`

Full test name:

- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.Array_prototype_splice("built-ins/Array/prototype/splice/create-species-length-exceeding-integer-limit.js",False)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.Array_prototype_splice("built-ins/Array/prototype/splice/create-species-length-exceeding-integer-limit.js",True)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.Array_prototype_splice("built-ins/Array/prototype/splice/create-species-undef-invalid-len.js",False)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.Array_prototype_splice("built-ins/Array/prototype/splice/create-species-undef-invalid-len.js",True)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.Array_prototype_splice("built-ins/Array/prototype/splice/property-traps-order-with-species.js",False)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.Array_prototype_splice("built-ins/Array/prototype/splice/property-traps-order-with-species.js",True)

---
## Diagnosis (2026-01-22)

**Summary:** Splice's reentrancy guard touches observable properties on proxy receivers, and array length setters bypass receiver-aware proxy semantics, breaking trap order and "no side effects before RangeError" guarantees.

**Error Pattern:**
```
Asynkron.JsEngine.ThrowSignal: Unhandled JavaScript throw:
Asynkron.JsEngine.ThrowSignal: Unhandled JavaScript throw: 'Error': 'Actual [defineProperty, defineProperty, set, getOwnPropertyDescriptor, defineProperty] and expected [defineProperty, defineProperty, set] should have the same contents. ...
```

**Analysis:**
- `create-species-undef-invalid-len` expects the RangeError from `ArraySpeciesCreate` to occur before any property modification (`callCount === 0`). The current reentrancy guard writes `__inSplice__` onto the receiver, triggering proxy `set` and making `callCount` non-zero.
- `create-species-length-exceeding-integer-limit` expects proxy traps to begin with `source.[[Get]]:length` and preserve the exact trap order. The guard reads/writes `__inSplice__` on the proxy before length access, introducing extra observable proxy operations and breaking the expected sequence (and thus downstream assertions).
- `property-traps-order-with-species` expects `Set(A, "length", ...)` to consult `getOwnPropertyDescriptor`/`defineProperty` when `A` is a proxy. `SetArrayLikeLength` funnels to `JsArray.SetProperty("length")`, which ignores the receiver and calls `SetLength` directly, so proxies only observe the initial `set` trap lookup and miss the later trap lookups.

**Fix Direction:**
- Move the splice reentrancy guard to an internal, non-observable slot/state (realm/context flag or private storage) to avoid `TryGetProperty`/`SetProperty` on user objects.
- Route `Set(A, "length", ...)` through a receiver-aware path (`SetPropertyOrThrow` with receiver) or update `JsArray.SetProperty` to honor proxy receivers for `"length"` so the required `getOwnPropertyDescriptor`/`defineProperty` trap lookups occur.

** DONE **
