# Object_prototype_isPrototypeOf

FQN:
`Asynkron.JsEngine.Tests.Test262.BuiltInsTests.Object_prototype_isPrototypeOf`

Full test name:

- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.Object_prototype_isPrototypeOf("built-ins/Object/prototype/isPrototypeOf/arg-is-proxy.js",False)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.Object_prototype_isPrototypeOf("built-ins/Object/prototype/isPrototypeOf/arg-is-proxy.js",True)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.Object_prototype_isPrototypeOf("built-ins/Object/prototype/isPrototypeOf/null-this-and-primitive-arg-returns-false.js",False)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.Object_prototype_isPrototypeOf("built-ins/Object/prototype/isPrototypeOf/null-this-and-primitive-arg-returns-false.js",True)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.Object_prototype_isPrototypeOf("built-ins/Object/prototype/isPrototypeOf/undefined-this-and-primitive-arg-returns-false.js",False)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.Object_prototype_isPrototypeOf("built-ins/Object/prototype/isPrototypeOf/undefined-this-and-primitive-arg-returns-false.js",True)

---
## Diagnosis (2026-02-03)

**Summary:** `Object.prototype.isPrototypeOf` threw on null/undefined `this` before checking non-object `V`, and proxy targets failed because proxy `getPrototypeOf` results were restricted to `JsObject`.

**Fixes:**
- Perform the `V` object check before `ToObject(this)` so non-objects return `false` without throwing.
- In proxy `getPrototypeOf` trap handling, accept any object-like result (including arrays).
- When `V` is a proxy, use its `getPrototypeOf` trap in the prototype walk.

**Tests:**
`dotnet test tests/Asynkron.JsEngine.Tests.Test262 -c Release --settings tests/Asynkron.JsEngine.Tests.Test262/BuiltInsTests.runsettings --filter "Name=Object_prototype_isPrototypeOf"`

** DONE **
