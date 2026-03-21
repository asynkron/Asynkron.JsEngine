# Atomics_notify_bigint

FQN:
`Asynkron.JsEngine.Tests.Test262.BuiltInsTests.Atomics_notify_bigint`

Full test name:

- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.Atomics_notify_bigint("built-ins/Atomics/notify/bigint/non-shared-bufferdata-count-evaluation-throws.js",False)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.Atomics_notify_bigint("built-ins/Atomics/notify/bigint/non-shared-bufferdata-count-evaluation-throws.js",True)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.Atomics_notify_bigint("built-ins/Atomics/notify/bigint/non-shared-bufferdata-index-evaluation-throws.js",False)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.Atomics_notify_bigint("built-ins/Atomics/notify/bigint/non-shared-bufferdata-index-evaluation-throws.js",True)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.Atomics_notify_bigint("built-ins/Atomics/notify/bigint/non-shared-bufferdata-returns-0.js",False)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.Atomics_notify_bigint("built-ins/Atomics/notify/bigint/non-shared-bufferdata-returns-0.js",True)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.Atomics_notify_bigint("built-ins/Atomics/notify/bigint/notify-all-on-loc.js",False)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.Atomics_notify_bigint("built-ins/Atomics/notify/bigint/notify-all-on-loc.js",True)

---
## Diagnosis (2026-01-22)

**Summary:** Atomics.notify throws on non-shared buffers (and short-circuits index/count evaluation) plus agent-based notify tests fail because `$262.agent` is missing.

**Error Pattern:**
```
Asynkron.JsEngine.ThrowSignal: Unhandled JavaScript throw: Expected a Test262Error but got a TypeError
Asynkron.JsEngine.ThrowSignal: Unhandled JavaScript throw: 'TypeError': 'Atomics.wait/notify require a SharedArrayBuffer'
Asynkron.JsEngine.ThrowSignal: Unhandled JavaScript throw: 'TypeError': 'Cannot read properties of null or undefined'
```

**Analysis:**
For non-shared-buffer tests, the spec requires Atomics.notify to evaluate `index`/`count`, then return 0 when the typed array's buffer is not a SharedArrayBuffer. The engine instead throws a TypeError ("Atomics.wait/notify require a SharedArrayBuffer") before evaluating `index`/`count`, so the Test262Error from `valueOf()` never happens and the return-0 tests throw. The notify-all-on-loc test fails earlier in harness execution with a null/undefined property access, which points to `$262.agent`/atomicsHelper being unavailable in this runner.

**Fix Direction:**
Update Atomics.notify to perform `ValidateAtomicAccess`/`ToInteger(count)` before checking for SharedArrayBuffer and to return 0 (not throw) when the buffer is not shared; ensure the BigInt typed-array path uses that behavior. For notify-all-on-loc, implement `$262.agent` host support for agent-based Atomics (or skip agent-only tests when agent support is absent).

** DONE **
