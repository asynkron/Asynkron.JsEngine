# DataView_prototype_setUint8

FQN:
`Asynkron.JsEngine.Tests.Test262.BuiltInsTests.DataView_prototype_setUint8`

Full test name:

- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.DataView_prototype_setUint8("built-ins/DataView/prototype/setUint8/detached-buffer-after-toindex-byteoffset.js",False)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.DataView_prototype_setUint8("built-ins/DataView/prototype/setUint8/detached-buffer-after-toindex-byteoffset.js",True)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.DataView_prototype_setUint8("built-ins/DataView/prototype/setUint8/detached-buffer-before-outofrange-byteoffset.js",False)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.DataView_prototype_setUint8("built-ins/DataView/prototype/setUint8/detached-buffer-before-outofrange-byteoffset.js",True)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.DataView_prototype_setUint8("built-ins/DataView/prototype/setUint8/detached-buffer.js",False)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.DataView_prototype_setUint8("built-ins/DataView/prototype/setUint8/detached-buffer.js",True)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.DataView_prototype_setUint8("built-ins/DataView/prototype/setUint8/index-check-before-value-conversion.js",False)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.DataView_prototype_setUint8("built-ins/DataView/prototype/setUint8/index-check-before-value-conversion.js",True)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.DataView_prototype_setUint8("built-ins/DataView/prototype/setUint8/index-is-out-of-range.js",False)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.DataView_prototype_setUint8("built-ins/DataView/prototype/setUint8/index-is-out-of-range.js",True)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.DataView_prototype_setUint8("built-ins/DataView/prototype/setUint8/resizable-buffer.js",False)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.DataView_prototype_setUint8("built-ins/DataView/prototype/setUint8/resizable-buffer.js",True)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.DataView_prototype_setUint8("built-ins/DataView/prototype/setUint8/set-values-return-undefined.js",False)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.DataView_prototype_setUint8("built-ins/DataView/prototype/setUint8/set-values-return-undefined.js",True)

---
## Diagnosis (2026-01-22)

**Summary:** DataView `setUint8` uses non-spec conversions/order and stale bounds (no detached/resizable handling), leading to wrong error types, IndexOutOfRange, and incorrect Uint8 coercion.

**Error Pattern:**
```
Expected a TypeError but got a RangeError
System.IndexOutOfRangeException: Index was outside the bounds of the array.
Unhandled JavaScript throw: Expected a TypeError but got a Test262Error
Unhandled JavaScript throw: value: 2147483648 Expected SameValue(«255», «0») to be true
```

**Analysis:**
`DataViewPrototype.SetUint8` converts `byteOffset` via `JsOps.ToNumber` and casts to `int`, then converts `value` to `(byte)(int)`, before any spec-mandated checks. That skips `ToIndex` (RangeError for -1/-1.5/Infinity), triggers value conversion too early (poisoned `valueOf` throws), and clamps large numbers via `int` overflow/saturation (e.g., 2147483648 becomes 255 instead of 0). `JsDataView.CheckBounds` uses the view's cached `ByteLength` instead of current buffer length and doesn't check `IsDetached`, so detached or resized buffers fall through to raw array access and throw `IndexOutOfRangeException` or allow out-of-bounds writes. This reverses the required error ordering (detached vs range) and misses resizable-buffer out-of-bounds `TypeError`.

**Fix Direction:**
Implement spec-order `SetViewValue` for DataView: use `NumberHelper.ToIndex` for `byteOffset`, check `IsDetached` and resizable out-of-bounds before writing, and map errors to `TypeError`/`RangeError` instead of .NET exceptions. Convert `value` with proper `ToUint8` (mod 256) after `ToIndex` (and in the right order relative to range checks per spec). Update `JsDataView` bounds checks to use current buffer length and detached state (or centralize in DataViewPrototype to avoid stale `ByteLength`).

** DONE **
