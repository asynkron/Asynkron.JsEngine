# DataView_prototype_getFloat16

FQN:
`Asynkron.JsEngine.Tests.Test262.BuiltInsTests.DataView_prototype_getFloat16`

Full test name:

- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.DataView_prototype_getFloat16("built-ins/DataView/prototype/getFloat16/detached-buffer-before-outofrange-byteoffset.js",False)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.DataView_prototype_getFloat16("built-ins/DataView/prototype/getFloat16/detached-buffer-before-outofrange-byteoffset.js",True)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.DataView_prototype_getFloat16("built-ins/DataView/prototype/getFloat16/detached-buffer.js",False)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.DataView_prototype_getFloat16("built-ins/DataView/prototype/getFloat16/detached-buffer.js",True)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.DataView_prototype_getFloat16("built-ins/DataView/prototype/getFloat16/resizable-buffer.js",False)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.DataView_prototype_getFloat16("built-ins/DataView/prototype/getFloat16/resizable-buffer.js",True)

---
## Diagnosis (2026-01-22)

**Summary:** `DataView.prototype.getFloat16` throws the wrong error type because it checks bounds before detached/out-of-bounds checks, and skips the resizable buffer out-of-bounds check.

**Error Pattern:**
```
Unhandled JavaScript throw: Expected a TypeError but got a RangeError
Unhandled JavaScript throw: 13 Expected a TypeError but got a RangeError
Unhandled JavaScript throw: Expected a TypeError but got a Test262Error
```

**Analysis:**
Detached-buffer tests expect a `TypeError` even when the byteOffset is out of range, but the implementation throws `RangeError`, indicating the range check runs before verifying the buffer is detached. The resizable-buffer test expects a `TypeError` after shrinking the buffer below the DataView bounds, but `getFloat16` completes successfully, which triggers the test’s `Test262Error` sentinel. This points to missing/incorrect `IsDetachedBuffer` and `IsViewOutOfBounds` checks (or wrong ordering) in the `getFloat16` path.

**Fix Direction:**
Implement the spec order for `DataView.prototype.getFloat16`: after `ToIndex(byteOffset)`, verify the buffer is not detached and the view is not out-of-bounds (including resizable ArrayBuffer shrink), throwing `TypeError` before any range check. Only after these checks should the range/offset validation run and throw `RangeError` when appropriate.
