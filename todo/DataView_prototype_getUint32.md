# DataView_prototype_getUint32

FQN:
`Asynkron.JsEngine.Tests.Test262.BuiltInsTests.DataView_prototype_getUint32`

Full test name:

- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.DataView_prototype_getUint32("built-ins/DataView/prototype/getUint32/detached-buffer-before-outofrange-byteoffset.js",False)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.DataView_prototype_getUint32("built-ins/DataView/prototype/getUint32/detached-buffer-before-outofrange-byteoffset.js",True)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.DataView_prototype_getUint32("built-ins/DataView/prototype/getUint32/detached-buffer.js",False)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.DataView_prototype_getUint32("built-ins/DataView/prototype/getUint32/detached-buffer.js",True)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.DataView_prototype_getUint32("built-ins/DataView/prototype/getUint32/resizable-buffer.js",False)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.DataView_prototype_getUint32("built-ins/DataView/prototype/getUint32/resizable-buffer.js",True)

---
## Diagnosis (2026-01-22)

**Summary:** DataView.getUint32 does bounds checks before detached/OOB checks, and resizable ArrayBuffer view OOB detection is missing, so wrong/no error types are thrown.

**Error Pattern:**
```
Unhandled JavaScript throw: 13 Expected a TypeError but got a RangeError
Unhandled JavaScript throw: Expected a TypeError but got a RangeError
Unhandled JavaScript throw: Expected a TypeError but got a Test262Error
```

**Analysis:**
The detached-buffer tests expect a TypeError when the [[ViewedArrayBuffer]] is detached. The engine instead throws a RangeError (or reports one), which matches doing the "getIndex + elementSize > viewSize" check before the IsDetachedBuffer check. The resizable-buffer test shrinks the underlying ArrayBuffer so the DataView becomes out-of-bounds; the spec requires a TypeError, but the engine completes the read and the test throws Test262Error, indicating no OOB TypeError is raised for resizable buffers.

**Fix Direction:**
In GetViewValue/DataView.getUint32, ensure the IsDetachedBuffer check happens before any range checks. For resizable ArrayBuffer-backed DataViews, add or use an "IsViewOutOfBounds" check based on the current buffer byteLength and view offset/length; if the view is OOB, throw TypeError before attempting the read or RangeError logic.
