# Date_prototype_toTemporalInstant

FQN:
`Asynkron.JsEngine.Tests.Test262.BuiltInsTests.Date_prototype_toTemporalInstant`

Full test name:

- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.Date_prototype_toTemporalInstant("built-ins/Date/prototype/toTemporalInstant/this-value-valid-date.js",False)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.Date_prototype_toTemporalInstant("built-ins/Date/prototype/toTemporalInstant/this-value-valid-date.js",True)

---
## Diagnosis (2026-02-03)

**Summary:** `Date.prototype.toTemporalInstant` failed because Temporal prototypes were not initialized for the realm when called from Date.

**Fixes:**
- Lazily initialize Temporal prototypes when missing so `CreateInstantFromEpochMilliseconds` can wrap the result.
- Implement `Temporal.Instant.prototype.epochNanoseconds` to expose the created instant.

**Tests:**
`dotnet test tests/Asynkron.JsEngine.Tests.Test262 -c Release --settings tests/Asynkron.JsEngine.Tests.Test262/BuiltInsTests.runsettings --filter "Name=Date_prototype_toTemporalInstant|Name=Temporal_Instant_prototype_epochNanoseconds"`

** DONE **
