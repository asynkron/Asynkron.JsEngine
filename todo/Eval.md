# Eval

FQN:
`Asynkron.JsEngine.Tests.Test262.BuiltInsTests.Eval`

Full test name:

- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.Eval("built-ins/eval/length-enumerable.js",False)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.Eval("built-ins/eval/length-enumerable.js",True)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.Eval("built-ins/eval/length-non-configurable.js",False)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.Eval("built-ins/eval/length-non-configurable.js",True)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.Eval("built-ins/eval/length-non-writable.js",False)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.Eval("built-ins/eval/length-non-writable.js",True)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.Eval("built-ins/eval/length-value.js",False)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.Eval("built-ins/eval/length-value.js",True)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.Eval("built-ins/eval/name.js",False)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.Eval("built-ins/eval/name.js",True)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.Eval("built-ins/eval/no-proto.js",False)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.Eval("built-ins/eval/no-proto.js",True)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.Eval("built-ins/eval/not-a-constructor.js",False)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.Eval("built-ins/eval/not-a-constructor.js",True)

---
## Diagnosis (2026-02-03)

**Summary:** Eval is now a proper non-constructable function object with correct `name`/`length` attributes and no `prototype` property, so Test262 property-attribute checks pass.

**Fixes:**
- `EvalHostFunction` now implements object-like interfaces so `hasOwnProperty` and descriptor checks work.
- Added `name` and `length` as configurable, non-enumerable, non-writable data properties.
- Marked eval as non-constructable (`DisallowConstruct`) and removed `prototype` property.

**Tests:**
`dotnet test tests/Asynkron.JsEngine.Tests.Test262 -c Release --settings tests/Asynkron.JsEngine.Tests.Test262/BuiltInsTests.runsettings --filter "Name=Eval"`

**DONE**
