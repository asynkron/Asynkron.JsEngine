# Temporal_ZonedDateTime_prototype_since

Status: 182/200 passing (18 remaining = 4 BuiltIns + 14 intl402)

FQNs:

- `Asynkron.JsEngine.Tests.Test262.BuiltInsTests.Temporal_ZonedDateTime_prototype_since`
- `Asynkron.JsEngine.Tests.Test262.Intl402Tests.Temporal_ZonedDateTime_prototype_since`

Remaining BuiltIns failures (4):

- argument-string-limits.js × 2 (extreme date range — DateTimeOffset overflow)
- roundingincrement-addition-out-of-range.js × 2 (NudgeToCalendarUnit range check missing)

Remaining intl402 failures (14): DST balancing/rounding, sub-minute offsets, calendar
