# ConstructorsTaintObjectPrototype

FQN:
`Asynkron.JsEngine.Tests.Test262.Intl402Tests.ConstructorsTaintObjectPrototype`

Full test name:

- Asynkron.JsEngine.Tests.Test262.Intl402Tests.ConstructorsTaintObjectPrototype("intl402/constructors-taint-Object-prototype.js",False)
- Asynkron.JsEngine.Tests.Test262.Intl402Tests.ConstructorsTaintObjectPrototype("intl402/constructors-taint-Object-prototype.js",True)

---
## Diagnosis (2026-01-22)

**Summary:** Intl `resolvedOptions()` builds result objects via `SetProperty`, so Object.prototype tainted setters for `locale`/`extension`/`extensionIndex` fire and throw.

**Error Pattern:**
```
intl402/constructors-taint-Object-prototype.js
Asynkron.JsEngine.ThrowSignal: Unhandled JavaScript throw:
```

**Analysis:**
`taintProperties` in `harness/testIntl.js` installs throwing setters on `Object.prototype` for
`locale`, `extension`, and `extensionIndex` (plus variants). When Intl constructors run and the
test calls `resolvedOptions()`, the implementation creates a `JsObject` and assigns properties
like `locale` using `SetProperty`. That path honors prototype setters, so the `Object.prototype`
setter executes instead of defining an own data property, which throws a `Test262Error`. The
spec expects these internal records and resolvedOptions objects to be created with own data
properties (CreateDataProperty/DefineProperty) and to be immune to prototype chain tainting.

**Fix Direction:**
Use `DefineProperty`/CreateDataProperty-style writes when populating Intl resolvedOptions and
any internal "record" objects so prototype setters are not invoked. Alternatively, create
internal records with a null prototype and then define data properties, while keeping the
resolvedOptions return value as an ordinary object with own properties.
