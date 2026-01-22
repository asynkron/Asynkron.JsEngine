# Array_of

FQN:
`Asynkron.JsEngine.Tests.Test262.BuiltInsTests.Array_of`

Full test name:

- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.Array_of("built-ins/Array/of/does-not-use-prototype-properties.js",False)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.Array_of("built-ins/Array/of/does-not-use-prototype-properties.js",True)

---
## Diagnosis (2026-01-22)

**Summary:** Array.of writes elements via ordinary assignment, so prototype setters fire instead of defining own data properties.

**Error Pattern:**
```
Asynkron.JsEngine.ThrowSignal: Unhandled JavaScript throw: Should define own properties
built-ins/Array/of/does-not-use-prototype-properties.js
```

**Analysis:**
The test installs a throwing setter on `Array.prototype["0"]` and `Custom.prototype["0"]`.
`Array.of(true)` and `Array.of.call(Custom, true)` must define own data properties
(CreateDataPropertyOrThrow) for each index, which bypasses prototype setters. The
engine currently triggers the setter, indicating it uses Set/Put assignment for
element creation rather than defining the property directly.

**Fix Direction:**
Update the Array.of implementation to use CreateDataPropertyOrThrow/DefineProperty
for each element on the newly created array (or custom instance), ensuring indices
are defined as own data properties without invoking prototype setters.
