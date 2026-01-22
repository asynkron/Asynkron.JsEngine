# Array_prototype_sort

FQN:
`Asynkron.JsEngine.Tests.Test262.BuiltInsTests.Array_prototype_sort`

Full test name:

- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.Array_prototype_sort("built-ins/Array/prototype/sort/comparefn-shrink.js",False)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.Array_prototype_sort("built-ins/Array/prototype/sort/comparefn-shrink.js",True)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.Array_prototype_sort("built-ins/Array/prototype/sort/precise-prototype-accessors.js",False)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.Array_prototype_sort("built-ins/Array/prototype/sort/precise-prototype-accessors.js",True)

---
## Diagnosis (2026-01-22)

**Summary:** Array.prototype.sort uses a fast path that bypasses spec Get/Set semantics, so resizable TypedArray access throws on shrink and prototype accessors are skipped.

**Error Pattern:**
```
built-ins/Array/prototype/sort/comparefn-shrink.js
Asynkron.JsEngine.ThrowSignal: Unhandled JavaScript throw: 'TypeError': 'Out of bounds access on TypedArray'

built-ins/Array/prototype/sort/precise-prototype-accessors.js
Asynkron.JsEngine.ThrowSignal: Unhandled JavaScript throw: Expected SameValue(undefined, "set with 3") to be true
```

**Analysis:**
Array.prototype.sort appears to read/write elements through an optimized internal path (iterator/typed array fast path and direct element writes). When comparefn shrinks a resizable TypedArray buffer, the fast path continues to access indices that are now out-of-bounds and throws instead of treating them as absent/undefined. Separately, the sort write-back does not invoke [[Set]] on index "2" on the receiver, so the prototype setter is never called and `logs[1]` stays undefined.

**Fix Direction:**
Route Array.prototype.sort through the spec SortIndexedProperties behavior: use HasProperty + Get over numeric indices and Set for write-back so prototype accessors are honored. For TypedArrays backed by resizable buffers, re-check bounds per access (IsTypedArrayOutOfBounds / TypedArrayLength) and treat out-of-bounds elements as missing instead of throwing during compare or write-back.
