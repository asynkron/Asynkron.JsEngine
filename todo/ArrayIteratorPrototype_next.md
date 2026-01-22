# ArrayIteratorPrototype_next

FQN:
`Asynkron.JsEngine.Tests.Test262.BuiltInsTests.ArrayIteratorPrototype_next`

Full test name:

- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.ArrayIteratorPrototype_next("built-ins/ArrayIteratorPrototype/next/detach-typedarray-in-progress.js",False)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.ArrayIteratorPrototype_next("built-ins/ArrayIteratorPrototype/next/detach-typedarray-in-progress.js",True)

---
## Diagnosis (2026-01-22)

**Summary:** Array iterator over typed arrays does not throw TypeError after buffer detachment; instead an internal IndexOutOfRangeException escapes.

**Error Pattern:**
```
System.IndexOutOfRangeException: Index was outside the bounds of the array.
   at Asynkron.JsEngine.Ast.TypedAstEvaluator.ExecutionPlanRunner.ExecutePlan(ResumeMode mode, JsValue resumeValue) in src/Asynkron.JsEngine/Ast/TypedAstEvaluator.ExecutionPlanRunner.Loop.cs:line 162
```

**Analysis:**
`detach-typedarray-in-progress.js` expects `%ArrayIteratorPrototype%.next` to throw a TypeError when a TypedArray’s buffer is detached during iteration (spec step 8.a). The test detaches `typedArray.buffer` inside `for (let key of typedArray.keys())` and asserts a TypeError plus only one iteration. In both strict and non-strict runs, the engine instead throws a .NET `IndexOutOfRangeException` from the IR execution loop, indicating the typed array detachment path is not surfaced as a JS TypeError and an internal out-of-bounds access occurs.

**Fix Direction:**
Ensure typed-array iterators check `IsDetachedOrOutOfBounds()` on every `next()` call and throw `CreateOutOfBoundsTypeError()` (ThrowSignal) before any element access. Verify no optimized iteration path (array iterator reuse, pooled enumerators) bypasses this check for `typedArray.keys()`/`values()`/`entries()` so detachment produces a JS TypeError instead of a runtime IndexOutOfRange.
