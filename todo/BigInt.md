# BigInt

FQN:
`Asynkron.JsEngine.Tests.Test262.BuiltInsTests.BigInt`

Full test name:

- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.BigInt("built-ins/BigInt/wrapper-object-ordinary-toprimitive.js",False)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.BigInt("built-ins/BigInt/wrapper-object-ordinary-toprimitive.js",True)

---
## Diagnosis (2026-01-22)

**Summary:** BigInt wrapper objects are unboxed via `__value__` during numeric conversion, bypassing OrdinaryToPrimitive and ignoring overridden `valueOf`/`toString` accessors.

**Error Pattern:**
```
built-ins/BigInt/wrapper-object-ordinary-toprimitive.js
Asynkron.JsEngine.ThrowSignal: Unhandled JavaScript throw:
```

**Analysis:**
The test rewires `BigInt.prototype.toString`/`valueOf` getters and expects OrdinaryToPrimitive to fetch and invoke them in the spec order, throwing TypeError when neither is callable. In `JsOps.ToNumericCore`, wrapper objects are short-circuited by reading `__value__`, which skips ToPrimitive entirely. Paths like unary `+`, `Number(Object(1n))`, and `new Date(Object(1n))` therefore never touch the overridden accessors and avoid the required TypeError, causing the harness assertions to fail (strict and non-strict).

**Fix Direction:**
Remove or gate the `__value__` unboxing in `JsOps.ToNumericCore` (and callers like `ToNumericAsJsValue`/`Number()`), so object-to-numeric conversion goes through `JsOps.ToPrimitive` with hint `Number`. Alternatively, only use `__value__` when the object has no custom @@toPrimitive/valueOf/toString on its prototype chain.

** DONE **
