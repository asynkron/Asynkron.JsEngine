# BigInt_prototype_toString

FQN:
`Asynkron.JsEngine.Tests.Test262.BuiltInsTests.BigInt_prototype_toString`

Full test name:

- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.BigInt_prototype_toString("built-ins/BigInt/prototype/toString/prototype-call.js",False)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.BigInt_prototype_toString("built-ins/BigInt/prototype/toString/prototype-call.js",True)

---
## Diagnosis (2026-01-22)

**Summary:** `BigInt.prototype` is treated as a BigInt wrapper (has `__value__`), so `thisBigIntValue` succeeds and radix validation throws `RangeError` instead of the required `TypeError`.

**Error Pattern:**
```
Unhandled JavaScript throw: Expected a TypeError but got a RangeError
```

**Analysis:**
`prototype-call.js` calls `BigInt.prototype.toString(1)` and expects a `TypeError` because `BigInt.prototype` has no [[BigIntData]] slot. The current implementation sets `__value__` on the prototype in `BigIntPrototype.ConfigurePrototype`, and `RequireBigIntValue` accepts any object with `__value__`. That makes `BigInt.prototype` look like a BigInt wrapper, so `thisBigIntValue` does not throw and the code proceeds to radix validation, producing a `RangeError` for radix `1`. Both strict and non-strict variants fail with the same mismatch.

**Fix Direction:**
Stop treating `BigInt.prototype` as a boxed BigInt. Remove the `__value__` initialization from the prototype or add a brand check in `RequireBigIntValue` to reject the `BigInt.prototype` object (only accept real `JsBigInt` or wrapper objects created by `BigIntHelper.CreateBigIntWrapper`).

** DONE **
