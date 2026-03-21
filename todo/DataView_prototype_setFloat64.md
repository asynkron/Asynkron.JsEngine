# DataView_prototype_setFloat64

FQN:
`Asynkron.JsEngine.Tests.Test262.BuiltInsTests.DataView_prototype_setFloat64`

Full test name:

- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.DataView_prototype_setFloat64("built-ins/DataView/prototype/setFloat64/detached-buffer-before-outofrange-byteoffset.js",False)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.DataView_prototype_setFloat64("built-ins/DataView/prototype/setFloat64/detached-buffer-before-outofrange-byteoffset.js",True)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.DataView_prototype_setFloat64("built-ins/DataView/prototype/setFloat64/detached-buffer.js",False)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.DataView_prototype_setFloat64("built-ins/DataView/prototype/setFloat64/detached-buffer.js",True)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.DataView_prototype_setFloat64("built-ins/DataView/prototype/setFloat64/index-check-before-value-conversion.js",False)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.DataView_prototype_setFloat64("built-ins/DataView/prototype/setFloat64/index-check-before-value-conversion.js",True)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.DataView_prototype_setFloat64("built-ins/DataView/prototype/setFloat64/no-value-arg.js",False)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.DataView_prototype_setFloat64("built-ins/DataView/prototype/setFloat64/no-value-arg.js",True)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.DataView_prototype_setFloat64("built-ins/DataView/prototype/setFloat64/resizable-buffer.js",False)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.DataView_prototype_setFloat64("built-ins/DataView/prototype/setFloat64/resizable-buffer.js",True)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.DataView_prototype_setFloat64("built-ins/DataView/prototype/setFloat64/toindex-byteoffset.js",False)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.DataView_prototype_setFloat64("built-ins/DataView/prototype/setFloat64/toindex-byteoffset.js",True)

---
## Diagnosis (2026-01-22)

**Summary:** `DataView.prototype.setFloat64` uses the wrong conversion/check order and lacks detached/resizable buffer guards, so it throws the wrong errors and stores the wrong value when `value` is missing.

**Error Pattern:**
```
Expected a TypeError but got a RangeError
Unhandled JavaScript throw:
Expected SameValue("0", "NaN") to be true
Expected a TypeError but got a Test262Error
```

**Analysis:**
`SetFloat64` in `src/Asynkron.JsEngine/StdLib/DataView/DataViewPrototype.cs` (and the `JsDataView` host method path) converts `value` via `ToNumber` before running `ToIndex` on `byteOffset`. This trips the `poisoned.valueOf()` in `index-check-before-value-conversion.js` and violates the spec ordering (RangeError from `ToIndex` should happen before value conversion). The method also defaults missing `value` to `0.0` instead of `undefined`, so `ToNumber` never produces `NaN`, failing `no-value-arg.js` and the "no arg" assertion in `toindex-byteoffset.js`. Finally, `JsDataView.CheckBounds` only compares against the view's cached `ByteLength` and never checks `Buffer.IsDetached` or resizable out-of-bounds, so detached buffers raise RangeError (from bounds) instead of TypeError, and resizable-buffer out-of-bounds writes succeed (causing a Test262Error).

**Fix Direction:**
Implement `SetViewValue` ordering in `SetFloat64`: compute `byteOffset` with `ToIndex` first, then check `Buffer.IsDetached` and view out-of-bounds (including resizable buffers) before any RangeError, and only then `ToNumber(value)` (treat missing `value` as `undefined` so it becomes `NaN`). Apply the same ordering in both `DataViewPrototype` and `JsDataView` paths, and ensure resizable-buffer out-of-bounds throws TypeError.

** DONE **
