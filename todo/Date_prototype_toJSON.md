# Date_prototype_toJSON

FQN:
`Asynkron.JsEngine.Tests.Test262.BuiltInsTests.Date_prototype_toJSON`

Full test name:

- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.Date_prototype_toJSON("built-ins/Date/prototype/toJSON/invoke-abrupt.js",False)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.Date_prototype_toJSON("built-ins/Date/prototype/toJSON/invoke-abrupt.js",True)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.Date_prototype_toJSON("built-ins/Date/prototype/toJSON/invoke-arguments.js",False)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.Date_prototype_toJSON("built-ins/Date/prototype/toJSON/invoke-arguments.js",True)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.Date_prototype_toJSON("built-ins/Date/prototype/toJSON/invoke-result.js",False)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.Date_prototype_toJSON("built-ins/Date/prototype/toJSON/invoke-result.js",True)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.Date_prototype_toJSON("built-ins/Date/prototype/toJSON/non-finite.js",False)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.Date_prototype_toJSON("built-ins/Date/prototype/toJSON/non-finite.js",True)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.Date_prototype_toJSON("built-ins/Date/prototype/toJSON/to-object.js",False)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.Date_prototype_toJSON("built-ins/Date/prototype/toJSON/to-object.js",True)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.Date_prototype_toJSON("built-ins/Date/prototype/toJSON/to-primitive-abrupt.js",False)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.Date_prototype_toJSON("built-ins/Date/prototype/toJSON/to-primitive-abrupt.js",True)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.Date_prototype_toJSON("built-ins/Date/prototype/toJSON/to-primitive-symbol.js",False)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.Date_prototype_toJSON("built-ins/Date/prototype/toJSON/to-primitive-symbol.js",True)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.Date_prototype_toJSON("built-ins/Date/prototype/toJSON/to-primitive-value-of.js",False)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.Date_prototype_toJSON("built-ins/Date/prototype/toJSON/to-primitive-value-of.js",True)

---
## Diagnosis (2026-02-03)

**Summary:** `Date.prototype.toJSON` was incorrectly restricted to Date instances and bypassed the `ToPrimitive`/`toISOString` algorithm. This broke generic usage and threw on non-Date receivers.

**Fixes:**
- Implement the spec algorithm: `ToObject`, `ToPrimitive` (Number), return `null` for non-finite numbers, then `Get`/call `toISOString`.

**Tests:**
`dotnet test tests/Asynkron.JsEngine.Tests.Test262 -c Release --settings tests/Asynkron.JsEngine.Tests.Test262/BuiltInsTests.runsettings --filter "Name=Date_prototype_toJSON"`

** DONE **
