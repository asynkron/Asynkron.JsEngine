# Temporal_ZonedDateTime_prototype_withPlainTime

Status: 64/70 passing (6 remaining = 2 intl402 + 4 extreme range/DST edge cases)

FQNs:

- `Asynkron.JsEngine.Tests.Test262.BuiltInsTests.Temporal_ZonedDateTime_prototype_withPlainTime`
- `Asynkron.JsEngine.Tests.Test262.Intl402Tests.Temporal_ZonedDateTime_prototype_withPlainTime`

Remaining failures:

- throws-if-epoch-nanoseconds-outside-valid-limits.js × 2 (DateTimeOffset overflow for extreme epochs)
- get-start-of-day-throws.js × 2 (DST start-of-day computation)
- intl402/dst-skipped-cross-midnight.js × 2
