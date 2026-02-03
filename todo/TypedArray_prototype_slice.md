# TypedArray_prototype_slice

FQN:
`Asynkron.JsEngine.Tests.Test262.BuiltInsTests.TypedArray_prototype_slice`

Full test name:

- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.TypedArray_prototype_slice("built-ins/TypedArray/prototype/slice/coerced-start-end-grow.js",False)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.TypedArray_prototype_slice("built-ins/TypedArray/prototype/slice/coerced-start-end-grow.js",True)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.TypedArray_prototype_slice("built-ins/TypedArray/prototype/slice/coerced-start-end-shrink.js",False)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.TypedArray_prototype_slice("built-ins/TypedArray/prototype/slice/coerced-start-end-shrink.js",True)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.TypedArray_prototype_slice("built-ins/TypedArray/prototype/slice/detached-buffer-custom-ctor-other-targettype.js",False)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.TypedArray_prototype_slice("built-ins/TypedArray/prototype/slice/detached-buffer-custom-ctor-other-targettype.js",True)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.TypedArray_prototype_slice("built-ins/TypedArray/prototype/slice/detached-buffer-speciesctor-get-species-custom-ctor-throws.js",False)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.TypedArray_prototype_slice("built-ins/TypedArray/prototype/slice/detached-buffer-speciesctor-get-species-custom-ctor-throws.js",True)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.TypedArray_prototype_slice("built-ins/TypedArray/prototype/slice/resizable-buffer.js",False)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.TypedArray_prototype_slice("built-ins/TypedArray/prototype/slice/resizable-buffer.js",True)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.TypedArray_prototype_slice("built-ins/TypedArray/prototype/slice/speciesctor-get-ctor-returns-throws.js",False)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.TypedArray_prototype_slice("built-ins/TypedArray/prototype/slice/speciesctor-get-ctor-returns-throws.js",True)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.TypedArray_prototype_slice("built-ins/TypedArray/prototype/slice/speciesctor-get-species-custom-ctor-invocation.js",False)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.TypedArray_prototype_slice("built-ins/TypedArray/prototype/slice/speciesctor-get-species-custom-ctor-invocation.js",True)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.TypedArray_prototype_slice("built-ins/TypedArray/prototype/slice/speciesctor-get-species-use-default-ctor.js",False)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.TypedArray_prototype_slice("built-ins/TypedArray/prototype/slice/speciesctor-get-species-use-default-ctor.js",True)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.TypedArray_prototype_slice("built-ins/TypedArray/prototype/slice/speciesctor-resize.js",False)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.TypedArray_prototype_slice("built-ins/TypedArray/prototype/slice/speciesctor-resize.js",True)

---
## Diagnosis (2026-02-03)

**Summary:** `speciesctor-resize.js` failed because symbol-keyed properties (e.g., `Symbol.species`) were registered using `JsSymbol.For("Symbol.*")`, while runtime well-known symbols use `JsSymbol.Create`. This mismatch made `%TypedArray%.prototype.slice` fall back to default species handling and skip the subclass constructor path, so buffer resize side effects never ran.

**Fix:** Generate symbol-keyed properties using well-known `SymbolKeys.*` (and `JsSymbol.PropertyKey`) so `[Symbol.species]` resolves correctly. This restores species constructor invocation and resizable buffer behavior.

**Tests:** `dotnet test tests/Asynkron.JsEngine.Tests.Test262 -c Release --settings tests/Asynkron.JsEngine.Tests.Test262/BuiltInsTests.runsettings --filter "Name=TypedArray_prototype_slice"`

** DONE **
