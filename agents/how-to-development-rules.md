# Development Rules

## Thread Safety
- Do not use `Task.Wait()`, `Task.Result`, or `Thread.Sleep()`.
- Do not use `ThreadStatic`, `AsyncLocal<T>`, or shared state between async calls.
- Pass context explicitly via `JsEnvironment` or equivalent.

## ECMAScript Compliance
- Follow the specification; no non-standard extensions.
- Support strict and sloppy mode differences.

## Error Handling
- Throw `NotSupportedException` with clear reasons; never silently degrade.
- Use `realm.Logger?.LogInformation(...)` for diagnostics (no `Console.WriteLine`).

## Code Generation
- Never edit `.generated.` files; change partials/helpers instead.

## Test Timeouts
- All tests must finish within 20 seconds.
- CLI pattern: `dotnet test -- xUnit.MaxParallelThreads=1 -timeout 20000`.

## Project Structure
```
src/
  Asynkron.JsEngine/
  Asynkron.JsEngine.Generators/
tests/
  Asynkron.JsEngine.Tests/
  Asynkron.JsEngine.Tests.Test262/
examples/
docs/
```

## Other Guidelines
- Rider MCP is available for refactor/rename; prefer when symbol-aware edits help.
