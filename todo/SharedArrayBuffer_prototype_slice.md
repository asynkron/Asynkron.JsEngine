# SharedArrayBuffer_prototype_slice

FQN:
`Asynkron.JsEngine.Tests.Test262.BuiltInsTests.SharedArrayBuffer_prototype_slice`

Full test name:

- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.SharedArrayBuffer_prototype_slice("built-ins/SharedArrayBuffer/prototype/slice/end-default-if-absent.js",False)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.SharedArrayBuffer_prototype_slice("built-ins/SharedArrayBuffer/prototype/slice/end-default-if-absent.js",True)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.SharedArrayBuffer_prototype_slice("built-ins/SharedArrayBuffer/prototype/slice/end-default-if-undefined.js",False)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.SharedArrayBuffer_prototype_slice("built-ins/SharedArrayBuffer/prototype/slice/end-default-if-undefined.js",True)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.SharedArrayBuffer_prototype_slice("built-ins/SharedArrayBuffer/prototype/slice/end-exceeds-length.js",False)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.SharedArrayBuffer_prototype_slice("built-ins/SharedArrayBuffer/prototype/slice/end-exceeds-length.js",True)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.SharedArrayBuffer_prototype_slice("built-ins/SharedArrayBuffer/prototype/slice/negative-end.js",False)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.SharedArrayBuffer_prototype_slice("built-ins/SharedArrayBuffer/prototype/slice/negative-end.js",True)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.SharedArrayBuffer_prototype_slice("built-ins/SharedArrayBuffer/prototype/slice/negative-start.js",False)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.SharedArrayBuffer_prototype_slice("built-ins/SharedArrayBuffer/prototype/slice/negative-start.js",True)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.SharedArrayBuffer_prototype_slice("built-ins/SharedArrayBuffer/prototype/slice/number-conversion.js",False)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.SharedArrayBuffer_prototype_slice("built-ins/SharedArrayBuffer/prototype/slice/number-conversion.js",True)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.SharedArrayBuffer_prototype_slice("built-ins/SharedArrayBuffer/prototype/slice/species-returns-larger-arraybuffer.js",False)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.SharedArrayBuffer_prototype_slice("built-ins/SharedArrayBuffer/prototype/slice/species-returns-larger-arraybuffer.js",True)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.SharedArrayBuffer_prototype_slice("built-ins/SharedArrayBuffer/prototype/slice/species.js",False)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.SharedArrayBuffer_prototype_slice("built-ins/SharedArrayBuffer/prototype/slice/species.js",True)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.SharedArrayBuffer_prototype_slice("built-ins/SharedArrayBuffer/prototype/slice/start-default-if-absent.js",False)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.SharedArrayBuffer_prototype_slice("built-ins/SharedArrayBuffer/prototype/slice/start-default-if-absent.js",True)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.SharedArrayBuffer_prototype_slice("built-ins/SharedArrayBuffer/prototype/slice/start-default-if-undefined.js",False)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.SharedArrayBuffer_prototype_slice("built-ins/SharedArrayBuffer/prototype/slice/start-default-if-undefined.js",True)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.SharedArrayBuffer_prototype_slice("built-ins/SharedArrayBuffer/prototype/slice/start-exceeds-end.js",False)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.SharedArrayBuffer_prototype_slice("built-ins/SharedArrayBuffer/prototype/slice/start-exceeds-end.js",True)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.SharedArrayBuffer_prototype_slice("built-ins/SharedArrayBuffer/prototype/slice/start-exceeds-length.js",False)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.SharedArrayBuffer_prototype_slice("built-ins/SharedArrayBuffer/prototype/slice/start-exceeds-length.js",True)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.SharedArrayBuffer_prototype_slice("built-ins/SharedArrayBuffer/prototype/slice/tointeger-conversion-end.js",False)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.SharedArrayBuffer_prototype_slice("built-ins/SharedArrayBuffer/prototype/slice/tointeger-conversion-end.js",True)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.SharedArrayBuffer_prototype_slice("built-ins/SharedArrayBuffer/prototype/slice/tointeger-conversion-start.js",False)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.SharedArrayBuffer_prototype_slice("built-ins/SharedArrayBuffer/prototype/slice/tointeger-conversion-start.js",True)

---
## Diagnosis (2026-02-03)

**Summary:** Species handling failed in two places: the source generator emitted missing symbol-method stubs that overwrote `[Symbol.species]` getters (making species non-constructors), and `SharedArrayBuffer.prototype.slice` treated `ReflectHelper.Construct`’s `JsValue` result as `object`, causing false “did not return an object” errors.

**Fix:**
- Generator: suppress missing symbol-method/getter/setter stubs when any symbol member with that name is already implemented.
- Slice: keep the `Construct` result as `JsValue`, validate via `TryGetObject`, and use the actual object for comparisons.

**Tests:** `dotnet test tests/Asynkron.JsEngine.Tests.Test262 -c Release --settings tests/Asynkron.JsEngine.Tests.Test262/BuiltInsTests.runsettings --filter "Name=SharedArrayBuffer_prototype_slice"`

** DONE **
