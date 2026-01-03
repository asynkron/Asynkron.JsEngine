# Coding Standards

## Antipatterns
- Avoid `object`; use `JsValue` for JavaScript values to prevent boxing.
- Avoid `IDictionary<Symbol, T>`; slot-based access is faster than hash lookups.
- Minimize allocations in hot paths; skip new objects/arrays/strings inside tight loops.
- Avoid deep recursion; prefer iterative forms for stack safety and perf.
- Avoid unnecessary `JsEnvironment` activations; reuse environments when possible.
- Avoid default-culture conversions; always specify `InvariantCulture` for number/string conversions.
- Avoid complex LINQ in hot paths; simple loops are cheaper.

## InvariantCulture for Number/String Conversions
All floating-point and double conversions must use `CultureInfo.InvariantCulture`.

### Correct
```csharp
double value = 3.14;
string str = value.ToString(System.Globalization.CultureInfo.InvariantCulture);

long intValue = 1000;
string intStr = intValue.ToString(System.Globalization.CultureInfo.InvariantCulture);

double num = 42.123;
string formatted = num.ToString("F2", System.Globalization.CultureInfo.InvariantCulture);
string exponential = num.ToString("e", System.Globalization.CultureInfo.InvariantCulture);
```

### Incorrect (do not use)
```csharp
double value = 3.14;
string str = value.ToString();

long intValue = 1000;
string intStr = intValue.ToString();
```

### Where This Applies
- Number.prototype methods: toString, toFixed, toExponential, toPrecision
- String constructor conversions
- Math operations that produce string output
- JSON serialization of numbers
- Console output of numeric values
- Date/time formatting with numeric components

### Why
Cultures differ on separators; JavaScript expects invariant/US formatting. Using `InvariantCulture` keeps behavior consistent across locales.
