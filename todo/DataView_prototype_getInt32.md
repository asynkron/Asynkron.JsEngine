# DataView_prototype_getInt32

FQN:
`Asynkron.JsEngine.Tests.Test262.BuiltInsTests.DataView_prototype_getInt32`

Full test name:

- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.DataView_prototype_getInt32("built-ins/DataView/prototype/getInt32/detached-buffer-before-outofrange-byteoffset.js",False)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.DataView_prototype_getInt32("built-ins/DataView/prototype/getInt32/detached-buffer-before-outofrange-byteoffset.js",True)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.DataView_prototype_getInt32("built-ins/DataView/prototype/getInt32/detached-buffer.js",False)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.DataView_prototype_getInt32("built-ins/DataView/prototype/getInt32/detached-buffer.js",True)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.DataView_prototype_getInt32("built-ins/DataView/prototype/getInt32/resizable-buffer.js",False)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.DataView_prototype_getInt32("built-ins/DataView/prototype/getInt32/resizable-buffer.js",True)

---
## Diagnosis (2026-01-22)

**Summary:** DataView.getInt32 checks bounds before detached/out-of-bounds view validation, so detached buffers throw RangeError and resizable buffers don't throw TypeError when the view becomes out-of-bounds.

**Error Pattern:**
```
Asynkron.JsEngine.ThrowSignal: Unhandled JavaScript throw: Expected a TypeError but got a RangeError
Asynkron.JsEngine.ThrowSignal: Unhandled JavaScript throw: 13 Expected a TypeError but got a RangeError
Asynkron.JsEngine.ThrowSignal: Unhandled JavaScript throw: Expected a TypeError but got a Test262Error
```

**Analysis:**
Tests for detached buffers (`detached-buffer.js`, `detached-buffer-before-outofrange-byteoffset.js`) expect `GetViewValue` to throw `TypeError` as soon as `[[ViewedArrayBuffer]]` is detached, before any range/out-of-range checks. The engine instead reports `RangeError`, implying the range check runs first.

For resizable buffers (`resizable-buffer.js`), shrinking the buffer below the view's extent should make the view out-of-bounds and `getInt32` must throw `TypeError`. Instead the call succeeds and the test throws `Test262Error` ("operation completed successfully"), indicating the engine does not check `IsViewOutOfBounds` / updated byte length for resizable array buffers on each access.

**Fix Direction:**
In the shared `GetViewValue`/DataView read path, reorder and expand validation:
- Check `IsDetachedBuffer(buffer)` before any range/out-of-range logic and throw `TypeError`.
- For resizable array buffers, compute current view byte length and throw `TypeError` if the view is out-of-bounds before validating `requestIndex` against view size.
