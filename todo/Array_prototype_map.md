# Array_prototype_map

FQN:
`Asynkron.JsEngine.Tests.Test262.BuiltInsTests.Array_prototype_map`

Full test name:

- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.Array_prototype_map("built-ins/Array/prototype/map/15.4.4.19-5-16.js",False)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.Array_prototype_map("built-ins/Array/prototype/map/15.4.4.19-5-17.js",False)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.Array_prototype_map("built-ins/Array/prototype/map/15.4.4.19-5-17.js",True)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.Array_prototype_map("built-ins/Array/prototype/map/15.4.4.19-5-18.js",True)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.Array_prototype_map("built-ins/Array/prototype/map/15.4.4.19-5-19.js",False)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.Array_prototype_map("built-ins/Array/prototype/map/15.4.4.19-5-19.js",True)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.Array_prototype_map("built-ins/Array/prototype/map/15.4.4.19-5-2.js",False)

---
## Diagnosis (2026-01-22)

**Summary:** Array length reduction throws on non-configurable elements in non-strict mode; it should fail silently and leave length unchanged.

**Error Pattern:**
```
Asynkron.JsEngine.ThrowSignal: Unhandled JavaScript throw: 'TypeError': 'Invalid array length'
  at Asynkron.JsEngine.Ast.TypedAstEvaluator.EvaluateProgramJsValueCore(...)
built-ins/Array/prototype/map/15.4.4.19-8-b-16.js (noStrict)
```

**Analysis:**
The failing test defines a non-configurable element at index 2, then a getter on index 1 that sets `arr.length = 2` during `map`. In non-strict mode, the length reduction should fail (because index 2 is non-configurable) without throwing, and `map` should keep iterating up to the originally captured length (3). The engine instead throws `TypeError: Invalid array length` while setting length, so the callback never completes and the test fails.

**Fix Direction:**
Adjust the array length setter / `ArraySetLength` path so failed length reductions due to non-configurable elements return `false` without throwing when `Throw` is `false` (sloppy mode), and preserve the original length. Only throw in strict mode or when `Throw` is `true`.
