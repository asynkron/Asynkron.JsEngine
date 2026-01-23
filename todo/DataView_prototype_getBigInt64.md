# DataView_prototype_getBigInt64

FQN:
`Asynkron.JsEngine.Tests.Test262.BuiltInsTests.DataView_prototype_getBigInt64`

Full test name:

- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.DataView_prototype_getBigInt64("built-ins/DataView/prototype/getBigInt64/detached-buffer-before-outofrange-byteoffset.js",False)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.DataView_prototype_getBigInt64("built-ins/DataView/prototype/getBigInt64/detached-buffer-before-outofrange-byteoffset.js",True)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.DataView_prototype_getBigInt64("built-ins/DataView/prototype/getBigInt64/detached-buffer.js",False)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.DataView_prototype_getBigInt64("built-ins/DataView/prototype/getBigInt64/detached-buffer.js",True)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.DataView_prototype_getBigInt64("built-ins/DataView/prototype/getBigInt64/resizable-buffer.js",False)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.DataView_prototype_getBigInt64("built-ins/DataView/prototype/getBigInt64/resizable-buffer.js",True)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.DataView_prototype_getBigInt64("built-ins/DataView/prototype/getBigInt64/toindex-byteoffset-toprimitive.js",False)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.DataView_prototype_getBigInt64("built-ins/DataView/prototype/getBigInt64/toindex-byteoffset-toprimitive.js",True)

---
## Diagnosis (2026-01-22)

**Summary:** DataView.getBigInt64 skips detached/resizable buffer checks and uses a ToNumber/ToPrimitive path that does not enforce ToIndex errors, so expected TypeErrors never surface.

**Error Pattern:**
```
Asynkron.JsEngine.ThrowSignal: Unhandled JavaScript throw:
Asynkron.JsEngine.ThrowSignal: Unhandled JavaScript throw: Expected a TypeError but got a Test262Error
```

**Analysis:**
DataView uses `(int)JsOps.ToNumber` for `byteOffset` and `JsDataView.CheckBounds` only compares against the fixed `ByteLength` captured at construction. There is no `IsDetached` guard and no out-of-bounds check against the current `Buffer.ByteLength` for resizable buffers. As a result, detached buffers do not throw TypeError (and may surface RangeError or succeed), and resizable-buffer shrink still reads successfully, causing `assert.throws(TypeError)` to fail (Test262Error). The `toindex-byteoffset-toprimitive` failures also point at `JsOps.ToNumeric` via `TryConvertToNumericPrimitiveJsValue`, which skips throwing when `@@toPrimitive` is present but non-callable or returns an object; the spec requires a TypeError in those cases.

**Fix Direction:**
Implement GetViewValue semantics for DataView: check `buffer.IsDetached` before any offset/range logic, then apply `ToIndex` for `byteOffset`, and verify the view is not out-of-bounds against the current `Buffer.ByteLength` (throw TypeError, not RangeError). In `DataViewPrototype`, use `NumberHelper.ToIndex` (or a dedicated helper) instead of `(int)JsOps.ToNumber`. Tighten `TryConvertToNumericPrimitiveJsValue` to throw TypeError when `@@toPrimitive` exists but is non-callable or returns a non-primitive.

** DONE **
