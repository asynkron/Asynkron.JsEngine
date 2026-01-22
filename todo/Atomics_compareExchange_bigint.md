# Atomics_compareExchange_bigint

FQN:
`Asynkron.JsEngine.Tests.Test262.BuiltInsTests.Atomics_compareExchange_bigint`

Full test name:

- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.Atomics_compareExchange_bigint("built-ins/Atomics/compareExchange/bigint/good-views.js",False)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.Atomics_compareExchange_bigint("built-ins/Atomics/compareExchange/bigint/good-views.js",True)

---
## Diagnosis (2026-01-22)

**Summary:** Atomics.compareExchange compares BigUint64Array expected values without unsigned wrapping, so negative expected BigInts never match stored unsigned values.

**Error Pattern:**
```
Asynkron.JsEngine.ThrowSignal: Unhandled JavaScript throw:
built-ins/Atomics/compareExchange/bigint/good-views.js
```

**Analysis:**
`good-views.js` exercises BigInt64Array and BigUint64Array. For BigUint64Array, values like `-5n` are stored as `ToBigUint64(-5n)` (wrap to 2^64-5n). The current `Atomics.compareExchange` path calls `ToBigInt` for the expected value and compares it directly against the stored BigInt. This means `expected = -5n` never matches the stored `2^64-5n`, so the exchange does not occur and later `assert.sameValue(view[3], 0n)` fails. The same file passes for BigInt64Array, so the failure is specific to unsigned BigInt element normalization during comparison.

**Fix Direction:**
In `AtomicsPrototype.CompareExchange`, normalize `expected` to the element type before `SameValue` for BigInt typed arrays. For `BigUint64Array`, apply `ToBigUint64` (or the array's element conversion) to the expected BigInt before comparing; for `BigInt64Array`, keep signed conversion. Alternatively, convert both expected and oldValue to the array's canonical element representation before comparison.
