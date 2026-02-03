# Object_prototype_hasOwnProperty

FQN:
`Asynkron.JsEngine.Tests.Test262.BuiltInsTests.Object_prototype_hasOwnProperty`

Full test name:

- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.Object_prototype_hasOwnProperty("built-ins/Object/prototype/hasOwnProperty/topropertykey_before_toobject.js",False)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.Object_prototype_hasOwnProperty("built-ins/Object/prototype/hasOwnProperty/topropertykey_before_toobject.js",True)

---
## Diagnosis (2026-02-03)

**Summary:** `Object.prototype.hasOwnProperty` must call `ToPropertyKey` before `ToObject` on the receiver. The implementation coerced `this` first, violating the required evaluation order.

**Fixes:**
- Compute the property name via `JsOps.ToPropertyName` before `TryGetObject`.

**Tests:**
`dotnet test tests/Asynkron.JsEngine.Tests.Test262 -c Release --settings tests/Asynkron.JsEngine.Tests.Test262/BuiltInsTests.runsettings --filter "Name=Object_prototype_hasOwnProperty"`

** DONE **
