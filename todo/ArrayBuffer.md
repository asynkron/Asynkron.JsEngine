# ArrayBuffer

FQN:
`Asynkron.JsEngine.Tests.Test262.BuiltInsTests.ArrayBuffer`

Full test name:

- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.ArrayBuffer("built-ins/ArrayBuffer/data-allocation-after-object-creation.js",False)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.ArrayBuffer("built-ins/ArrayBuffer/data-allocation-after-object-creation.js",True)

---
## Diagnosis (2026-01-22)

**Summary:** ArrayBuffer constructor pre-validates/allocates the data block before resolving `newTarget.prototype`, so a RangeError is thrown before the DummyError from the prototype getter.

**Error Pattern:**
```
Unhandled JavaScript throw: Expected a DummyError but got a RangeError
built-ins/ArrayBuffer/data-allocation-after-object-creation.js
```

**Analysis:**
The failing Test262 case uses `Reflect.construct(ArrayBuffer, [7 * 1024^5], newTarget)` where `newTarget.prototype` is a getter that throws `DummyError`. Per spec, `AllocateArrayBuffer` must call `OrdinaryCreateFromConstructor` (which reads `newTarget.prototype`) before `CreateByteDataBlock`. In `ArrayBufferHelper.ConstructBufferCore`, `RequireAllocatableLength(byteLength)` runs before `ReflectHelper.ResolveConstructPrototype(...)`, so the `int.MaxValue` guard throws a RangeError before the prototype getter can run. This reverses the spec ordering and causes the test harness to see RangeError instead of DummyError (both strict and non-strict).

**Fix Direction:**
Move length validation and data block allocation in `ArrayBufferHelper.ConstructBufferCore` to after `ResolveConstructPrototype`/object creation for the non-default `newTarget` path (i.e., mirror `AllocateArrayBuffer`: create object via `OrdinaryCreateFromConstructor` first, then `CreateByteDataBlock`). Ensure any RangeError from allocation limits only occurs after the prototype getter has been invoked.
