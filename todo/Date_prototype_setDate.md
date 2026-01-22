# Date_prototype_setDate

FQN:
`Asynkron.JsEngine.Tests.Test262.BuiltInsTests.Date_prototype_setDate`

Full test name:

- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.Date_prototype_setDate("built-ins/Date/prototype/setDate/date-value-read-before-tonumber-when-date-is-invalid.js",False)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.Date_prototype_setDate("built-ins/Date/prototype/setDate/date-value-read-before-tonumber-when-date-is-invalid.js",True)

---
## Diagnosis (2026-01-22)

**Summary:** `Date.prototype.setDate` stores a NaN result back onto the date even when the original [[DateValue]] was NaN, overwriting side effects from argument coercion.

**Error Pattern:**
```
built-ins/Date/prototype/setDate/date-value-read-before-tonumber-when-date-is-invalid.js
Asynkron.JsEngine.ThrowSignal: Unhandled JavaScript throw:
```

**Analysis:**
The failing test sets a Date with [[DateValue]] = NaN, then calls `setDate` with an argument whose `valueOf` mutates the same Date via `setTime(0)`. The spec order is: read [[DateValue]] into `t`, then perform `ToNumber(date)`, then return NaN if `t` is NaN without writing a new value. The current `setDate` implementation reads `timeValue` and still computes/clips a new date, then calls `StoreInternalDateValue(obj, clipped)` even when `timeValue` is NaN. That write overwrites the `setTime(0)` side effect, so the assertion `dt.getTime() === 0` fails in both strict and non-strict modes.

**Fix Direction:**
In `DatePrototype.SetDate` (`src/Asynkron.JsEngine/StdLib/Date/DatePrototype.cs`), keep the current order (read `timeValue`, call `ToNumber`), but if the original `timeValue` is NaN then return NaN immediately and skip `StoreInternalDateValue`. This preserves side effects from `ToNumber` while matching spec behavior.
