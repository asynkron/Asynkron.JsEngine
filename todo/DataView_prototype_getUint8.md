# DataView_prototype_getUint8

FQN:
`Asynkron.JsEngine.Tests.Test262.BuiltInsTests.DataView_prototype_getUint8`

Full test name:

- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.DataView_prototype_getUint8("built-ins/DataView/prototype/getUint8/detached-buffer-after-toindex-byteoffset.js",False)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.DataView_prototype_getUint8("built-ins/DataView/prototype/getUint8/detached-buffer-after-toindex-byteoffset.js",True)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.DataView_prototype_getUint8("built-ins/DataView/prototype/getUint8/detached-buffer-before-outofrange-byteoffset.js",False)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.DataView_prototype_getUint8("built-ins/DataView/prototype/getUint8/detached-buffer-before-outofrange-byteoffset.js",True)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.DataView_prototype_getUint8("built-ins/DataView/prototype/getUint8/detached-buffer.js",False)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.DataView_prototype_getUint8("built-ins/DataView/prototype/getUint8/detached-buffer.js",True)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.DataView_prototype_getUint8("built-ins/DataView/prototype/getUint8/index-is-out-of-range.js",False)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.DataView_prototype_getUint8("built-ins/DataView/prototype/getUint8/index-is-out-of-range.js",True)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.DataView_prototype_getUint8("built-ins/DataView/prototype/getUint8/resizable-buffer.js",False)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.DataView_prototype_getUint8("built-ins/DataView/prototype/getUint8/resizable-buffer.js",True)

---
## Diagnosis (2026-01-22)

**Summary:** DataView.getUint8 uses ToNumber+int casting and fixed view bounds without detached/resizable checks, so it throws the wrong errors (or .NET IndexOutOfRange) and checks in the wrong order vs GetViewValue.

**Error Pattern:**
```
System.IndexOutOfRangeException: Index was outside the bounds of the array.
   at Asynkron.JsEngine.JsTypes.JsDataView.GetUint8(Int32 byteOffset)

Unhandled JavaScript throw: 13 Expected a TypeError but got a RangeError

Unhandled JavaScript throw: Expected a TypeError but got a Test262Error
```

**Analysis:**
`DataViewPrototype.GetUint8` converts `byteOffset` with `ToNumber` and casts to `int`, skipping the spec `ToIndex` conversion (so Infinity/-1 do not produce the required RangeError). `JsDataView.CheckBounds` uses the view's fixed `ByteLength` and does not check `Buffer.IsDetached` or view-out-of-bounds for resizable buffers. When the buffer is detached, the view still reports its original `ByteLength`, so `CheckBounds` passes and `Buffer.Buffer[...]` throws `IndexOutOfRangeException` instead of a TypeError. For `detached-buffer-before-outofrange-byteoffset`, the range check runs before the detached check, producing RangeError when the spec requires TypeError. For `resizable-buffer.js`, the view is out-of-bounds after a shrink, but the code still returns a value, causing the test's `assert.throws` to fail.

**Fix Direction:**
Implement the spec `GetViewValue` ordering for DataView reads: apply `ToIndex` to `byteOffset`, then check `IsDetachedBuffer` and `IsViewOutOfBounds` (resizable buffers), throwing TypeError when appropriate. Compute `viewSize` from the current buffer length and view byteOffset/byteLength, then perform the range check and throw RangeError. Also ensure out-of-range access surfaces as RangeError (either by explicit checks or by catching `IndexOutOfRangeException` in `WithRangeError`).

** DONE **
