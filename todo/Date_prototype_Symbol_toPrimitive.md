# Date_prototype_Symbol_toPrimitive

FQN:
`Asynkron.JsEngine.Tests.Test262.BuiltInsTests.Date_prototype_Symbol_toPrimitive`

Full test name:

- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.Date_prototype_Symbol_toPrimitive("built-ins/Date/prototype/Symbol.toPrimitive/hint-default-first-invalid.js",False)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.Date_prototype_Symbol_toPrimitive("built-ins/Date/prototype/Symbol.toPrimitive/hint-default-first-invalid.js",True)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.Date_prototype_Symbol_toPrimitive("built-ins/Date/prototype/Symbol.toPrimitive/hint-default-first-non-callable.js",False)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.Date_prototype_Symbol_toPrimitive("built-ins/Date/prototype/Symbol.toPrimitive/hint-default-first-non-callable.js",True)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.Date_prototype_Symbol_toPrimitive("built-ins/Date/prototype/Symbol.toPrimitive/hint-default-first-valid.js",False)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.Date_prototype_Symbol_toPrimitive("built-ins/Date/prototype/Symbol.toPrimitive/hint-default-first-valid.js",True)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.Date_prototype_Symbol_toPrimitive("built-ins/Date/prototype/Symbol.toPrimitive/hint-number-first-invalid.js",False)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.Date_prototype_Symbol_toPrimitive("built-ins/Date/prototype/Symbol.toPrimitive/hint-number-first-invalid.js",True)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.Date_prototype_Symbol_toPrimitive("built-ins/Date/prototype/Symbol.toPrimitive/hint-number-first-non-callable.js",False)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.Date_prototype_Symbol_toPrimitive("built-ins/Date/prototype/Symbol.toPrimitive/hint-number-first-non-callable.js",True)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.Date_prototype_Symbol_toPrimitive("built-ins/Date/prototype/Symbol.toPrimitive/hint-number-first-valid.js",False)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.Date_prototype_Symbol_toPrimitive("built-ins/Date/prototype/Symbol.toPrimitive/hint-number-first-valid.js",True)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.Date_prototype_Symbol_toPrimitive("built-ins/Date/prototype/Symbol.toPrimitive/hint-number-no-callables.js",False)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.Date_prototype_Symbol_toPrimitive("built-ins/Date/prototype/Symbol.toPrimitive/hint-number-no-callables.js",True)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.Date_prototype_Symbol_toPrimitive("built-ins/Date/prototype/Symbol.toPrimitive/hint-string-first-invalid.js",False)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.Date_prototype_Symbol_toPrimitive("built-ins/Date/prototype/Symbol.toPrimitive/hint-string-first-invalid.js",True)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.Date_prototype_Symbol_toPrimitive("built-ins/Date/prototype/Symbol.toPrimitive/hint-string-first-non-callable.js",False)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.Date_prototype_Symbol_toPrimitive("built-ins/Date/prototype/Symbol.toPrimitive/hint-string-first-non-callable.js",True)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.Date_prototype_Symbol_toPrimitive("built-ins/Date/prototype/Symbol.toPrimitive/hint-string-first-valid.js",False)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.Date_prototype_Symbol_toPrimitive("built-ins/Date/prototype/Symbol.toPrimitive/hint-string-first-valid.js",True)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.Date_prototype_Symbol_toPrimitive("built-ins/Date/prototype/Symbol.toPrimitive/prop-desc.js",False)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.Date_prototype_Symbol_toPrimitive("built-ins/Date/prototype/Symbol.toPrimitive/prop-desc.js",True)

---
## Diagnosis (2026-02-03)

**Summary:** `Date.prototype[Symbol.toPrimitive]` was restricted to Date instances and was defined as writable, causing generic usage and descriptor tests to fail.

**Fixes:**
- Allow any object receiver; only non-objects throw `TypeError`.
- Mark `Date.prototype[Symbol.toPrimitive]` as non-writable.
- Use ordinary toPrimitive ordering without requiring Date internal slots.

**Tests:**
`dotnet test tests/Asynkron.JsEngine.Tests.Test262 -c Release --settings tests/Asynkron.JsEngine.Tests.Test262/BuiltInsTests.runsettings --filter "Name=Date_prototype_Symbol_toPrimitive"`

** DONE **
