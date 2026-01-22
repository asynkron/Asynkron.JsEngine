# Array_prototype_filter

FQN:
`Asynkron.JsEngine.Tests.Test262.BuiltInsTests.Array_prototype_filter`

Full test name:

- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.Array_prototype_filter("built-ins/Array/prototype/filter/15.4.4.20-9-c-i-22.js",False)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.Array_prototype_filter("built-ins/Array/prototype/filter/15.4.4.20-9-c-i-22.js",True)

---
## Diagnosis (2026-01-22)

**Summary:** Array.prototype.filter skips inherited accessor properties and throws on non-strict length shrink, breaking spec iteration semantics.

**Error Pattern:**
```
Asynkron.JsEngine.ThrowSignal: Unhandled JavaScript throw: 'TypeError': 'Invalid array length'
Asynkron.JsEngine.ThrowSignal: Unhandled JavaScript throw: newArr.length Expected SameValue(0, 1) to be true
```

**Analysis:**
15.4.4.20-9-b-16.js throws when a getter sets `arr.length = 2` while a non-configurable index 2 exists. Spec ArraySetLength must fail without throwing in non-strict mode and must not delete non-configurable elements; length should remain 3 so filter can still read index 2.
15.4.4.20-9-c-i-22.js (strict + non-strict) expects inherited accessor property `Array.prototype[0]` (setter-only) to be observed. HasProperty should be true and Get returns undefined, so callback runs and output length is 1. Engine returns length 0, implying filter only checks own elements (or dense storage) and ignores prototype chain entries.

**Fix Direction:**
Ensure Array.prototype.filter uses HasProperty/Get per spec (including prototype chain) and iterates with the initial `len` snapshot.
Fix ArraySetLength/length assignment so shrinking below non-configurable elements returns false and only throws in strict mode; non-strict must fail silently and keep length unchanged.
