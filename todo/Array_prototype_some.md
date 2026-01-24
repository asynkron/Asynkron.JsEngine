# Array_prototype_some

FQN:
`Asynkron.JsEngine.Tests.Test262.BuiltInsTests.Array_prototype_some`

Full test name:

- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.Array_prototype_some("built-ins/Array/prototype/some/15.4.4.17-7-c-ii-2.js",False)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.Array_prototype_some("built-ins/Array/prototype/some/15.4.4.17-7-c-ii-2.js",True)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.Array_prototype_some("built-ins/Array/prototype/some/15.4.4.17-7-c-iii-17.js",True)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.Array_prototype_some("built-ins/Array/prototype/some/15.4.4.17-7-c-iii-19.js",True)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.Array_prototype_some("built-ins/Array/prototype/some/15.4.4.17-7-c-iii-2.js",True)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.Array_prototype_some("built-ins/Array/prototype/some/15.4.4.17-7-c-iii-21.js",False)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.Array_prototype_some("built-ins/Array/prototype/some/15.4.4.17-7-c-iii-21.js",True)

---
## Diagnosis (2026-01-22)

**Summary:** Array length shrink during `some` throws in non-strict mode when a non-configurable element prevents deletion.

**Error Pattern:**
```
Asynkron.JsEngine.ThrowSignal: Unhandled JavaScript throw: 'TypeError': 'Invalid array length'
built-ins/Array/prototype/some/15.4.4.17-7-b-16.js
```

**Analysis:**
`15.4.4.17-7-b-16.js` defines a non-configurable element at index 2, then the getter for index 1 executes `arr.length = 2` during iteration. Per spec, shrinking length should fail silently in non-strict mode when deletion of a non-configurable element is blocked, leaving the length at index+1 and keeping the element. `Array.prototype.some` captures the initial length and should still visit index 2, returning true. The engine instead throws `TypeError: Invalid array length`, indicating the length setter is throwing even in sloppy mode when it should return `false` without exception.

**Fix Direction:**
Ensure the array `length` setter follows `ArraySetLength` semantics: when deletion of a non-configurable element fails, set length to `failedIndex + 1` and return `false`; only throw if the `[[Set]]`/`DefineOwnProperty` call has `Throw` = true (strict mode). This should prevent exceptions in non-strict callers like this test and allow `some` to keep iterating with the initial length.

---
## Re-validation (2026-01-24)

Re-ran:
```bash
dotnet test tests/Asynkron.JsEngine.Tests.Test262 -c Release --filter "Name=Array_prototype_some" -v n
```

Result: `Total tests: 434` / `Passed: 434`

** DONE **
