# Atomics_store_bigint

FQN:
`Asynkron.JsEngine.Tests.Test262.BuiltInsTests.Atomics_store_bigint`

Full test name:

- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.Atomics_store_bigint("built-ins/Atomics/store/bigint/good-views.js",False)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.Atomics_store_bigint("built-ins/Atomics/store/bigint/good-views.js",True)

---
## Diagnosis (2026-01-22)

**Summary:** `Atomics.store` returns the stored BigUint64Array value (wrapped) instead of the input `BigInt(val)`, so `good-views.js` fails when negative BigInt values are used.

**Error Pattern:**
```
Failed Atomics_store_bigint("built-ins/Atomics/store/bigint/good-views.js",True)
Asynkron.JsEngine.ThrowSignal: Unhandled JavaScript throw:
Failed Atomics_store_bigint("built-ins/Atomics/store/bigint/good-views.js",False)
Asynkron.JsEngine.ThrowSignal: Unhandled JavaScript throw:
```

**Analysis:**
`good-views.js` runs `Atomics.store(view, 3, val)` for BigInt64Array and BigUint64Array views with a non-zero byteOffset on a `SharedArrayBuffer`. The test asserts the return value equals `BigInt(val)`. In `AtomicsPrototype.Store`, the BigInt path writes the BigInt then returns `typedArray.GetValueForIndex(index)`, which is the stored element after BigInt64/BigUint64 coercion. For `BigUint64Array` and negative inputs (e.g., `-5n`), the stored value is modulo 2^64, so the return value becomes `2^64 - 5n` instead of `-5n`, triggering a Test262Error. Both strict and non-strict runs fail on the same file, consistent with the constructor loop in `testWithBigIntTypedArrayConstructors`.

**Fix Direction:**
In `AtomicsPrototype.Store`, return the coerced BigInt input (the `ToBigInt` result) instead of re-reading the stored element for BigInt typed arrays. That preserves the spec-required return value for BigUint64Array when wrapping occurs, while still storing the wrapped value in the buffer.

** DONE **
