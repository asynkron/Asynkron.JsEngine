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

## Pool Debug Invariants

The engine uses a two-tier invariant system to catch pooling bugs (double-lease, use-after-return, async races).

### Tier 1: PoolDebug (DEBUG-only)

Located in `src/Asynkron.JsEngine/PoolDebug.cs`. Uses `ConditionalWeakTable<object, LeaseState>` to track ownership:

| Method | Purpose |
|--------|---------|
| `MarkLeased(object)` | Called on rent - throws if already leased |
| `MarkReturned(object)` | Called on return - throws if not leased |
| `AssertOwned(object, string)` | Verifies object is currently owned |

All methods use `[Conditional("DEBUG")]` - zero overhead in RELEASE builds.

### Tier 2: PoolGuard (Runtime, optional)

Located in `src/Asynkron.JsEngine/PoolGuard.cs`. Uses lease IDs to detect cross-async mismatches:

- Enable via `JSENGINE_DEBUG_POOL_GUARDS=true` environment variable
- Each rent gets a unique atomic lease ID
- Objects verify their lease ID during operations

### Pooled Objects

Key types implementing `IRentable`:
- `JsEnvironment` - execution scopes
- `IteratorDriverState` - for-of loop state
- `ForInDriverState` - for-in loop state

### Usage Pattern

Pooled objects expose both layers:
```csharp
// Runtime guard (when JSENGINE_DEBUG_POOL_GUARDS=true)
state.MarkLeased(PoolGuard.NextLeaseId());
state.AssertLease(expectedLeaseId, "for-of iterator state");
state.MarkReturned();

// DEBUG-only guard
state.MarkLeasedDebug();
state.AssertOwnership("for-of iterator state");
state.MarkReturnedDebug();
```

In hot loops (e.g., `IteratorDriverPlanExtensions.cs`):
```csharp
var stateLeaseId = PoolGuard.Enabled ? state.PoolLeaseId : 0;

while (!context.ShouldStopEvaluation)
{
    if (stateLeaseId != 0)
        state.AssertLease(stateLeaseId, "for-of iterator state");
    state.AssertOwnership("for-of iterator state");
    // ...
}
```

### What These Catch

1. **Double-lease** - object rented while still in use
2. **Use-after-return** - object accessed after returned to pool
3. **Async races** - iterator state used with wrong environment due to async interleaving
