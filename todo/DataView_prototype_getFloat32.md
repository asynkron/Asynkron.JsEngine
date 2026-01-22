# DataView_prototype_getFloat32

FQN:
`Asynkron.JsEngine.Tests.Test262.BuiltInsTests.DataView_prototype_getFloat32`

Full test name:

- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.DataView_prototype_getFloat32("built-ins/DataView/prototype/getFloat32/detached-buffer-before-outofrange-byteoffset.js",False)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.DataView_prototype_getFloat32("built-ins/DataView/prototype/getFloat32/detached-buffer-before-outofrange-byteoffset.js",True)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.DataView_prototype_getFloat32("built-ins/DataView/prototype/getFloat32/detached-buffer.js",False)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.DataView_prototype_getFloat32("built-ins/DataView/prototype/getFloat32/detached-buffer.js",True)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.DataView_prototype_getFloat32("built-ins/DataView/prototype/getFloat32/resizable-buffer.js",False)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.DataView_prototype_getFloat32("built-ins/DataView/prototype/getFloat32/resizable-buffer.js",True)

---
## Diagnosis (2026-01-22)

**Summary:** `getFloat32` checks bounds before detached/out-of-bounds view state, yielding RangeError or no error instead of the required TypeError.

**Error Pattern:**
```
Unhandled JavaScript throw: Expected a TypeError but got a RangeError
Unhandled JavaScript throw: 13 Expected a TypeError but got a RangeError
Unhandled JavaScript throw: Expected a TypeError but got a Test262Error
```

**Analysis:**
Detached-buffer tests show RangeError when a detached buffer should trigger TypeError before range validation. The resizable-buffer test expects TypeError when the DataView becomes out-of-bounds after shrinking the backing buffer, but no error is thrown (the test throws Test262Error). This points to `GetViewValue`/DataView access running range checks (or reading) without first checking `IsDetachedBuffer`/`IsViewOutOfBounds` per spec ordering for detached buffers and resizable array buffers.

**Fix Direction:**
Align `DataView.prototype.getFloat32` with the spec for `GetViewValue`: check `IsDetachedBuffer(buffer)` and `IsViewOutOfBounds(view)` before any range/element size checks, and throw TypeError for detached or out-of-bounds views. Ensure resizable ArrayBuffer views compute view size based on current buffer length so out-of-bounds views throw TypeError rather than returning a value.

** DONE **
