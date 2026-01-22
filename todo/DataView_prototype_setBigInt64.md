# DataView_prototype_setBigInt64

FQN:
`Asynkron.JsEngine.Tests.Test262.BuiltInsTests.DataView_prototype_setBigInt64`

Full test name:

- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.DataView_prototype_setBigInt64("built-ins/DataView/prototype/setBigInt64/detached-buffer-before-outofrange-byteoffset.js",False)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.DataView_prototype_setBigInt64("built-ins/DataView/prototype/setBigInt64/detached-buffer-before-outofrange-byteoffset.js",True)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.DataView_prototype_setBigInt64("built-ins/DataView/prototype/setBigInt64/detached-buffer.js",False)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.DataView_prototype_setBigInt64("built-ins/DataView/prototype/setBigInt64/detached-buffer.js",True)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.DataView_prototype_setBigInt64("built-ins/DataView/prototype/setBigInt64/index-check-before-value-conversion.js",False)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.DataView_prototype_setBigInt64("built-ins/DataView/prototype/setBigInt64/index-check-before-value-conversion.js",True)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.DataView_prototype_setBigInt64("built-ins/DataView/prototype/setBigInt64/no-value-arg.js",False)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.DataView_prototype_setBigInt64("built-ins/DataView/prototype/setBigInt64/no-value-arg.js",True)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.DataView_prototype_setBigInt64("built-ins/DataView/prototype/setBigInt64/resizable-buffer.js",False)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.DataView_prototype_setBigInt64("built-ins/DataView/prototype/setBigInt64/resizable-buffer.js",True)

---
## Diagnosis (2026-01-22)

**Summary:** `setBigInt64` performs validation/conversion in the wrong order and misses detached/out-of-bounds checks for resizable buffers, leading to wrong error types or no error.

**Error Pattern:**
```
Unhandled JavaScript throw: Expected a TypeError but got a RangeError
Unhandled JavaScript throw: Expected a TypeError but got a Test262Error
Unhandled JavaScript throw:  (empty message in index-check-before-value-conversion.js,
detached-buffer-before-outofrange-byteoffset.js, no-value-arg.js)
```

**Analysis:**
Detached-buffer tests (`detached-buffer.js`, `detached-buffer-before-outofrange-byteoffset.js`) show RangeError or other failures when a TypeError should be thrown first, implying detachment/out-of-bounds checks happen after range checks. The poisoned value test (`index-check-before-value-conversion.js`) indicates value conversion runs before `ToIndex`, so `valueOf` is invoked and throws before the expected RangeError. The missing-value case (`no-value-arg.js`) should throw TypeError on `ToBigInt(undefined)` but does not. The resizable buffer test (`resizable-buffer.js`) completes successfully after shrinking the buffer below the view size; spec requires a TypeError when the view is out-of-bounds after resize.

**Fix Direction:**
Reorder `DataView.prototype.setBigInt64` to follow spec: validate DataView, check `IsDetachedBuffer` before any range checks, perform `ToIndex(byteOffset)` before `ToBigInt(value)`, and throw TypeError for missing/undefined value. Add resizable-buffer handling via `IsViewOutOfBounds`/current buffer length checks so out-of-bounds views throw TypeError after resize.
