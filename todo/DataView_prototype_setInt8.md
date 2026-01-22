# DataView_prototype_setInt8

FQN:
`Asynkron.JsEngine.Tests.Test262.BuiltInsTests.DataView_prototype_setInt8`

Full test name:

- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.DataView_prototype_setInt8("built-ins/DataView/prototype/setInt8/detached-buffer-after-toindex-byteoffset.js",False)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.DataView_prototype_setInt8("built-ins/DataView/prototype/setInt8/detached-buffer-after-toindex-byteoffset.js",True)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.DataView_prototype_setInt8("built-ins/DataView/prototype/setInt8/detached-buffer-before-outofrange-byteoffset.js",False)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.DataView_prototype_setInt8("built-ins/DataView/prototype/setInt8/detached-buffer-before-outofrange-byteoffset.js",True)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.DataView_prototype_setInt8("built-ins/DataView/prototype/setInt8/detached-buffer.js",False)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.DataView_prototype_setInt8("built-ins/DataView/prototype/setInt8/detached-buffer.js",True)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.DataView_prototype_setInt8("built-ins/DataView/prototype/setInt8/index-check-before-value-conversion.js",False)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.DataView_prototype_setInt8("built-ins/DataView/prototype/setInt8/index-check-before-value-conversion.js",True)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.DataView_prototype_setInt8("built-ins/DataView/prototype/setInt8/index-is-out-of-range.js",False)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.DataView_prototype_setInt8("built-ins/DataView/prototype/setInt8/index-is-out-of-range.js",True)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.DataView_prototype_setInt8("built-ins/DataView/prototype/setInt8/resizable-buffer.js",False)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.DataView_prototype_setInt8("built-ins/DataView/prototype/setInt8/resizable-buffer.js",True)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.DataView_prototype_setInt8("built-ins/DataView/prototype/setInt8/set-values-return-undefined.js",False)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.DataView_prototype_setInt8("built-ins/DataView/prototype/setInt8/set-values-return-undefined.js",True)

---
## Diagnosis (2026-01-22)

**Summary:** `DataView.prototype.setInt8` uses naive int casts and cached view length, skipping `ToIndex`/detached/out-of-bounds checks and `ToInt8` conversion, so exceptions and stored values diverge from the spec.

**Error Pattern:**
```
System.IndexOutOfRangeException: Index was outside the bounds of the array.
Asynkron.JsEngine.ThrowSignal: Unhandled JavaScript throw: 13 Expected a TypeError but got a RangeError
Asynkron.JsEngine.ThrowSignal: Unhandled JavaScript throw: Expected a TypeError but got a Test262Error
Asynkron.JsEngine.ThrowSignal: Unhandled JavaScript throw:
Asynkron.JsEngine.ThrowSignal: Unhandled JavaScript throw: value: 2147483648 Expected SameValue(«-1», «0») to be true
```

**Analysis:**
`DataViewPrototype.SetInt8` casts `byteOffset` with `(int)JsOps.ToNumber` and `value` with `(sbyte)(int)JsOps.ToNumber`, which is neither `ToIndex` nor `ToInt8`. This means non-integral/Infinity offsets do not throw RangeError before value conversion, so the poisoned `valueOf` runs and throws a Test262Error. `JsDataView.CheckBounds` relies on the cached `ByteLength` set at construction and never checks `Buffer.IsDetached` or resizable out-of-bounds. After detach or shrink, bounds checks pass and the raw byte access throws `IndexOutOfRangeException`, and detached-buffer checks occur after range checks (RangeError instead of TypeError). Large numeric values are converted via C# int casts, so `2147483648` stores `-1` instead of `0` (missing modulo 2^8 conversion).

**Fix Direction:**
Implement `SetViewValue` ordering for DataView writes: `ToIndex(byteOffset)` first, then detached/out-of-bounds checks (including resizable buffer view-out-of-bounds), then range checks, then `ToInt8` value conversion. Ensure bounds checks use current buffer length (and throw TypeError for detached/out-of-bounds) so `IndexOutOfRangeException` never leaks and large values follow modulo 2^8 semantics.

** DONE **
