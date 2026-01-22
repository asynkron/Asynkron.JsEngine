# AsyncGeneratorPrototype

FQN:
`Asynkron.JsEngine.Tests.Test262.BuiltInsTests.AsyncGeneratorPrototype`

Full test name:

- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.AsyncGeneratorPrototype("built-ins/AsyncGeneratorPrototype/constructor.js",False)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.AsyncGeneratorPrototype("built-ins/AsyncGeneratorPrototype/constructor.js",True)

---
## Diagnosis (2026-01-22)

**Summary:** `AsyncGenerator.prototype` does not define its own `constructor` property.

**Error Pattern:**
```
Unhandled JavaScript throw: obj should have an own property constructor
built-ins/AsyncGeneratorPrototype/constructor.js
```

**Analysis:**
The test builds an async generator function `g`, then uses `Object.getPrototypeOf(g)` to fetch the
AsyncGenerator constructor and inspects `AsyncGenerator.prototype`. The test expects
`AsyncGenerator.prototype.constructor` to be an own property with value set to the constructor and
attributes `{ writable: false, enumerable: false, configurable: true }`. In the current runtime
setup, `AsyncGeneratorPrototype` is created without a `constructor` property (and no later wiring
adds it), so `verifyProperty` throws the "own property constructor" assertion in both strict and
non-strict runs.

**Fix Direction:**
Define `constructor` on the async generator prototype when wiring intrinsics (likely in
`AsyncGeneratorFunctionConstructor.ConfigureConstructor`, mirroring `AsyncFunctionConstructor`,
or in prototype initialization). Set `Value = constructor`, `Writable = false`,
`Enumerable = false`, `Configurable = true`.
