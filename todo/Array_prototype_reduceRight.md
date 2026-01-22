# Array_prototype_reduceRight

FQN:
`Asynkron.JsEngine.Tests.Test262.BuiltInsTests.Array_prototype_reduceRight`

Full test name:

- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.Array_prototype_reduceRight("built-ins/Array/prototype/reduceRight/15.4.4.22-9-c-i-12.js",True)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.Array_prototype_reduceRight("built-ins/Array/prototype/reduceRight/15.4.4.22-9-c-i-13.js",False)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.Array_prototype_reduceRight("built-ins/Array/prototype/reduceRight/15.4.4.22-9-c-i-13.js",True)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.Array_prototype_reduceRight("built-ins/Array/prototype/reduceRight/15.4.4.22-9-c-i-14.js",False)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.Array_prototype_reduceRight("built-ins/Array/prototype/reduceRight/15.4.4.22-9-c-i-14.js",True)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.Array_prototype_reduceRight("built-ins/Array/prototype/reduceRight/15.4.4.22-9-c-i-15.js",False)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.Array_prototype_reduceRight("built-ins/Array/prototype/reduceRight/15.4.4.22-9-c-i-15.js",True)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.Array_prototype_reduceRight("built-ins/Array/prototype/reduceRight/15.4.4.22-9-c-i-16.js",False)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.Array_prototype_reduceRight("built-ins/Array/prototype/reduceRight/15.4.4.22-9-c-ii-12.js",True)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.Array_prototype_reduceRight("built-ins/Array/prototype/reduceRight/15.4.4.22-9-c-ii-13.js",False)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.Array_prototype_reduceRight("built-ins/Array/prototype/reduceRight/15.4.4.22-9-c-ii-13.js",True)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.Array_prototype_reduceRight("built-ins/Array/prototype/reduceRight/15.4.4.22-9-c-ii-14.js",False)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.Array_prototype_reduceRight("built-ins/Array/prototype/reduceRight/15.4.4.22-9-c-ii-14.js",True)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.Array_prototype_reduceRight("built-ins/Array/prototype/reduceRight/15.4.4.22-9-c-ii-16.js",False)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.Array_prototype_reduceRight("built-ins/Array/prototype/reduceRight/15.4.4.22-9-c-ii-16.js",True)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.Array_prototype_reduceRight("built-ins/Array/prototype/reduceRight/15.4.4.22-9-c-ii-17.js",False)

---
## Diagnosis (2026-01-22)

**Summary:** Array length shrink during reduceRight throws TypeError in non-strict mode instead of failing silently when non-configurable elements block deletion.

**Error Pattern:**
```
Asynkron.JsEngine.ThrowSignal: Unhandled JavaScript throw: 'TypeError': 'Invalid array length'
built-ins/Array/prototype/reduceRight/15.4.4.22-9-b-16.js
built-ins/Array/prototype/reduceRight/15.4.4.22-9-b-29.js
```

**Analysis:**
Both failures are noStrict tests where a getter on index 3 sets `arr.length = 2` while index 2 is non-configurable. Per spec, ArraySetLength should attempt deletions, stop on the non-configurable property, set length to index+1, and return false without throwing in sloppy mode. The engine throws a TypeError "Invalid array length", aborting reduceRight before visiting index 2. This indicates length assignment uses a throw-on-failure path even when the current scope is non-strict.

**Fix Direction:**
When assigning to `length` through normal property set (e.g., `JsOps.TryAssignArrayLikeValueJsValue`), pass `throwOnWritableFailure: context?.CurrentScope.IsStrict == true` (or equivalent) into `JsArray.SetLength`. Ensure `TryShrinkLength` failures only throw in strict mode; in sloppy mode they should fail silently and leave length at `index + 1`, allowing reduceRight to continue and observe non-configurable elements.
