# Array_prototype_lastIndexOf

FQN:
`Asynkron.JsEngine.Tests.Test262.BuiltInsTests.Array_prototype_lastIndexOf`

Full test name:

- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.Array_prototype_lastIndexOf("built-ins/Array/prototype/lastIndexOf/15.4.4.15-5-6.js",False)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.Array_prototype_lastIndexOf("built-ins/Array/prototype/lastIndexOf/15.4.4.15-5-6.js",True)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.Array_prototype_lastIndexOf("built-ins/Array/prototype/lastIndexOf/15.4.4.15-5-7.js",False)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.Array_prototype_lastIndexOf("built-ins/Array/prototype/lastIndexOf/15.4.4.15-5-7.js",True)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.Array_prototype_lastIndexOf("built-ins/Array/prototype/lastIndexOf/15.4.4.15-5-8.js",False)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.Array_prototype_lastIndexOf("built-ins/Array/prototype/lastIndexOf/15.4.4.15-5-8.js",True)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.Array_prototype_lastIndexOf("built-ins/Array/prototype/lastIndexOf/15.4.4.15-5-9.js",False)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.Array_prototype_lastIndexOf("built-ins/Array/prototype/lastIndexOf/15.4.4.15-5-9.js",True)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.Array_prototype_lastIndexOf("built-ins/Array/prototype/lastIndexOf/resizable-buffer.js",False)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.Array_prototype_lastIndexOf("built-ins/Array/prototype/lastIndexOf/resizable-buffer.js",True)

---
## Diagnosis (2026-01-22)

**Summary:** Array.prototype.lastIndexOf throws when it should tolerate array length shrink failures and resizable TypedArray out-of-bounds, so tests expecting -1 or a valid index see TypeErrors.

**Error Pattern:**
```
Unhandled JavaScript throw: 'TypeError': 'Invalid array length'
Unhandled JavaScript throw: 'TypeError': 'Out of bounds access on TypedArray'
```

**Analysis:**
The current failures are `built-ins/Array/prototype/lastIndexOf/15.4.4.15-8-a-19.js` (noStrict) and `resizable-buffer.js` (strict + non-strict). In 15.4.4.15-8-a-19, the getter on index "3" sets `arr.length = 2` while a non-configurable element exists at index 2. Per spec in non-strict mode this length write should fail silently (length stays 4), and lastIndexOf should still find the non-configurable element at index 2. Instead, the length setter throws `TypeError: Invalid array length`. In resizable-buffer.js, Array.prototype.lastIndexOf is called on TypedArrays backed by resizable buffers. When those TypedArrays become out-of-bounds after resize, the engine throws `TypeError: Out of bounds access on TypedArray` rather than treating the array-like length as 0 and returning -1.

**Fix Direction:**
Ensure array length writes respect strict vs sloppy semantics: when a length shrink fails due to non-configurable elements, return false without throwing in non-strict contexts. Also reconsider the TypedArray fast-path in `Array.prototype.lastIndexOf`: avoid delegating to `TypedArrayBase.LastIndexOfInternal` (which throws on out-of-bounds), or adjust that path to treat out-of-bounds TypedArrays as length 0 and return -1 for Array.prototype.lastIndexOf calls.
