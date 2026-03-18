# RegExp_prototype_exec

FQN:
`Asynkron.JsEngine.Tests.Test262.BuiltInsTests.RegExp_prototype_exec`

Remaining failures (deep .NET regex engine limitations):

## Duplicate named groups (ES2025 - .NET Regex doesn't support duplicate group names)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.RegExp_prototype_exec("built-ins/RegExp/prototype/exec/duplicate-named-groups-properties.js",False)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.RegExp_prototype_exec("built-ins/RegExp/prototype/exec/duplicate-named-groups-properties.js",True)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.RegExp_prototype_exec("built-ins/RegExp/prototype/exec/duplicate-named-indices-groups-properties.js",False)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.RegExp_prototype_exec("built-ins/RegExp/prototype/exec/duplicate-named-indices-groups-properties.js",True)

## Capture group reset in quantified groups (.NET keeps last capture, JS resets)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.RegExp_prototype_exec("built-ins/RegExp/prototype/exec/S15.10.6.2_A1_T6.js",False)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.RegExp_prototype_exec("built-ins/RegExp/prototype/exec/S15.10.6.2_A1_T6.js",True)
