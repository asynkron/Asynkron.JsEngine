# Error

FQN:
`Asynkron.JsEngine.Tests.Test262.BuiltInsTests.Error`

Full test name:

- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.Error("built-ins/Error/cause_abrupt.js",False)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.Error("built-ins/Error/cause_abrupt.js",True)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.Error("built-ins/Error/proto-from-ctor-realm.js",False)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.Error("built-ins/Error/proto-from-ctor-realm.js",True)

---
## Diagnosis (2026-02-03)

**Summary:** Fixed Error `cause` option handling to follow spec `HasProperty` + `Get`, and resolved Error prototype lookup from the constructor's realm global to satisfy cross-realm Error construction.

**Fixes:**
- `InstallErrorCause` now checks `HasProperty(options, "cause")` before `Get`, matching spec behavior and ensuring abrupt completions propagate correctly.
- `TryResolveRealmDefaultPrototype` now looks up the default prototype in the constructor's realm global, not the current realm, so `Error` subclasses created in other realms use the correct prototype.

**Tests:**
`dotnet test tests/Asynkron.JsEngine.Tests.Test262 -c Release --settings tests/Asynkron.JsEngine.Tests.Test262/BuiltInsTests.runsettings --filter "Name=Error"`

**DONE**
