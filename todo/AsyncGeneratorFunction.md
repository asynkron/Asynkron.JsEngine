# AsyncGeneratorFunction

FQN:
`Asynkron.JsEngine.Tests.Test262.BuiltInsTests.AsyncGeneratorFunction`

Full test name:

- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.AsyncGeneratorFunction("built-ins/AsyncGeneratorFunction/instance-await-expr-in-param.js",False)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.AsyncGeneratorFunction("built-ins/AsyncGeneratorFunction/instance-await-expr-in-param.js",True)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.AsyncGeneratorFunction("built-ins/AsyncGeneratorFunction/instance-construct-throws.js",False)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.AsyncGeneratorFunction("built-ins/AsyncGeneratorFunction/instance-construct-throws.js",True)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.AsyncGeneratorFunction("built-ins/AsyncGeneratorFunction/instance-yield-expr-in-param.js",False)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.AsyncGeneratorFunction("built-ins/AsyncGeneratorFunction/instance-yield-expr-in-param.js",True)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.AsyncGeneratorFunction("built-ins/AsyncGeneratorFunction/proto-from-ctor-realm-prototype.js",False)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.AsyncGeneratorFunction("built-ins/AsyncGeneratorFunction/proto-from-ctor-realm-prototype.js",True)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.AsyncGeneratorFunction("built-ins/AsyncGeneratorFunction/proto-from-ctor-realm.js",False)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.AsyncGeneratorFunction("built-ins/AsyncGeneratorFunction/proto-from-ctor-realm.js",True)

---
## Diagnosis (2026-01-22)

**Summary:** AsyncGeneratorFunction dynamic creation misses spec checks (await/yield in params, non-constructable) and cross-realm prototype selection uses the wrong realm.

**Error Pattern:**
```
Asynkron.JsEngine.ThrowSignal: Unhandled JavaScript throw:
   at Asynkron.JsEngine.Ast.TypedAstEvaluator.EvaluateProgramJsValueCore(...ProgramNodeExtensions.cs:618)
```

**Analysis:**
The failing tests all end up throwing a Test262 assertion error (surfacing as an unhandled JS throw) because the engine does not match the spec behavior:
- `instance-construct-throws`: async generator function instances are treated as constructors, so `new instance()` does not throw a `TypeError`.
- `instance-await-expr-in-param` / `instance-yield-expr-in-param`: `AsyncGeneratorFunction` accepts `await`/`yield` in parameter lists, but the spec requires a `SyntaxError` early error.
- `proto-from-ctor-realm*`: `Reflect.construct` resolves the function's `[[Prototype]]` using the constructor's realm fallback because the newTarget realm lookup does not map `AsyncGeneratorFunction`, so the function and/or its `prototype` chain come from the wrong realm.

**Fix Direction:**
Ensure async generator functions are non-constructable, enforce early errors for `await`/`yield` in async generator parameter lists, and extend `GetPrototypeFromConstructor`/`ResolveConstructPrototype` to resolve `AsyncGeneratorFunction.prototype` from the newTarget's realm (and verify the created `prototype` object uses the constructor's realm).
