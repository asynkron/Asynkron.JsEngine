# Temporal_Instant_prototype_epochNanoseconds

FQN:
`Asynkron.JsEngine.Tests.Test262.BuiltInsTests.Temporal_Instant_prototype_epochNanoseconds`

Full test name:

- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.Temporal_Instant_prototype_epochNanoseconds("built-ins/Temporal/Instant/prototype/epochNanoseconds/branding.js",False)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.Temporal_Instant_prototype_epochNanoseconds("built-ins/Temporal/Instant/prototype/epochNanoseconds/branding.js",True)

---
## Diagnosis (2026-02-03)

**Summary:** `Temporal.Instant.prototype.epochNanoseconds` was unimplemented, so accessing it threw.

**Fixes:**
- Unwrap the Temporal.Instant slot and return a BigInt containing `EpochNanoseconds`.

**Tests:**
`dotnet test tests/Asynkron.JsEngine.Tests.Test262 -c Release --settings tests/Asynkron.JsEngine.Tests.Test262/BuiltInsTests.runsettings --filter "Name=Temporal_Instant_prototype_epochNanoseconds"`

** DONE **
