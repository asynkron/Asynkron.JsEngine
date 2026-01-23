# DataView_prototype_getBigUint64

FQN:
`Asynkron.JsEngine.Tests.Test262.BuiltInsTests.DataView_prototype_getBigUint64`

Full test name:

- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.DataView_prototype_getBigUint64("built-ins/DataView/prototype/getBigUint64/detached-buffer-before-outofrange-byteoffset.js",False)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.DataView_prototype_getBigUint64("built-ins/DataView/prototype/getBigUint64/detached-buffer-before-outofrange-byteoffset.js",True)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.DataView_prototype_getBigUint64("built-ins/DataView/prototype/getBigUint64/detached-buffer.js",False)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.DataView_prototype_getBigUint64("built-ins/DataView/prototype/getBigUint64/detached-buffer.js",True)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.DataView_prototype_getBigUint64("built-ins/DataView/prototype/getBigUint64/resizable-buffer.js",False)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.DataView_prototype_getBigUint64("built-ins/DataView/prototype/getBigUint64/resizable-buffer.js",True)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.DataView_prototype_getBigUint64("built-ins/DataView/prototype/getBigUint64/toindex-byteoffset-toprimitive.js",False)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.DataView_prototype_getBigUint64("built-ins/DataView/prototype/getBigUint64/toindex-byteoffset-toprimitive.js",True)

---
## Diagnosis (2026-01-22)

**Summary:** `getBigUint64` uses ToNumber + fixed DataView bounds checks, so detached/resizable buffer and ToIndex/ToPrimitive error semantics are missing.

**Error Pattern:**
```
Asynkron.JsEngine.ThrowSignal: Unhandled JavaScript throw:
Asynkron.JsEngine.ThrowSignal: Unhandled JavaScript throw: Expected a TypeError but got a Test262Error
```

**Analysis:**
`DataViewPrototype.GetBigUint64` converts `byteOffset` via `JsOps.ToNumber` and casts to `int`, bypassing the spec's ToIndex requirements and the strict ToPrimitive behavior for `Symbol.toPrimitive` (non-callable or object-returning cases should throw TypeError). That breaks the `toindex-byteoffset-toprimitive` cases. Separately, `JsDataView.CheckBounds` only checks the DataView's fixed `ByteLength` and never consults `JsArrayBuffer.IsDetached` or the current buffer length after resize, so detached buffers or resizable-arraybuffer shrink/out-of-bounds accesses do not throw the required TypeError (or are turned into RangeError). The detached-buffer-before-outofrange test shows detachment must be checked before bounds validation.

**Fix Direction:**
Implement a spec-compliant DataView get path: use ToIndex for `byteOffset` (including TypeError on non-callable `Symbol.toPrimitive`), check `IsDetached` and view-in-bounds against the current `ArrayBuffer` length on each access, and throw TypeError for detached/out-of-bounds rather than RangeError; ensure detachment is checked before bounds validation for out-of-range offsets.

** DONE **
