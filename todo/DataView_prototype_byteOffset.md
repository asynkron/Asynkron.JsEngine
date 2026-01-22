# DataView_prototype_byteOffset

FQN:
`Asynkron.JsEngine.Tests.Test262.BuiltInsTests.DataView_prototype_byteOffset`

Full test name:

- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.DataView_prototype_byteOffset("built-ins/DataView/prototype/byteOffset/detached-buffer.js",False)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.DataView_prototype_byteOffset("built-ins/DataView/prototype/byteOffset/detached-buffer.js",True)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.DataView_prototype_byteOffset("built-ins/DataView/prototype/byteOffset/resizable-array-buffer-auto.js",False)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.DataView_prototype_byteOffset("built-ins/DataView/prototype/byteOffset/resizable-array-buffer-auto.js",True)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.DataView_prototype_byteOffset("built-ins/DataView/prototype/byteOffset/resizable-array-buffer-fixed.js",False)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.DataView_prototype_byteOffset("built-ins/DataView/prototype/byteOffset/resizable-array-buffer-fixed.js",True)

---
## Diagnosis (2026-01-22)

**Summary:** DataView.prototype.byteOffset getter skips detached/out-of-bounds checks, so it returns a value instead of throwing TypeError after buffer detach or resize.

**Error Pattern:**
```
Asynkron.JsEngine.ThrowSignal: Unhandled JavaScript throw:
Asynkron.JsEngine.ThrowSignal: Unhandled JavaScript throw: Expected a TypeError but got a Test262Error
```

**Analysis:**
The DataView byteOffset getter in `src/Asynkron.JsEngine/StdLib/DataView/DataViewPrototype.cs` simply returns `dv.ByteOffset` without spec-mandated validation. For `detached-buffer.js`, `$DETACHBUFFER` succeeds and the getter should throw TypeError when the viewed ArrayBuffer is detached, but it doesn't, so the test harness throws a Test262Error (empty message in the ThrowSignal format). For the resizable-array-buffer tests, after shrinking beyond the DataView bounds, the getter must throw TypeError; instead it returns normally, so the test throws `Test262Error` ("Expected a TypeError but got a Test262Error"). The common pattern is missing detached/out-of-bounds checks for DataView getters, especially for resizable buffers and length-tracking views.

**Fix Direction:**
Implement DataView validation similar to TypedArray: in the byteOffset getter (and likely byteLength/buffer for completeness), check `dv.Buffer.IsDetached` and throw TypeError when detached. Add an `IsViewOutOfBounds`/length-tracking check for resizable ArrayBuffers (e.g., based on stored byteOffset/byteLength or a new length-tracking flag) and throw TypeError when the view no longer fits after resize.

** DONE **
