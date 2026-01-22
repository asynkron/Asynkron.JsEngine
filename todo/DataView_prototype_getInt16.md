# DataView_prototype_getInt16

FQN:
`Asynkron.JsEngine.Tests.Test262.BuiltInsTests.DataView_prototype_getInt16`

Full test name:

- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.DataView_prototype_getInt16("built-ins/DataView/prototype/getInt16/detached-buffer-before-outofrange-byteoffset.js",False)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.DataView_prototype_getInt16("built-ins/DataView/prototype/getInt16/detached-buffer-before-outofrange-byteoffset.js",True)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.DataView_prototype_getInt16("built-ins/DataView/prototype/getInt16/detached-buffer.js",False)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.DataView_prototype_getInt16("built-ins/DataView/prototype/getInt16/detached-buffer.js",True)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.DataView_prototype_getInt16("built-ins/DataView/prototype/getInt16/resizable-buffer.js",False)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.DataView_prototype_getInt16("built-ins/DataView/prototype/getInt16/resizable-buffer.js",True)

---
## Diagnosis (2026-01-22)

**Summary:** DataView getInt16 performs range/out-of-bounds checks before (or instead of) detached-buffer/out-of-bounds view checks, yielding RangeError or no throw where a TypeError is required.

**Error Pattern:**
```
Asynkron.JsEngine.ThrowSignal: Unhandled JavaScript throw: 13 Expected a TypeError but got a RangeError
Asynkron.JsEngine.ThrowSignal: Unhandled JavaScript throw: Expected a TypeError but got a RangeError
Asynkron.JsEngine.ThrowSignal: Unhandled JavaScript throw: Expected a TypeError but got a Test262Error
```

**Analysis:**
The failing tests cover detached buffers and resizable ArrayBuffers. For detached buffers (including the "detached-buffer-before-outofrange-byteoffset" cases), the spec requires `IsDetachedBuffer(buffer)` to be checked before any range validation, and a TypeError must be thrown. The engine instead performs the range/byteOffset check first (or uses a zero-length view size), which produces a RangeError. For resizable buffers, after shrinking the buffer below the DataView’s range, the view is out-of-bounds and `GetViewValue` should throw a TypeError. The engine appears to skip this out-of-bounds view check, so `getInt16` completes successfully and the test throws a Test262Error.

**Fix Direction:**
Update the DataView `GetViewValue` path used by `getInt16` to (1) check `IsDetachedBuffer(buffer)` before computing view size or byteOffset range checks, and (2) for resizable buffers, detect out-of-bounds views (view offset + view length > buffer byte length) and throw TypeError before evaluating the requested index. Ensure the TypeError path runs before the RangeError path for byteOffset.
