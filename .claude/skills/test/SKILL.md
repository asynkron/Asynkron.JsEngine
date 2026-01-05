---
name: test
description: Run the internal test suite (not ECMAScript 262 tests)
---

# Run Internal Tests

Run the main test suite:

```bash
dotnet test tests/Asynkron.JsEngine.Tests
```

If a filter is provided, use it:

```bash
dotnet test tests/Asynkron.JsEngine.Tests --filter "FullyQualifiedName~<filter>"
```

Report the results concisely - number of tests passed/failed and any failure details.
