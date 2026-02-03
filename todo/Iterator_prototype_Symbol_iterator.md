# Iterator_prototype_Symbol_iterator

FQN:
`Asynkron.JsEngine.Tests.Test262.BuiltInsTests.Iterator_prototype_Symbol_iterator`

Full test name:

- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.Iterator_prototype_Symbol_iterator("built-ins/Iterator/prototype/Symbol.iterator/name.js",False)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.Iterator_prototype_Symbol_iterator("built-ins/Iterator/prototype/Symbol.iterator/name.js",True)

---
## Diagnosis (2026-02-03)

**Summary:** `IteratorPrototype[Symbol.iterator]` used a symbol alias that reused `__selfIterator` and carried the wrong function name. The test expects the function name to be `[Symbol.iterator]`.

**Fix:** Replace the alias with a symbol method declaration: `SelfIterator` is now `[JsSymbolMethod("iterator", DisplayName = "[Symbol.iterator]")]`, so the function name matches the spec.

**Tests:** `dotnet test tests/Asynkron.JsEngine.Tests.Test262 -c Release --settings tests/Asynkron.JsEngine.Tests.Test262/BuiltInsTests.runsettings --filter "Name=Iterator_prototype_Symbol_iterator"`

** DONE **
