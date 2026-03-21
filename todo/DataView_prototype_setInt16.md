# DataView_prototype_setInt16

FQN:
`Asynkron.JsEngine.Tests.Test262.BuiltInsTests.DataView_prototype_setInt16`

Full test name:

- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.DataView_prototype_setInt16("built-ins/DataView/prototype/setInt16/detached-buffer-before-outofrange-byteoffset.js",False)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.DataView_prototype_setInt16("built-ins/DataView/prototype/setInt16/detached-buffer-before-outofrange-byteoffset.js",True)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.DataView_prototype_setInt16("built-ins/DataView/prototype/setInt16/detached-buffer.js",False)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.DataView_prototype_setInt16("built-ins/DataView/prototype/setInt16/detached-buffer.js",True)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.DataView_prototype_setInt16("built-ins/DataView/prototype/setInt16/index-check-before-value-conversion.js",False)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.DataView_prototype_setInt16("built-ins/DataView/prototype/setInt16/index-check-before-value-conversion.js",True)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.DataView_prototype_setInt16("built-ins/DataView/prototype/setInt16/resizable-buffer.js",False)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.DataView_prototype_setInt16("built-ins/DataView/prototype/setInt16/resizable-buffer.js",True)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.DataView_prototype_setInt16("built-ins/DataView/prototype/setInt16/set-values-little-endian-order.js",False)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.DataView_prototype_setInt16("built-ins/DataView/prototype/setInt16/set-values-little-endian-order.js",True)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.DataView_prototype_setInt16("built-ins/DataView/prototype/setInt16/set-values-return-undefined.js",False)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.DataView_prototype_setInt16("built-ins/DataView/prototype/setInt16/set-values-return-undefined.js",True)

---
## Diagnosis (2026-01-22)

**Summary:** `DataView.prototype.setInt16` uses `ToNumber` + C# casts and only `CheckBounds`, so it violates SetViewValue ordering (ToIndex before value conversion), misses detached/out-of-bounds checks, and writes wrong Int16 values.

**Error Pattern:**
```
Asynkron.JsEngine.ThrowSignal: Unhandled JavaScript throw: Expected a TypeError but got a RangeError
Asynkron.JsEngine.ThrowSignal: Unhandled JavaScript throw: 13 Expected a TypeError but got a RangeError
Asynkron.JsEngine.ThrowSignal: Unhandled JavaScript throw: Expected a TypeError but got a Test262Error
Asynkron.JsEngine.ThrowSignal: Unhandled JavaScript throw: Expected SameValue('-1', '-28545') to be true
Asynkron.JsEngine.ThrowSignal: Unhandled JavaScript throw: value: 2147483648 Expected SameValue('-1', '0') to be true
```

**Analysis:**
`DataViewPrototype.SetInt16` computes `byteOffset` with `(int)JsOps.ToNumber(args[0])` and `value` with `(short)(int)JsOps.ToNumber(args[1])`. That triggers `valueOf` before `ToIndex`, so `index-check-before-value-conversion` throws the test's `Test262Error` instead of a `RangeError`. `JsDataView.CheckBounds` only compares against `ByteLength`, ignoring `JsArrayBuffer.IsDetached` and resizable-buffer view out-of-bounds semantics, so detached buffers throw `RangeError` (or succeed) where a `TypeError` is required. Finally, the Int16 conversion uses raw C# casts instead of ECMAScript `ToInt16`/`ToInt32` semantics, producing incorrect stored values (observed as `getInt16` returning `-1` instead of `-28545`/`0`) in the little-endian and conversion-table tests.

**Fix Direction:**
Implement `SetViewValue` ordering for DataView setters: `ToIndex(byteOffset)` first, then `IsDetachedBuffer`/`IsViewOutOfBounds` checks (resizable buffers) before range checks, then `ToNumber(value)` and `ToInt16` conversion. Replace `(short)(int)JsOps.ToNumber` with `JsNumericConversions.ToInt32(number)` plus a cast to `short` (and analogous conversions for UInt16). Add detached and out-of-bounds checks in `JsDataView` (throw `TypeError` before `RangeError`).

** DONE **
