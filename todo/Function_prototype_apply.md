# Function_prototype_apply

FQN:
`Asynkron.JsEngine.Tests.Test262.BuiltInsTests.Function_prototype_apply`

Full test name:

- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.Function_prototype_apply("built-ins/Function/prototype/apply/argarray-not-object-realm.js",False)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.Function_prototype_apply("built-ins/Function/prototype/apply/argarray-not-object-realm.js",True)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.Function_prototype_apply("built-ins/Function/prototype/apply/argarray-not-object.js",False)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.Function_prototype_apply("built-ins/Function/prototype/apply/argarray-not-object.js",True)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.Function_prototype_apply("built-ins/Function/prototype/apply/this-not-callable-realm.js",False)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.Function_prototype_apply("built-ins/Function/prototype/apply/this-not-callable-realm.js",True)

---
## Diagnosis (2026-02-03)

**Summary:** `Function.prototype.apply` must throw for non-object `argArray` (per `CreateListFromArrayLike`) and use the realm of the apply function when throwing in cross-realm scenarios.

**Fixes:**
- Require `argArray` to be an object (`IJsPropertyAccessor`) instead of boxing primitives.
- Use the prototype instance realm for TypeError creation (apply is now instance-based for proper realm access).

**Tests:**
`dotnet test tests/Asynkron.JsEngine.Tests.Test262 -c Release --settings tests/Asynkron.JsEngine.Tests.Test262/BuiltInsTests.runsettings --filter "Name=Function_prototype_apply"`

** DONE **
