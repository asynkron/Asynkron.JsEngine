# Symbol_keyFor

FQN:
`Asynkron.JsEngine.Tests.Test262.BuiltInsTests.Symbol_keyFor`

Full test name:

- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.Symbol_keyFor("built-ins/Symbol/keyFor/arg-non-symbol.js",False)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.Symbol_keyFor("built-ins/Symbol/keyFor/arg-non-symbol.js",True)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.Symbol_keyFor("built-ins/Symbol/keyFor/arg-symbol-registry-miss.js",False)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.Symbol_keyFor("built-ins/Symbol/keyFor/arg-symbol-registry-miss.js",True)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.Symbol_keyFor("built-ins/Symbol/keyFor/cross-realm.js",False)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.Symbol_keyFor("built-ins/Symbol/keyFor/cross-realm.js",True)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.Symbol_keyFor("built-ins/Symbol/keyFor/length.js",False)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.Symbol_keyFor("built-ins/Symbol/keyFor/length.js",True)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.Symbol_keyFor("built-ins/Symbol/keyFor/name.js",False)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.Symbol_keyFor("built-ins/Symbol/keyFor/name.js",True)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.Symbol_keyFor("built-ins/Symbol/keyFor/not-a-constructor.js",False)

---
## Diagnosis (2026-02-03)

**Summary:** `Symbol.keyFor` now throws for non-symbols and well-known symbols are no longer registered in the global symbol registry, matching spec expectations.

**Fixes:**
- `Symbol.keyFor` now throws a TypeError for non-symbol arguments.
- Well-known symbols are created with `JsSymbol.Create`, not `JsSymbol.For`, so `Symbol.keyFor(Symbol.iterator)` returns `undefined`.

**Tests:**
`dotnet test tests/Asynkron.JsEngine.Tests.Test262 -c Release --settings tests/Asynkron.JsEngine.Tests.Test262/BuiltInsTests.runsettings --filter "Name=Symbol_keyFor"`

**DONE**
