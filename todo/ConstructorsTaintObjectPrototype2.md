# ConstructorsTaintObjectPrototype2

FQN:
`Asynkron.JsEngine.Tests.Test262.Intl402Tests.ConstructorsTaintObjectPrototype2`

Full test name:

- Asynkron.JsEngine.Tests.Test262.Intl402Tests.ConstructorsTaintObjectPrototype2("intl402/constructors-taint-Object-prototype-2.js",False)
- Asynkron.JsEngine.Tests.Test262.Intl402Tests.ConstructorsTaintObjectPrototype2("intl402/constructors-taint-Object-prototype-2.js",True)

---
## Diagnosis (2026-01-22)

**Summary:** Intl resolvedOptions/internal locale records are built with ordinary objects using `SetProperty`, so tainted `Object.prototype` setters for `locale`/`nu`/`ca`/`co`/`dataLocale` are invoked and throw.

**Error Pattern:**
```
intl402/constructors-taint-Object-prototype-2.js
Asynkron.JsEngine.ThrowSignal: Unhandled JavaScript throw:
```

**Analysis:**
The test calls `taintProperties(...)`, which installs throwing setters on `Object.prototype` for `locale`, `dataLocale`, `nu`, `ca`, `co` (and variants). When `new Intl.*(...).resolvedOptions()` runs, the engine constructs the result and/or internal locale records via `JsObject.SetProperty` on normal objects. `SetProperty` honors prototype setters (`GetSetter`), so the write hits the tainted `Object.prototype` setter and throws a `Test262Error`. Per spec, internal Records must not be affected by `Object.prototype`, and `resolvedOptions` should use `CreateDataProperty` semantics (own data properties) rather than `[[Set]]`.

**Fix Direction:**
Use `CreateDataPropertyOrThrow`/`DefineProperty` when populating `resolvedOptions` and any internal locale records (or construct records with a null prototype) so writes bypass `Object.prototype` setters. This should be applied across Intl constructors that set `locale`/`nu`/`ca`/`co`/`dataLocale` during initialization or in `resolvedOptions`.
