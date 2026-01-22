# DataView_prototype_setUint16

FQN:
`Asynkron.JsEngine.Tests.Test262.BuiltInsTests.DataView_prototype_setUint16`

Full test name:

- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.DataView_prototype_setUint16("built-ins/DataView/prototype/setUint16/detached-buffer-before-outofrange-byteoffset.js",False)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.DataView_prototype_setUint16("built-ins/DataView/prototype/setUint16/detached-buffer-before-outofrange-byteoffset.js",True)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.DataView_prototype_setUint16("built-ins/DataView/prototype/setUint16/detached-buffer.js",False)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.DataView_prototype_setUint16("built-ins/DataView/prototype/setUint16/detached-buffer.js",True)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.DataView_prototype_setUint16("built-ins/DataView/prototype/setUint16/index-check-before-value-conversion.js",False)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.DataView_prototype_setUint16("built-ins/DataView/prototype/setUint16/index-check-before-value-conversion.js",True)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.DataView_prototype_setUint16("built-ins/DataView/prototype/setUint16/resizable-buffer.js",False)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.DataView_prototype_setUint16("built-ins/DataView/prototype/setUint16/resizable-buffer.js",True)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.DataView_prototype_setUint16("built-ins/DataView/prototype/setUint16/set-values-little-endian-order.js",False)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.DataView_prototype_setUint16("built-ins/DataView/prototype/setUint16/set-values-little-endian-order.js",True)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.DataView_prototype_setUint16("built-ins/DataView/prototype/setUint16/set-values-return-undefined.js",False)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.DataView_prototype_setUint16("built-ins/DataView/prototype/setUint16/set-values-return-undefined.js",True)

---
## Diagnosis (2026-01-22)

**Summary:** setUint16 performs value conversion and static bounds checks before the spec-required detach/out-of-bounds checks and uses int-cast instead of ToUint16, so it throws wrong errors and writes wrong bytes for large values and resizable buffers.

**Error Pattern:**
```
Expected a TypeError but got a RangeError
Expected a TypeError but got a Test262Error
Expected SameValue(65535, 36991) to be true
value: 2147483648 Expected SameValue(65535, 0) to be true
```

**Analysis:**
- DataViewPrototype.SetUint16 (and the JsDataView host method) converts `value` via `(ushort)(int)JsOps.ToNumber` before offset validation. This calls `valueOf` for poisoned values even when `byteOffset` is invalid; Test262 expects RangeError from ToIndex before value conversion (`index-check-before-value-conversion`).
- Detached-buffer tests throw RangeError because CheckBounds runs against the stored DataView.ByteLength and does not check `Buffer.IsDetached`; spec requires TypeError when IsDetachedBuffer is true.
- Resizable-buffer tests pass unexpectedly because DataView.ByteLength is fixed at construction; after shrink, `CheckBounds` still succeeds and the write completes, so the test throws `Test262Error`.
- For large values (e.g., 2147483648, 4160782224), the `(int)` cast overflows/saturates instead of applying ToUint16 modulo 2^16, producing 0xFFFF and breaking the little-endian/value tests.

**Fix Direction:**
- Rework setUint16 to follow SetViewValue ordering: check detached buffer, convert byteOffset with ToIndex, validate range against the current buffer length (including resizable buffers), then convert the value with a proper ToUint16 (mod 2^16) helper and write bytes with the requested endianness.
- Avoid converting `value` before offset validation in DataViewPrototype.SetUint16 and the JsDataView host method; centralize conversions in a shared helper (e.g., NumberHelper/JsOps ToUint16) and update bounds checks to use buffer dynamic length.
