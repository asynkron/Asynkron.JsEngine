# Array_prototype_toString

FQN:
`Asynkron.JsEngine.Tests.Test262.BuiltInsTests.Array_prototype_toString`

Full test name:

- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.Array_prototype_toString("built-ins/Array/prototype/toString/non-callable-join-string-tag.js",False)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.Array_prototype_toString("built-ins/Array/prototype/toString/non-callable-join-string-tag.js",True)

---
## Diagnosis (2026-01-22)

**Summary:** `Object.defineProperty` rejects redefining `Object.prototype[Symbol.toStringTag]`, throwing as if it were a read-only assignment.

**Error Pattern:**
```
Asynkron.JsEngine.ThrowSignal: Unhandled JavaScript throw: 'TypeError': 'Cannot assign to read only property '@@symbol:1'.'
   at Asynkron.JsEngine.Ast.AssignmentReferenceResolver.AssignObjectProperty(...)
   at Asynkron.JsEngine.Ast.TypedAstEvaluator.AssignPropertyValueWithNullCheckCore(...)
```

**Analysis:**
The failing Test262 script reaches `Object.defineProperty(Object.prototype, Symbol.toStringTag, { get: ... })` (line 68). In spec terms, `Object.prototype[Symbol.toStringTag]` is configurable, so redefining it with an accessor should succeed. Instead, the engine throws a TypeError from the normal assignment path, treating the symbol property as a read-only data property. That aborts the test before the final `Array.prototype.toString.call({})` assertion. This happens in both strict and non-strict runs, indicating `defineProperty` is routed through a writable check or `Set` path that should not be used for `DefineProperty`.

**Fix Direction:**
Ensure `Object.defineProperty` (and any internal `DefineOwnProperty` helpers) use proper property descriptor semantics for symbol keys. Redefining a configurable non-writable property like `Object.prototype[Symbol.toStringTag]` should succeed and not hit `AssignObjectProperty`. Audit the symbol-property define path to avoid throwing on configurable properties and to keep strict-mode-only errors confined to actual assignment (`[[Set]]`) operations.
