# DataView_prototype_getInt8

FQN:
`Asynkron.JsEngine.Tests.Test262.BuiltInsTests.DataView_prototype_getInt8`

Full test name:

- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.DataView_prototype_getInt8("built-ins/DataView/prototype/getInt8/detached-buffer-after-toindex-byteoffset.js",False)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.DataView_prototype_getInt8("built-ins/DataView/prototype/getInt8/detached-buffer-after-toindex-byteoffset.js",True)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.DataView_prototype_getInt8("built-ins/DataView/prototype/getInt8/detached-buffer-before-outofrange-byteoffset.js",False)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.DataView_prototype_getInt8("built-ins/DataView/prototype/getInt8/detached-buffer-before-outofrange-byteoffset.js",True)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.DataView_prototype_getInt8("built-ins/DataView/prototype/getInt8/detached-buffer.js",False)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.DataView_prototype_getInt8("built-ins/DataView/prototype/getInt8/detached-buffer.js",True)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.DataView_prototype_getInt8("built-ins/DataView/prototype/getInt8/index-is-out-of-range.js",False)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.DataView_prototype_getInt8("built-ins/DataView/prototype/getInt8/index-is-out-of-range.js",True)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.DataView_prototype_getInt8("built-ins/DataView/prototype/getInt8/resizable-buffer.js",False)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.DataView_prototype_getInt8("built-ins/DataView/prototype/getInt8/resizable-buffer.js",True)

---
## Diagnosis (2026-01-22)

**Summary:** DataView getInt8 uses cached view length and raw buffer indexing without detached/out-of-bounds checks, so it throws IndexOutOfRangeException or returns a value instead of the spec-required RangeError/TypeError.

**Error Pattern:**
```
System.IndexOutOfRangeException: Index was outside the bounds of the array.
  at Asynkron.JsEngine.JsTypes.JsDataView.GetInt8(Int32 byteOffset) ... JsDataView.cs:line 342
...
Test262Test.ThrowError ... (assert.throws failed in resizable-buffer.js expecting TypeError)
```

**Analysis:**
`DataViewPrototype.GetInt8` calls `JsOps.ToNumber` and then `JsDataView.GetInt8`, which only checks `ByteLength` (fixed at creation) and indexes `Buffer.Buffer` directly. After a buffer is detached or resized smaller, `ByteLength` no longer matches `Buffer.Buffer.Length`. That lets `CheckBounds` pass, then array indexing throws or returns a value, instead of performing the spec-mandated detached/out-of-bounds checks. The required `GetViewValue` ordering (ToIndex first, then detached/out-of-bounds validation) is missing, so detached-buffer and resizable-buffer cases fail with the wrong error type or no error.

**Fix Direction:**
- Implement `GetViewValue` semantics: apply `ToIndex` to byteOffset, then check `IsDetached` and `IsViewOutOfBounds` against current `Buffer.ByteLength`/`ByteOffset`, throwing TypeError before any data access.
- Keep RangeError for true offset-out-of-range cases (and ensure it is thrown instead of IndexOutOfRangeException).
- Consider recomputing/validating DataView bounds on each access for resizable ArrayBuffer (or track "view out-of-bounds" state per spec).
