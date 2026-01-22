# BigInt_asUintN

FQN:
`Asynkron.JsEngine.Tests.Test262.BuiltInsTests.BigInt_asUintN`

Full test name:

- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.BigInt_asUintN("built-ins/BigInt/asUintN/bigint-tobigint-errors.js",False)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.BigInt_asUintN("built-ins/BigInt/asUintN/bigint-tobigint-errors.js",True)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.BigInt_asUintN("built-ins/BigInt/asUintN/bits-toindex-toprimitive.js",False)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.BigInt_asUintN("built-ins/BigInt/asUintN/bits-toindex-toprimitive.js",True)

---
## Diagnosis (2026-01-22)

**Summary:** `ToBigInt` and `ToIndex` coercion paths do not match spec (Numbers are accepted for `ToBigInt`, and non-callable `@@toPrimitive` is not rejected), so the Test262 `assert.throws` checks fail and bubble a `Test262Error`.

**Error Pattern:**
```
Failed BigInt_asUintN("built-ins/BigInt/asUintN/bigint-tobigint-errors.js",True)
Asynkron.JsEngine.ThrowSignal: Unhandled JavaScript throw:

Failed BigInt_asUintN("built-ins/BigInt/asUintN/bits-toindex-toprimitive.js",False)
Asynkron.JsEngine.ThrowSignal: Unhandled JavaScript throw:
```

**Analysis:**
`BigInt.asUintN` uses `ToIndex` for `bits` and `ToBigInt` for the `bigint` argument. The failing `bigint-tobigint-errors.js` test expects `TypeError` for any Number input (and for unboxable objects that coerce to Number). Our `StandardLibrary.ToBigInt` instead converts Numbers to BigInt (or throws `RangeError` for non-integers), so `assert.throws(TypeError, ...)` does not observe the expected error and throws `Test262Error` itself. The failing `bits-toindex-toprimitive.js` test expects a `TypeError` when `@@toPrimitive` exists but is not callable; `NumberHelper.ToIndex` calls `JsOps.ToNumericAsJsValue`, which uses `TryConvertToNumericPrimitiveJsValue` and silently ignores non-callable `@@toPrimitive`, falling back to `valueOf`/`toString`. That violates `ToPrimitive` rules and again causes `assert.throws` to fail.

**Fix Direction:**
Split Number handling from `ToBigInt`: make the `ToBigInt` abstract operation throw `TypeError` for Number inputs and update the BigInt constructor path to use a dedicated Number-to-BigInt conversion (so `BigInt(1)` still works). For `ToIndex`, either route object coercion through `JsOps.ToPrimitive` (which already throws on non-callable `@@toPrimitive`) or add the same callable check in `TryConvertToNumericPrimitiveJsValue` so `ToNumeric` correctly raises `TypeError` when `@@toPrimitive` is present but not callable.
