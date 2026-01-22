# DataView_prototype_setFloat16

FQN:
`Asynkron.JsEngine.Tests.Test262.BuiltInsTests.DataView_prototype_setFloat16`

Full test name:

- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.DataView_prototype_setFloat16("built-ins/DataView/prototype/setFloat16/detached-buffer-before-outofrange-byteoffset.js",False)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.DataView_prototype_setFloat16("built-ins/DataView/prototype/setFloat16/detached-buffer-before-outofrange-byteoffset.js",True)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.DataView_prototype_setFloat16("built-ins/DataView/prototype/setFloat16/detached-buffer.js",False)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.DataView_prototype_setFloat16("built-ins/DataView/prototype/setFloat16/detached-buffer.js",True)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.DataView_prototype_setFloat16("built-ins/DataView/prototype/setFloat16/index-check-before-value-conversion.js",False)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.DataView_prototype_setFloat16("built-ins/DataView/prototype/setFloat16/index-check-before-value-conversion.js",True)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.DataView_prototype_setFloat16("built-ins/DataView/prototype/setFloat16/no-value-arg.js",False)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.DataView_prototype_setFloat16("built-ins/DataView/prototype/setFloat16/no-value-arg.js",True)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.DataView_prototype_setFloat16("built-ins/DataView/prototype/setFloat16/resizable-buffer.js",False)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.DataView_prototype_setFloat16("built-ins/DataView/prototype/setFloat16/resizable-buffer.js",True)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.DataView_prototype_setFloat16("built-ins/DataView/prototype/setFloat16/toindex-byteoffset.js",False)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.DataView_prototype_setFloat16("built-ins/DataView/prototype/setFloat16/toindex-byteoffset.js",True)

---
## Diagnosis (2026-01-22)

**Summary:** `DataView.prototype.setFloat16` performs validation/conversion in the wrong order (detached/out-of-bounds checks and missing value handling), and resizable-buffer bounds are not enforced.

**Error Pattern:**
```
Unhandled JavaScript throw: Expected a TypeError but got a RangeError
Unhandled JavaScript throw: 13 Expected a TypeError but got a RangeError
Unhandled JavaScript throw:
Unhandled JavaScript throw: Expected SameValue(«0», «NaN») to be true
Unhandled JavaScript throw: no arg Expected SameValue(«0», «NaN») to be true
Unhandled JavaScript throw: Expected a TypeError but got a Test262Error
```

**Analysis:**
The detached-buffer cases expect TypeError when the view's buffer is detached, but the engine does a range check first (byte length becomes 0), yielding RangeError instead. The index-check-before-value-conversion test expects RangeError for negative/Infinity byteOffset before converting the value argument, but the engine converts the value first (poisoned `valueOf` triggers a Test262Error). The no-value-arg and toindex-byteoffset tests show `setFloat16()` writing `0` instead of `undefined` -> `NaN`. The resizable-buffer test expects a TypeError when a resizable ArrayBuffer shrink makes the view out-of-bounds, but the write succeeds (test throws Test262Error).

**Fix Direction:**
Match the `SetViewValue` algorithm: check `IsDetachedBuffer` before bounds checks, re-check after `ToIndex(byteOffset)`, and perform the range check before converting the value when the index is invalid. Treat missing `value` as `undefined` and apply `ToNumber` so the stored value is `NaN`. For resizable buffers, recompute view length and throw TypeError when the view is out-of-bounds after resize (use `IsViewOutOfBounds`/`GetViewByteLength`).

** DONE **
