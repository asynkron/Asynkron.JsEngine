# Array_prototype_toSpliced

FQN:
`Asynkron.JsEngine.Tests.Test262.BuiltInsTests.Array_prototype_toSpliced`

Full test name:

- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.Array_prototype_toSpliced("built-ins/Array/prototype/toSpliced/length-exceeding-array-length-limit.js",False)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.Array_prototype_toSpliced("built-ins/Array/prototype/toSpliced/length-exceeding-array-length-limit.js",True)

---
## Diagnosis (2026-01-22)

**Summary:** `Array.prototype.toSpliced` throws `RangeError` where the spec requires `TypeError` when `newLen` exceeds `2 ** 53 - 1`.

**Error Pattern:**
```
Unhandled JavaScript throw: Expected a TypeError but got a RangeError
built-ins/Array/prototype/toSpliced/length-exceeding-array-length-limit.js
```

**Analysis:**
Both strict and non-strict variants of `length-exceeding-array-length-limit.js` fail. The test expects `RangeError` when the resulting length exceeds `2 ** 32 - 1`, but expects `TypeError` when `newLen > 2 ** 53 - 1` (e.g., `length = 2 ** 53 - 1` with one inserted item). The engine currently throws `RangeError` in those cases, which implies the `2 ** 53 - 1` guard is missing or happens after the array-length (`2 ** 32 - 1`) limit check.

**Fix Direction:**
In `Array.prototype.toSpliced`, compute `newLen` from `LengthOfArrayLike` and ensure the spec step `if newLen > 2 ** 53 - 1 throw TypeError` executes before `ArrayCreate`/array-length-limit checks. Keep `RangeError` only for the `2 ** 32 - 1` array length limit.

** DONE **
