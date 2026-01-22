# DataView_prototype_setInt32

FQN:
`Asynkron.JsEngine.Tests.Test262.BuiltInsTests.DataView_prototype_setInt32`

Full test name:

- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.DataView_prototype_setInt32("built-ins/DataView/prototype/setInt32/detached-buffer-before-outofrange-byteoffset.js",False)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.DataView_prototype_setInt32("built-ins/DataView/prototype/setInt32/detached-buffer-before-outofrange-byteoffset.js",True)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.DataView_prototype_setInt32("built-ins/DataView/prototype/setInt32/detached-buffer.js",False)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.DataView_prototype_setInt32("built-ins/DataView/prototype/setInt32/detached-buffer.js",True)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.DataView_prototype_setInt32("built-ins/DataView/prototype/setInt32/index-check-before-value-conversion.js",False)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.DataView_prototype_setInt32("built-ins/DataView/prototype/setInt32/index-check-before-value-conversion.js",True)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.DataView_prototype_setInt32("built-ins/DataView/prototype/setInt32/resizable-buffer.js",False)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.DataView_prototype_setInt32("built-ins/DataView/prototype/setInt32/resizable-buffer.js",True)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.DataView_prototype_setInt32("built-ins/DataView/prototype/setInt32/set-values-little-endian-order.js",False)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.DataView_prototype_setInt32("built-ins/DataView/prototype/setInt32/set-values-little-endian-order.js",True)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.DataView_prototype_setInt32("built-ins/DataView/prototype/setInt32/set-values-return-undefined.js",False)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.DataView_prototype_setInt32("built-ins/DataView/prototype/setInt32/set-values-return-undefined.js",True)

---
## Diagnosis (2026-01-22)

**Summary:** DataView `setInt32` uses simplified host-method conversions and stale bounds checks, so spec-required ordering (ToIndex, detachment, resizable bounds, ToInt32) is violated and the stored bytes are wrong.

**Error Pattern:**
```
Expected a TypeError but got a RangeError
Expected a TypeError but got a Test262Error
Unhandled JavaScript throw: valueOf called
Expected SameValue(«-129», «-1870724872») to be true
```

**Analysis:**
`JsDataView` checks `CheckBounds` against the cached `ByteLength` before any detachment check, which flips the error type/order for `detached-buffer*` (RangeError instead of TypeError). The host methods convert `byteOffset` and `value` via `TryGetDouble` and cast to `int` before bounds checks, so `ToIndex` is not enforced and the poisoned value conversion runs (index-check-before-value-conversion). For resizable buffers, `ByteLength` is fixed at construction, so shrink operations don't invalidate the view and writes succeed (Test262Error). The same conversion shortcuts (non-number -> 0, no `ToInt32` modulo semantics) yield incorrect stored bytes/readbacks in `set-values-little-endian-order` and `set-values-return-undefined`.

**Fix Direction:**
Implement `SetViewValue` semantics in a single DataView path: `ToIndex` for `byteOffset`, check `IsDetachedBuffer` before bounds, compute `viewSize` from current buffer length (resizable), then `ToNumber`/`ToInt32` for `value`, and use correct endian writes. Remove or guard the `TryGetDouble` fast paths so conversions and error ordering follow the spec.
