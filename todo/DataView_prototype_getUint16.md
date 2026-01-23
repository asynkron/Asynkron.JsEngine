# DataView_prototype_getUint16

FQN:
`Asynkron.JsEngine.Tests.Test262.BuiltInsTests.DataView_prototype_getUint16`

Full test name:

- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.DataView_prototype_getUint16("built-ins/DataView/prototype/getUint16/detached-buffer-before-outofrange-byteoffset.js",False)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.DataView_prototype_getUint16("built-ins/DataView/prototype/getUint16/detached-buffer-before-outofrange-byteoffset.js",True)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.DataView_prototype_getUint16("built-ins/DataView/prototype/getUint16/detached-buffer.js",False)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.DataView_prototype_getUint16("built-ins/DataView/prototype/getUint16/detached-buffer.js",True)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.DataView_prototype_getUint16("built-ins/DataView/prototype/getUint16/resizable-buffer.js",False)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.DataView_prototype_getUint16("built-ins/DataView/prototype/getUint16/resizable-buffer.js",True)

---
## Diagnosis (2026-01-22)

**Summary:** DataView getUint16 checks view bounds before validating detached/out-of-bounds buffers, and it does not throw TypeError for resizable-buffer out-of-bounds views.

**Error Pattern:**
```
Unhandled JavaScript throw: Expected a TypeError but got a RangeError
Unhandled JavaScript throw: Expected a TypeError but got a Test262Error
```

**Analysis:**
The detached-buffer tests expect a TypeError even when the byteOffset is out of range. The engine instead throws RangeError, which implies the range check runs before the detached-buffer check (or uses a detached buffer size of 0 and treats it as out of range). The resizable-buffer test shrinks the ArrayBuffer so the DataView is out-of-bounds, then calls getUint16. The implementation returns successfully (triggering Test262Error) rather than throwing TypeError. This indicates missing or incorrect IsDetachedBuffer/IsViewOutOfBounds handling for resizable buffers, and the required check order from GetViewValue is not followed.

**Fix Direction:**
Ensure DataView getUint16 (GetViewValue) first checks IsDetachedBuffer and IsViewOutOfBounds (for resizable buffers) before any range checks. If the view is out-of-bounds after a resize, throw TypeError. Only after those checks should it validate byteOffset + elementSize against the current view size and throw RangeError.

** DONE **
