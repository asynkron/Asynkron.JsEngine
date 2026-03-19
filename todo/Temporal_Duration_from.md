# Temporal_Duration_from

Status: 54/56 passing (2 remaining = precision-exact-numerical-values near 2^53 boundary)

FQN:
`Asynkron.JsEngine.Tests.Test262.BuiltInsTests.Temporal_Duration_from`

Remaining failures:

- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.Temporal_Duration_from("built-ins/Temporal/Duration/from/argument-duration-precision-exact-numerical-values.js",False)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.Temporal_Duration_from("built-ins/Temporal/Duration/from/argument-duration-precision-exact-numerical-values.js",True)

Root cause: Duration.ToString() uses long arithmetic which overflows for milliseconds values near 4.5e18.
Requires BigInteger arithmetic in the ToString balancing logic.
