# DataView_prototype_getFloat64

FQN:
`Asynkron.JsEngine.Tests.Test262.BuiltInsTests.DataView_prototype_getFloat64`

Full test name:

- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.DataView_prototype_getFloat64("built-ins/DataView/prototype/getFloat64/detached-buffer-before-outofrange-byteoffset.js",False)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.DataView_prototype_getFloat64("built-ins/DataView/prototype/getFloat64/detached-buffer-before-outofrange-byteoffset.js",True)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.DataView_prototype_getFloat64("built-ins/DataView/prototype/getFloat64/detached-buffer.js",False)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.DataView_prototype_getFloat64("built-ins/DataView/prototype/getFloat64/detached-buffer.js",True)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.DataView_prototype_getFloat64("built-ins/DataView/prototype/getFloat64/resizable-buffer.js",False)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.DataView_prototype_getFloat64("built-ins/DataView/prototype/getFloat64/resizable-buffer.js",True)

---
## Diagnosis (2026-01-22)

**Summary:** DataView.getFloat64 validates range before detached/view-out-of-bounds checks, so detached buffers throw RangeError and resizable buffers that shrink out-of-bounds don't throw.

**Error Pattern:**
```
Asynkron.JsEngine.ThrowSignal: Unhandled JavaScript throw: Expected a TypeError but got a RangeError
Asynkron.JsEngine.ThrowSignal: Unhandled JavaScript throw: 13 Expected a TypeError but got a RangeError
Asynkron.JsEngine.ThrowSignal: Unhandled JavaScript throw: Expected a TypeError but got a Test262Error
```

**Analysis:**
Detached buffer tests expect a TypeError from IsDetachedBuffer before any byteOffset range checks. The engine instead computes range/byteOffset first and throws RangeError. For resizable ArrayBuffer, a fixed-length DataView becomes out-of-bounds after a shrink (buffer length < view byteOffset + view length). getFloat64 returns normally, so the view-out-of-bounds check against the current buffer length is missing or not applied for resizable buffers.

**Fix Direction:**
In the GetViewValue/DataView.getFloat64 path, check IsDetachedBuffer(buffer) before any range validation. Add an IsViewOutOfBounds check against the current buffer byteLength for resizable buffers and throw TypeError when the view is out-of-bounds; only then apply RangeError for requestIndex.

** DONE **
