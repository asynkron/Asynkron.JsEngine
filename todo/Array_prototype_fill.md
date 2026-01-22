# Array_prototype_fill

FQN:
`Asynkron.JsEngine.Tests.Test262.BuiltInsTests.Array_prototype_fill`

Full test name:

- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.Array_prototype_fill("built-ins/Array/prototype/fill/resizable-buffer.js",False)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.Array_prototype_fill("built-ins/Array/prototype/fill/resizable-buffer.js",True)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.Array_prototype_fill("built-ins/Array/prototype/fill/return-abrupt-from-start-as-symbol.js",True)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.Array_prototype_fill("built-ins/Array/prototype/fill/return-abrupt-from-start.js",False)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.Array_prototype_fill("built-ins/Array/prototype/fill/return-abrupt-from-start.js",True)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.Array_prototype_fill("built-ins/Array/prototype/fill/return-abrupt-from-this-length-as-symbol.js",False)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.Array_prototype_fill("built-ins/Array/prototype/fill/typed-array-resize.js",False)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.Array_prototype_fill("built-ins/Array/prototype/fill/typed-array-resize.js",True)

---
## Diagnosis (2026-01-22)

**Summary:** `Array.prototype.fill` throws when a resizable ArrayBuffer shrink makes a TypedArray out-of-bounds during argument evaluation; the method should treat OOB TypedArrays as length 0 and no-op.

**Error Pattern:**
```
Failed Array_prototype_fill("built-ins/Array/prototype/fill/typed-array-resize.js",True/False)
Asynkron.JsEngine.ThrowSignal: Unhandled JavaScript throw: 'TypeError': 'Out of bounds access on TypedArray'
```

**Analysis:**
The failing test (`built-ins/Array/prototype/fill/typed-array-resize.js`) shrinks a resizable ArrayBuffer via `valueOf` while `Array.prototype.fill` is evaluating its arguments. The TypedArray (`fixedLength`) becomes out-of-bounds after the resize. The engine proceeds to set elements and throws `TypeError: Out of bounds access on TypedArray`, but the spec expects no throw and no writes (resulting buffer contents remain `[0, 0]`). This indicates the method does not re-check TypedArray OOB state after argument evaluation and/or uses a stale length instead of treating OOB typed arrays as length 0.

**Fix Direction:**
After evaluating `value`, `start`, and `end`, detect if `this` is a TypedArray backed by a resizable buffer and has become out-of-bounds. If so, return `this` early (no-op). Alternatively (or additionally), ensure `LengthOfArrayLike` for TypedArrays returns 0 when OOB so the fill loop never runs. Avoid invoking `IntegerIndexedElementSet` when OOB to prevent the TypeError.
