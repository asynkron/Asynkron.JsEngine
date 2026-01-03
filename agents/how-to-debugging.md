# Debugging Aids

## Realm Logger Assertions
- Use `FakeLogger` (`Microsoft.Extensions.Logging.Testing`) with `JsEngineOptions { DebugMode = true, Logger = fakeLogger }`.
- After running, inspect `fakeLogger.Collector.Snapshot()`.
- Example assertions: ensure no slot read misses; confirm expected hits for identifiers to prove fast paths.

## AST Slot Metadata Checks
- Scope analysis stamps `FunctionExpression` and `BlockStatement` with `ScopeId`, `SlotCount`, and `SlotMap` (symbol → slot index).
- Example:
```csharp
var parsed = engine.ParseProgram(script);
var runDecl = (FunctionDeclaration)parsed.Body[0];
var slotMap = runDecl.Function.SlotMap;
Assert.True(slotMap.ContainsKey(Symbol.Create("i")));
```
- Verifies identifiers received slots in expected scopes before execution.
