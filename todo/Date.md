# Date

FQN:
`Asynkron.JsEngine.Tests.Test262.BuiltInsTests.Date`

Full test name:

- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.Date("built-ins/Date/15.9.1.15-1.js",False)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.Date("built-ins/Date/15.9.1.15-1.js",True)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.Date("built-ins/Date/TimeClip_negative_zero.js",False)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.Date("built-ins/Date/TimeClip_negative_zero.js",True)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.Date("built-ins/Date/construct_with_date.js",False)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.Date("built-ins/Date/construct_with_date.js",True)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.Date("built-ins/Date/value-symbol-to-prim-invocation.js",False)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.Date("built-ins/Date/value-symbol-to-prim-invocation.js",True)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.Date("built-ins/Date/value-symbol-to-prim-return-obj.js",False)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.Date("built-ins/Date/value-symbol-to-prim-return-obj.js",True)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.Date("built-ins/Date/value-symbol-to-prim-return-prim.js",False)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.Date("built-ins/Date/value-symbol-to-prim-return-prim.js",True)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.Date("built-ins/Date/value-to-primitive-call.js",False)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.Date("built-ins/Date/value-to-primitive-call.js",True)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.Date("built-ins/Date/value-to-primitive-get-meth-err.js",False)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.Date("built-ins/Date/value-to-primitive-get-meth-err.js",True)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.Date("built-ins/Date/value-to-primitive-result-faulty.js",False)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.Date("built-ins/Date/value-to-primitive-result-faulty.js",True)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.Date("built-ins/Date/value-to-primitive-result-string.js",False)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.Date("built-ins/Date/value-to-primitive-result-string.js",True)

---
## Diagnosis (2026-02-03)

**Summary:** `new Date(dateObj)` was coercing via `ToPrimitive`, calling `toString`/`valueOf` and `Symbol.toPrimitive` instead of reading the internal [[DateValue]] slot directly.

**Fixes:**
- When a single argument is a Date object with an internal date slot, return that time value without invoking `ToPrimitive`.

**Tests:**
`dotnet test tests/Asynkron.JsEngine.Tests.Test262 -c Release --settings tests/Asynkron.JsEngine.Tests.Test262/BuiltInsTests.runsettings --filter "Name=Date"`

** DONE **
