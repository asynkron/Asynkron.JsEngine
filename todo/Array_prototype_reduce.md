# Array_prototype_reduce

FQN:
`Asynkron.JsEngine.Tests.Test262.BuiltInsTests.Array_prototype_reduce`

Full test name:

- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.Array_prototype_reduce("built-ins/Array/prototype/reduce/15.4.4.21-5-11.js",True)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.Array_prototype_reduce("built-ins/Array/prototype/reduce/15.4.4.21-5-12.js",True)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.Array_prototype_reduce("built-ins/Array/prototype/reduce/15.4.4.21-5-2.js",True)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.Array_prototype_reduce("built-ins/Array/prototype/reduce/15.4.4.21-5-3.js",False)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.Array_prototype_reduce("built-ins/Array/prototype/reduce/15.4.4.21-5-3.js",True)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.Array_prototype_reduce("built-ins/Array/prototype/reduce/15.4.4.21-5-4.js",False)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.Array_prototype_reduce("built-ins/Array/prototype/reduce/15.4.4.21-5-5.js",False)

---
## Diagnosis (2026-01-22)

**Summary:** Array length shrink in sloppy mode throws on non-configurable elements because length assignment always uses the strict/throwing path.

**Error Pattern:**
```
Failed Array_prototype_reduce("built-ins/Array/prototype/reduce/15.4.4.21-9-b-29.js",False)
Unhandled JavaScript throw: 'TypeError': 'Invalid array length'

Failed Array_prototype_reduce("built-ins/Array/prototype/reduce/15.4.4.21-9-b-16.js",False)
Unhandled JavaScript throw: 'TypeError': 'Invalid array length'
```

**Analysis:**
The failing tests reduce over an array where index 0's getter shrinks `length` to 2 while index 2 is a non-configurable accessor. Per spec, decreasing length must not delete non-configurable properties; in non-strict mode the length assignment should fail silently and clamp the length to `index + 1` (so index 2 remains and reduce still visits it). The engine instead throws `TypeError: Invalid array length` during the `length` assignment, so reduce aborts before hitting index 2.

**Fix Direction:**
Ensure array `length` assignment respects strictness. In the property assignment path (`JsOps.AssignPropertyValueJsValue`), pass `throwOnWritableFailure: context?.CurrentScope.IsStrict == true` when calling `JsArray.SetLength`. This lets `TryShrinkLength` return false without throwing in sloppy mode and preserves the non-configurable element so reduce proceeds.
