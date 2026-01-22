# DataView_prototype_setFloat32

FQN:
`Asynkron.JsEngine.Tests.Test262.BuiltInsTests.DataView_prototype_setFloat32`

Full test name:

- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.DataView_prototype_setFloat32("built-ins/DataView/prototype/setFloat32/detached-buffer-before-outofrange-byteoffset.js",False)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.DataView_prototype_setFloat32("built-ins/DataView/prototype/setFloat32/detached-buffer-before-outofrange-byteoffset.js",True)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.DataView_prototype_setFloat32("built-ins/DataView/prototype/setFloat32/detached-buffer.js",False)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.DataView_prototype_setFloat32("built-ins/DataView/prototype/setFloat32/detached-buffer.js",True)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.DataView_prototype_setFloat32("built-ins/DataView/prototype/setFloat32/index-check-before-value-conversion.js",False)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.DataView_prototype_setFloat32("built-ins/DataView/prototype/setFloat32/index-check-before-value-conversion.js",True)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.DataView_prototype_setFloat32("built-ins/DataView/prototype/setFloat32/negative-byteoffset-throws.js",True)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.DataView_prototype_setFloat32("built-ins/DataView/prototype/setFloat32/no-value-arg.js",False)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.DataView_prototype_setFloat32("built-ins/DataView/prototype/setFloat32/no-value-arg.js",True)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.DataView_prototype_setFloat32("built-ins/DataView/prototype/setFloat32/resizable-buffer.js",False)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.DataView_prototype_setFloat32("built-ins/DataView/prototype/setFloat32/resizable-buffer.js",True)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.DataView_prototype_setFloat32("built-ins/DataView/prototype/setFloat32/return-abrupt-from-tonumber-byteoffset-symbol.js",False)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.DataView_prototype_setFloat32("built-ins/DataView/prototype/setFloat32/return-abrupt-from-tonumber-byteoffset-symbol.js",True)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.DataView_prototype_setFloat32("built-ins/DataView/prototype/setFloat32/return-abrupt-from-tonumber-byteoffset.js",False)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.DataView_prototype_setFloat32("built-ins/DataView/prototype/setFloat32/return-abrupt-from-tonumber-byteoffset.js",True)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.DataView_prototype_setFloat32("built-ins/DataView/prototype/setFloat32/toindex-byteoffset.js",False)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.DataView_prototype_setFloat32("built-ins/DataView/prototype/setFloat32/toindex-byteoffset.js",True)

---
## Diagnosis (2026-01-22)

**Summary:** `setFloat32`'s `SetViewValue` path does checks/conversions in the wrong order and mishandles missing values and resizable-buffer bounds, producing the wrong errors or writing 0 instead of NaN.

**Error Pattern:**
```
Expected a TypeError but got a RangeError
13 Expected a TypeError but got a RangeError
Unhandled JavaScript throw:
Expected SameValue(0, NaN) to be true
Expected a TypeError but got a Test262Error
no arg Expected SameValue(0, NaN) to be true
```

**Analysis:**
Detached-buffer tests show a RangeError because the implementation checks the byteOffset range (using a detached/zero-length buffer) before the required IsDetachedBuffer check, which should throw TypeError first. The index-check-before-value-conversion tests fail because the value conversion happens before validating the byteOffset, so the poisoned value's `valueOf` is invoked instead of throwing RangeError. The no-value-arg and toindex-byteoffset failures show that missing `value` is treated as 0 instead of undefined -> NaN. The resizable-buffer failures show that when the DataView becomes out-of-bounds after shrink, `setFloat32` does not throw and the test's sentinel `Test262Error` fires.

**Fix Direction:**
Rework `SetViewValue`/`DataView.prototype.setFloat32` to follow spec order: check detached buffer (and view out-of-bounds for resizable buffers) before range checks; perform ToIndex on byteOffset, then range-check against the current view byteLength; only after passing those checks convert the `value` (using undefined when arg is missing) and write the bytes. Ensure resizable-buffer out-of-bounds paths throw TypeError.
