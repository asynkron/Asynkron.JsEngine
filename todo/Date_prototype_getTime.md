# Date_prototype_getTime

FQN:
`Asynkron.JsEngine.Tests.Test262.BuiltInsTests.Date_prototype_getTime`

Full test name:

- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.Date_prototype_getTime("built-ins/Date/prototype/getTime/this-value-valid-date.js",False)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.Date_prototype_getTime("built-ins/Date/prototype/getTime/this-value-valid-date.js",True)

---
## Diagnosis (2026-01-22)

**Summary:** `Date.prototype.getTime` returns `-0` for `new Date(-0)`; spec/tests expect `+0`.

**Error Pattern:**
```
Unhandled JavaScript throw: -0 Expected SameValue(«-0», «0») to be true
```

**Analysis:**
Both strict and non-strict variants of `this-value-valid-date.js` fail on
`assert.sameValue(new Date(-0).getTime(), 0, '-0');`. The engine preserves
negative zero in the internal date value, so `getTime()` returns `-0` and
`SameValue(-0, 0)` is false. `DateHelper.TimeClip` currently uses
`Math.Truncate(time)`, which keeps the sign of `-0`, and `_internalDate` is
stored with that value.

**Fix Direction:**
Normalize negative zero to positive zero when time values are clipped/stored or
when `getTime()` returns the internal value. The most central fix is in
`DateHelper.TimeClip` to canonicalize `-0` to `+0` after truncation (e.g.,
`if (result == 0d) result = 0d;` or a `1/result` sign check). Alternatively,
normalize in `StoreInternalDateValue` or `DatePrototype.getTime` before
returning.
