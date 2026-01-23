# DataView_prototype_setBigUint64

FQN:
`Asynkron.JsEngine.Tests.Test262.BuiltInsTests.DataView_prototype_setBigUint64`

Full test name:

- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.DataView_prototype_setBigUint64("built-ins/DataView/prototype/setBigUint64/resizable-buffer.js",False)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.DataView_prototype_setBigUint64("built-ins/DataView/prototype/setBigUint64/resizable-buffer.js",True)

---
## Diagnosis (2026-01-22)

**Summary:** DataView.setBigUint64 does not throw on resizable-buffer out-of-bounds views after a shrink.

**Error Pattern:**
```
Unhandled JavaScript throw: Expected a TypeError but got a Test262Error
built-ins/DataView/prototype/setBigUint64/resizable-buffer.js
```

**Analysis:**
The resizable-buffer test shrinks the ArrayBuffer from 24 to 8 bytes after creating a fixed-length DataView (offset 0, length 16). Per spec, the view becomes out-of-bounds and any DataView access must throw a TypeError. In Asynkron.JsEngine, setBigUint64 completes successfully, so the test throws Test262Error instead of seeing a TypeError. This points to missing or incorrect IsViewOutOfBounds/ValidateDataView checks for resizable ArrayBuffers in DataView setter paths.

**Fix Direction:**
Ensure DataView.prototype.setBigUint64 (and shared SetViewValue helpers) call ValidateDataView and perform the resizable-buffer out-of-bounds check on each access. If the viewed buffer is resizable and its current byte length is smaller than byteOffset + elementSize (8), throw TypeError before writing.

** DONE **
