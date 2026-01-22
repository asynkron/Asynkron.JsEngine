# DataView_prototype_setUint32

FQN:
`Asynkron.JsEngine.Tests.Test262.BuiltInsTests.DataView_prototype_setUint32`

Full test name:

- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.DataView_prototype_setUint32("built-ins/DataView/prototype/setUint32/detached-buffer-before-outofrange-byteoffset.js",False)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.DataView_prototype_setUint32("built-ins/DataView/prototype/setUint32/detached-buffer-before-outofrange-byteoffset.js",True)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.DataView_prototype_setUint32("built-ins/DataView/prototype/setUint32/detached-buffer.js",False)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.DataView_prototype_setUint32("built-ins/DataView/prototype/setUint32/detached-buffer.js",True)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.DataView_prototype_setUint32("built-ins/DataView/prototype/setUint32/index-check-before-value-conversion.js",False)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.DataView_prototype_setUint32("built-ins/DataView/prototype/setUint32/index-check-before-value-conversion.js",True)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.DataView_prototype_setUint32("built-ins/DataView/prototype/setUint32/resizable-buffer.js",False)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.DataView_prototype_setUint32("built-ins/DataView/prototype/setUint32/resizable-buffer.js",True)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.DataView_prototype_setUint32("built-ins/DataView/prototype/setUint32/set-values-little-endian-order.js",False)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.DataView_prototype_setUint32("built-ins/DataView/prototype/setUint32/set-values-little-endian-order.js",True)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.DataView_prototype_setUint32("built-ins/DataView/prototype/setUint32/set-values-return-undefined.js",False)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.DataView_prototype_setUint32("built-ins/DataView/prototype/setUint32/set-values-return-undefined.js",True)

---
## Diagnosis (2026-01-22)

**Summary:** `setUint32` uses C# casts and late/incorrect checks, so value conversion, detach/out-of-bounds handling, and ToIndex ordering diverge from spec.

**Error Pattern:**
```
Expected a TypeError but got a RangeError
Expected a TypeError but got a Test262Error
Expected SameValue(«0», «4160782224») to be true
Unhandled JavaScript throw: [empty message in index-check-before-value-conversion / set-values-return-undefined]
```

**Analysis:**
`DataViewPrototype.SetUint32` converts `byteOffset` with `JsOps.ToNumber` + `(int)` and converts `value` with `(uint)JsOps.ToNumber` before any range/detach checks. This breaks spec ordering (ToIndex should happen before value conversion) and triggers `valueOf` on poisoned values, causing the index-check-before-value-conversion tests to fail. The C# `(uint)` cast turns negative/out-of-range doubles into `0` instead of ECMAScript `ToUint32` (mod 2^32), so writes are wrong and the little-endian/value-conversion tests fail. In `JsDataView.CheckBounds`, detached buffers are not checked and resizable-buffer out-of-bounds is not detected; it throws RangeError for OOB and allows OOB after resize, so detached-buffer tests see RangeError instead of TypeError and resizable-buffer tests complete successfully (assert.throws reports Test262Error).

**Fix Direction:**
Move to spec order in `DataViewPrototype.SetUint32` (or a shared SetViewValue helper): perform `ToIndex` on `byteOffset` first, then check detached/out-of-bounds (TypeError for detached or OOB resizable view), then convert `value` with a proper `ToUint32` helper. Avoid direct `(uint)` casts. Update `JsDataView`/buffer logic so resizable ArrayBuffer shrink marks the view out-of-bounds and throws TypeError, not RangeError, before any write.
