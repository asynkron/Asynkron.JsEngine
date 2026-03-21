# Date_parse

FQN:
`Asynkron.JsEngine.Tests.Test262.BuiltInsTests.Date_parse`

Full test name:

- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.Date_parse("built-ins/Date/parse/time-value-maximum-range.js",False)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.Date_parse("built-ins/Date/parse/time-value-maximum-range.js",True)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.Date_parse("built-ins/Date/parse/without-utc-offset.js",False)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.Date_parse("built-ins/Date/parse/without-utc-offset.js",True)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.Date_parse("built-ins/Date/parse/zero.js",False)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.Date_parse("built-ins/Date/parse/zero.js",True)

---
## Diagnosis (2026-01-22)

**Summary:** Date.parse relies on DateTimeOffset.TryParse, which does not follow ECMAScript parsing rules for offsetless ISO strings, extended years, or Date.prototype string formats.

**Error Pattern:**
```
Asynkron.JsEngine.ThrowSignal: Unhandled JavaScript throw: Expected SameValue(-3600000, -0) to be true
Asynkron.JsEngine.ThrowSignal: Unhandled JavaScript throw: (empty message for zero.js/time-value-maximum-range.js)
```

**Analysis:**
The failing tests all hit Date.parse semantics that diverge from .NET parsing. In without-utc-offset.js, Date.parse("1970-01-01T00:00:00") returns 0 (UTC) instead of local time offset, so offsetless date-time strings are treated as UTC instead of local time. In time-value-maximum-range.js, Date.parse must accept extended year ISO strings like "-271821-04-20T00:00:00.000Z" and "+275760-09-13T00:00:00.000Z" and return +/-8640000000000000, but DateTimeOffset.TryParse cannot parse those years and the DateTimeOffset-based formatting clamps years to 0001-9999. In zero.js, Date.parse should accept the outputs of Date.prototype.toString/toUTCString/toISOString; the current parser does not reliably parse the engine's toString/toUTCString format (timezone name in parentheses, GMT offset), so assertions fail.

**Fix Direction:**
Replace Date.parse with a spec-compliant parser for the ECMAScript Date Time String Format (including signed 6-digit years), and explicitly implement the rule that offsetless date-time strings are local time while date-only strings are UTC. Avoid DateTimeOffset for parse/format in the extended year range; implement custom date math/formatting for toISOString and parsing of Date.prototype.toString/toUTCString outputs so Date.parse round-trips those formats.

** DONE **
