# Object_prototype___proto__

FQN:
`Asynkron.JsEngine.Tests.Test262.BuiltInsTests.Object_prototype___proto__`

Full test name:

- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.Object_prototype___proto__("built-ins/Object/prototype/__proto__/set-abrupt.js",False)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.Object_prototype___proto__("built-ins/Object/prototype/__proto__/set-abrupt.js",True)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.Object_prototype___proto__("built-ins/Object/prototype/__proto__/set-cycle.js",False)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.Object_prototype___proto__("built-ins/Object/prototype/__proto__/set-cycle.js",True)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.Object_prototype___proto__("built-ins/Object/prototype/__proto__/set-immutable.js",False)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.Object_prototype___proto__("built-ins/Object/prototype/__proto__/set-immutable.js",True)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.Object_prototype___proto__("built-ins/Object/prototype/__proto__/set-non-extensible.js",False)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.Object_prototype___proto__("built-ins/Object/prototype/__proto__/set-non-extensible.js",True)

---
## Diagnosis (2026-02-03)

**Summary:** `Object.prototype.__proto__` setter was swallowing abrupt completions and skipping non-extensible checks; proxy traps saw `Object.prototype.getPrototypeOf` stub from compat data, causing incorrect trap behavior.

**Fixes:**
- Propagate abrupt completions from proxy traps; only convert `[[SetPrototypeOf]]` false to `TypeError`.
- Enforce non-extensible `[[SetPrototypeOf]]` failure in `JsObject.SetPrototype`.
- Reject prototype cycles in `__proto__` setter.
- Remove non-standard `getPrototypeOf/setPrototypeOf/proto` from `stdlib-compat.json` so Object.prototype doesn't expose them.

**Tests:**
`dotnet test tests/Asynkron.JsEngine.Tests.Test262 -c Release --settings tests/Asynkron.JsEngine.Tests.Test262/BuiltInsTests.runsettings --filter "Name=Object_prototype___proto__"`

** DONE **
