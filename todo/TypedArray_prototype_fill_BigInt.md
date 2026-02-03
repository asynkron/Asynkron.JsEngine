# TypedArray_prototype_fill_BigInt

FQN:
`Asynkron.JsEngine.Tests.Test262.BuiltInsTests.TypedArray_prototype_fill_BigInt`

Full test name:

- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.TypedArray_prototype_fill_BigInt("built-ins/TypedArray/prototype/fill/BigInt/fill-values-conversion-once.js",False)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.TypedArray_prototype_fill_BigInt("built-ins/TypedArray/prototype/fill/BigInt/fill-values-conversion-once.js",True)

---
## Diagnosis (2026-02-03)

**Summary:** `%TypedArray%.prototype.fill` converted BigInt values per element instead of once, causing `fill-values-conversion-once.js` to throw unexpectedly for BigInt TypedArrays.

**Fix:** Pre-coerce the fill value once (BigInt vs number) before the loop and reuse it for all element writes.

**Tests:** `dotnet test tests/Asynkron.JsEngine.Tests.Test262 -c Release --settings tests/Asynkron.JsEngine.Tests.Test262/BuiltInsTests.runsettings --filter "Name=TypedArray_prototype_fill_BigInt"`

** DONE **
