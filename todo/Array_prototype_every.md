# Array_prototype_every

FQN:
`Asynkron.JsEngine.Tests.Test262.BuiltInsTests.Array_prototype_every`

Full test name:

- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.Array_prototype_every("built-ins/Array/prototype/every/15.4.4.16-1-15.js",True)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.Array_prototype_every("built-ins/Array/prototype/every/15.4.4.16-1-2.js",False)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.Array_prototype_every("built-ins/Array/prototype/every/15.4.4.16-1-3.js",False)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.Array_prototype_every("built-ins/Array/prototype/every/15.4.4.16-1-4.js",False)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.Array_prototype_every("built-ins/Array/prototype/every/15.4.4.16-1-4.js",True)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.Array_prototype_every("built-ins/Array/prototype/every/15.4.4.16-1-5.js",False)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.Array_prototype_every("built-ins/Array/prototype/every/15.4.4.16-7-c-ii-2.js",False)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.Array_prototype_every("built-ins/Array/prototype/every/15.4.4.16-7-c-ii-2.js",True)

---
## Diagnosis (2026-01-22)

**Summary:** Non-strict `arr.length` shrink throws on non-configurable elements; should fail silently so `every` can keep iterating.

**Error Pattern:**
```
Failed Array_prototype_every("built-ins/Array/prototype/every/15.4.4.16-7-b-16.js",False)
Asynkron.JsEngine.ThrowSignal: Unhandled JavaScript throw: 'TypeError': 'Invalid array length'
```

**Analysis:**
The failing case defines index 2 as non-configurable, then a getter on index 1 sets `arr.length = 2` during `Array.prototype.every`. In non-strict mode (flags: `noStrict`), that length reduction should *not* throw when it encounters a non-configurable element; the assignment should fail silently, leaving length unchanged and preserving index 2 so `every` can evaluate it and return `false`. The engine throws `TypeError: Invalid array length`, indicating the `length` setter path is using `Throw=true` semantics (or otherwise throwing on `ArraySetLength` failure) even in sloppy mode.

**Fix Direction:**
Ensure `PutValue/Set` passes `Throw=false` for non-strict references and that `ArraySetLength`/`DefineOwnProperty` respects `Throw=false` by returning `false` instead of throwing when length reduction fails due to non-configurable elements.
