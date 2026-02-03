# WeakRef_prototype_deref

FQN:
`Asynkron.JsEngine.Tests.Test262.BuiltInsTests.WeakRef_prototype_deref`

Full test name:

- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.WeakRef_prototype_deref("built-ins/WeakRef/prototype/deref/return-symbol-target.js",False)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.WeakRef_prototype_deref("built-ins/WeakRef/prototype/deref/return-symbol-target.js",True)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.WeakRef_prototype_deref("built-ins/WeakRef/prototype/deref/this-does-not-have-internal-target-throws.js",False)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.WeakRef_prototype_deref("built-ins/WeakRef/prototype/deref/this-does-not-have-internal-target-throws.js",True)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.WeakRef_prototype_deref("built-ins/WeakRef/prototype/deref/this-not-object-throws.js",False)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.WeakRef_prototype_deref("built-ins/WeakRef/prototype/deref/this-not-object-throws.js",True)

---
## Diagnosis (2026-02-03)

**Summary:** WeakRef now accepts symbol targets, throws proper TypeErrors for non-WeakRef receivers, and FinalizationRegistry is registered globally so deref tests can construct it. Fixed deref to avoid invalid casts on non-object receivers.

**Fixes:**
- Allow symbol targets in `WeakRef` constructor.
- Use `TryGetObject<JsObject>` in `WeakRef.prototype.deref` to avoid invalid cast and throw TypeError for non-WeakRef receivers.
- Register `FinalizationRegistry` global (stub constructor) so tests can instantiate it.

**Tests:**
`dotnet test tests/Asynkron.JsEngine.Tests.Test262 -c Release --settings tests/Asynkron.JsEngine.Tests.Test262/BuiltInsTests.runsettings --filter "Name=WeakRef_prototype_deref"`

** DONE **
