# Symbol_unscopables

FQN:
`Asynkron.JsEngine.Tests.Test262.BuiltInsTests.Symbol_unscopables`

Full test name:

- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.Symbol_unscopables("built-ins/Symbol/unscopables/prop-desc.js",False)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.Symbol_unscopables("built-ins/Symbol/unscopables/prop-desc.js",True)

---
## Diagnosis (2026-02-03)

**Summary:** Symbol built-in metadata was off in two ways. The compat data listed `Symbol.prototype.description` as a method, so codegen emitted a missing-method stub that shadowed the accessor and returned a host function. Also, well-known symbol constants and `[Symbol.toPrimitive]` needed spec-accurate descriptors.

**Fixes:**
- Update `stdlib-compat.json` to declare `Symbol.prototype.description` as a getter so the accessor is used.
- Define well-known symbol constants via `DefineConstantProperty` and align `[Symbol.toPrimitive]` attributes.
- Use `JsOps.ToJsString` in `Symbol.for` to match `ToString` semantics.

**Tests:**
`dotnet test tests/Asynkron.JsEngine.Tests.Test262 -c Release --settings tests/Asynkron.JsEngine.Tests.Test262/BuiltInsTests.runsettings --filter "Name=Symbol_for|Name=Symbol_keyFor|Name=Symbol_iterator|Name=Symbol_asyncIterator|Name=Symbol_species|Name=Symbol_toPrimitive|Name=Symbol_hasInstance|Name=Symbol_match|Name=Symbol_replace|Name=Symbol_search|Name=Symbol_split|Name=Symbol_matchAll|Name=Symbol_toStringTag|Name=Symbol_unscopables|Name=Symbol_isConcatSpreadable|Name=Symbol_prototype_description|Name=Symbol_prototype_Symbol_toPrimitive"`

** DONE **
