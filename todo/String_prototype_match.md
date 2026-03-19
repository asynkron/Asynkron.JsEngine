# String_prototype_match

FQN:
`Asynkron.JsEngine.Tests.Test262.BuiltInsTests.String_prototype_match`

Remaining failures: 4 (blocked by .NET group reset limitation)

.NET does not reset capturing groups on quantifier re-entry. Lines 33-36 test
`(?:(?:(?<x>a)|(?<x>b)|c)\k<x>){2}` where the last iteration is `c` (no capture),
expecting `groups.x = undefined`. .NET keeps stale captures from previous iterations,
causing the match to fail entirely.

- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.String_prototype_match("built-ins/String/prototype/match/duplicate-named-groups-properties.js",False)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.String_prototype_match("built-ins/String/prototype/match/duplicate-named-groups-properties.js",True)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.String_prototype_match("built-ins/String/prototype/match/duplicate-named-indices-groups-properties.js",False)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.String_prototype_match("built-ins/String/prototype/match/duplicate-named-indices-groups-properties.js",True)
