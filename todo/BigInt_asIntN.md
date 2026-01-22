# BigInt_asIntN

FQN:
`Asynkron.JsEngine.Tests.Test262.BuiltInsTests.BigInt_asIntN`

Full test name:

- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.BigInt_asIntN("built-ins/BigInt/asIntN/bigint-tobigint-errors.js",False)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.BigInt_asIntN("built-ins/BigInt/asIntN/bigint-tobigint-errors.js",True)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.BigInt_asIntN("built-ins/BigInt/asIntN/bits-toindex-toprimitive.js",False)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.BigInt_asIntN("built-ins/BigInt/asIntN/bits-toindex-toprimitive.js",True)

---
## Diagnosis (2026-01-22)

**Summary:** BigInt.asIntN uses conversions that are too permissive: `ToBigInt` accepts Numbers/Booleans and `ToIndex` ignores invalid @@toPrimitive, so expected TypeErrors are never thrown and Test262 asserts fail.

**Error Pattern:**
```
Failed BigInt_asIntN("built-ins/BigInt/asIntN/bigint-tobigint-errors.js",False)
Asynkron.JsEngine.ThrowSignal: Unhandled JavaScript throw:

Failed BigInt_asIntN("built-ins/BigInt/asIntN/bits-toindex-toprimitive.js",False)
Asynkron.JsEngine.ThrowSignal: Unhandled JavaScript throw:
```

**Analysis:**
`bigint-tobigint-errors.js` expects `BigInt.asIntN` to throw `TypeError` when the bigint argument is a Number, Symbol, undefined, or null. Our `StandardLibrary.ToBigInt` currently converts Numbers/Booleans (and throws `RangeError` for NaN/Infinity), which is closer to BigInt() conversion semantics than `ToBigInt` in the spec. That means `assert.throws(TypeError, ...)` never sees a TypeError for Number inputs, so the harness throws `Test262Error` and the test fails.

`bits-toindex-toprimitive.js` expects `ToIndex` to throw when `@@toPrimitive` is present but non-callable or returns an object. `NumberHelper.ToIndex` uses `JsOps.ToNumericAsJsValue`, which relies on `TryConvertToNumericPrimitiveJsValue`; that helper silently skips a non-callable `@@toPrimitive` and falls back to `valueOf`/`toString`, so the TypeError never occurs and the asserts fail.

**Fix Direction:**
- Make `StandardLibrary.ToBigInt` spec-correct: accept only BigInt and String (via parsing), and throw `TypeError` for Number/Boolean/Symbol/undefined/null. Add a separate helper for BigInt() and other call sites that intentionally accept Numbers (e.g., `ToBigIntFromNumberOrString` or similar).
- Update `JsOps.TryConvertToNumericPrimitiveJsValue` to match `ToPrimitive` rules: if `@@toPrimitive` exists and is not callable, throw `TypeError`; if it returns a non-primitive, throw `TypeError` rather than falling back to `valueOf`/`toString`.
