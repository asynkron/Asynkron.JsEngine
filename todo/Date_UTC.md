# Date_UTC

FQN:
`Asynkron.JsEngine.Tests.Test262.BuiltInsTests.Date_UTC`

Full test name:

- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.Date_UTC("built-ins/Date/UTC/coercion-errors.js",False)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.Date_UTC("built-ins/Date/UTC/coercion-errors.js",True)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.Date_UTC("built-ins/Date/UTC/coercion-order.js",False)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.Date_UTC("built-ins/Date/UTC/coercion-order.js",True)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.Date_UTC("built-ins/Date/UTC/fp-evaluation-order.js",False)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.Date_UTC("built-ins/Date/UTC/fp-evaluation-order.js",True)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.Date_UTC("built-ins/Date/UTC/non-integer-values.js",False)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.Date_UTC("built-ins/Date/UTC/non-integer-values.js",True)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.Date_UTC("built-ins/Date/UTC/overflow-make-day.js",False)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.Date_UTC("built-ins/Date/UTC/overflow-make-day.js",True)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.Date_UTC("built-ins/Date/UTC/overflow-make-time.js",False)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.Date_UTC("built-ins/Date/UTC/overflow-make-time.js",True)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.Date_UTC("built-ins/Date/UTC/time-clip.js",False)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.Date_UTC("built-ins/Date/UTC/time-clip.js",True)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.Date_UTC("built-ins/Date/UTC/year-offset.js",False)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.Date_UTC("built-ins/Date/UTC/year-offset.js",True)

---
## Diagnosis (2026-02-03)

**Summary:** `Date.UTC` used fast-path numeric casts and `DateTime`, which skipped ToNumber coercion order, failed to throw on conversion errors, and couldn't represent extended years.

**Fixes:**
- Implemented spec algorithm using sequential `ToNumber`, `MakeFullYear`, `MakeDay`, `MakeTime`, and `TimeClip`.
- Removed `DateTime` dependency to support extended year ranges and correct coercion order.

**Tests:**
`dotnet test tests/Asynkron.JsEngine.Tests.Test262 -c Release --settings tests/Asynkron.JsEngine.Tests.Test262/BuiltInsTests.runsettings --filter "Name=Date_UTC"`

** DONE **
