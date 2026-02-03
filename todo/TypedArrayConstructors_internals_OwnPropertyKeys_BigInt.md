# TypedArrayConstructors_internals_OwnPropertyKeys_BigInt

FQN:
`Asynkron.JsEngine.Tests.Test262.BuiltInsTests.TypedArrayConstructors_internals_OwnPropertyKeys_BigInt`

Full test name:

- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.TypedArrayConstructors_internals_OwnPropertyKeys_BigInt("built-ins/TypedArrayConstructors/internals/OwnPropertyKeys/BigInt/integer-indexes-and-string-and-symbol-keys-.js",False)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.TypedArrayConstructors_internals_OwnPropertyKeys_BigInt("built-ins/TypedArrayConstructors/internals/OwnPropertyKeys/BigInt/integer-indexes-and-string-and-symbol-keys-.js",True)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.TypedArrayConstructors_internals_OwnPropertyKeys_BigInt("built-ins/TypedArrayConstructors/internals/OwnPropertyKeys/BigInt/not-enumerable-keys.js",False)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.TypedArrayConstructors_internals_OwnPropertyKeys_BigInt("built-ins/TypedArrayConstructors/internals/OwnPropertyKeys/BigInt/not-enumerable-keys.js",True)

---
## Diagnosis (2026-02-03)

**Summary:** `Reflect.ownKeys` returned symbol keys as internal `@@symbol:*` strings instead of actual `Symbol` values, causing key ordering assertions to fail for BigInt typed arrays.

**Fix:** In `Reflect.ownKeys`, convert internal symbol keys back to `JsSymbol` instances via `JsSymbol.TryGetByInternalKey` before pushing to the result array.

**Tests:** `dotnet test tests/Asynkron.JsEngine.Tests.Test262 -c Release --settings tests/Asynkron.JsEngine.Tests.Test262/BuiltInsTests.runsettings --filter "Name=TypedArrayConstructors_internals_OwnPropertyKeys_BigInt"`

** DONE **
