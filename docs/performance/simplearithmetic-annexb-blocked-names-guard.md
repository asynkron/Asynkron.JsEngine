# SimpleArithmetic AnnexB Blocked-Names Guard

Date: 2026-05-30

## Selected Profile

Full benchmark table run before the change showed `simplearithmetic` as the largest Jint-wins gap:

```text
profile                    asynkron_ms  jint_ms  delta
simplearithmetic                   447       75  Jint 5,96x faster
classdef                          1537      307  Jint 5,01x faster
```

Baseline timestamp: 2026-05-30T11:00:00Z
Baseline signal: `simplearithmetic` Asynkron = 447 ms

## CPU Profile Evidence

The required profile command was run three times:

```bash
./tools/profile simplearithmetic --cpu --calltree-depth 40 --calltree-width 40
```

All three runs showed the same dominant pattern (73–80% of `ExecuteInstructionLoop` time):

```
ExecuteInstructionLoop
└─ HandleEvaluateAndDiscard
   └─ EvaluateExpressionProgram
      └─ ExecuteProgramCall → InvokeCallableNoArgs → InvokeCallableJsValueGeneric
         └─ SyncFunctionInvoker.InvokeWithContext → InvokeWithContextSlow → TryInvokeIrFast
            └─ SyncIrCallTrampoline.TryInvoke → InvokeCurrentFrame
               └─ InvokeWithContext → InvokeWithContextSlow
                  └─ ExecutionPlanRunner.RunSync
                     ├─ EnsureExecutionEnvironment (76.8%)
                     │  └─ CreateExecutionEnvironment
                     │     └─ CastHelpers.Box (73.9% — leaf)
                     └─ ExecutePlan (4.2% — actual arithmetic)
```

The key finding: 73–80% of `ExecuteInstructionLoop` time was spent in `CastHelpers.Box` inside
`CreateExecutionEnvironment`, while the actual arithmetic execution was only 4.2%. The inner
`ExecutePlan` was nearly free; the environment setup dominated.

## Root Cause

`CreateExecutionEnvironment` in `TypedAstEvaluator.ExecutionPlanRunner.Environment.cs` contained:

```csharp
HashSet<Symbol>? blockedFunctionVarNames = null;
if (!_isStrict)
{
    blockedFunctionVarNames = bodyLexicalTemplate.Length == 0
        ? new HashSet<Symbol>(ReferenceEqualityComparer<Symbol>.Instance)
        : new HashSet<Symbol>(bodyLexicalTemplate, ReferenceEqualityComparer<Symbol>.Instance);
    // ... + add parameterNames, catchParameterNames, etc.
}
if (blockedFunctionVarNames is { Count: > 0 })
{
    varEnvironment.SetAnnexBBlockedNames(blockedFunctionVarNames);
}
```

For every call of a non-strict function, this code created a **new** `HashSet<Symbol>` populated
from `bodyLexicalTemplate` (the function's lexical bindings). For the `simplearithmetic` IIFE,
`bodyLexicalTemplate` is `{x, y, z}`, so every one of the 10,000 iterations created a fresh
`HashSet<Symbol>` with three entries.

`SetAnnexBBlockedNames` only serves Annex B B.3.3 function-scope hoisting, consulted at runtime
exclusively by `HandleFunctionDeclaration`. If the function body contains **no** function
declarations, `HandleFunctionDeclaration` is never triggered for that scope, and the
`blockedFunctionVarNames` set is never consulted. Creating it was pure overhead.

`HoistPlan` (already cached on the AST node) exposes `HasFunctionDeclarations`, set to `true`
when any `FunctionDeclaration` appears in the body.

## Change

One-line guard added to `CreateExecutionEnvironment`:

```csharp
// Before:
if (!_isStrict)

// After:
if (!_isStrict && hoistPlan.HasFunctionDeclarations)
```

When `HasFunctionDeclarations` is `false`:
- No `HashSet<Symbol>` is allocated per call.
- No `ImmutableArray<Symbol>.Enumerator` struct is boxed per call.
- `SetAnnexBBlockedNames` is never called.
- `IsAnnexBBlocked` is never consulted (no function declarations to hoist).

When `HasFunctionDeclarations` is `true`, behavior is unchanged.

Files changed: `src/Asynkron.JsEngine/Ast/TypedAstEvaluator.ExecutionPlanRunner.Environment.cs`

## Final Signal

Three focused simplearithmetic runs after the change:

```text
profile                 asynkron_ms  jint_ms  delta
simplearithmetic                281       75  Jint 3,75x faster
simplearithmetic                279       78  Jint 3,58x faster
simplearithmetic                285       77  Jint 3,70x faster
```

Final timestamp: 2026-05-30T11:10:00Z
Final signal: `simplearithmetic` Asynkron = 281, 279, 285 ms → median 281 ms
Signal delta: 447 ms → 281 ms = −166 ms (−37.1%)

The `classdef` benchmark also improved (constructors and methods typically contain no
function declarations):

```text
profile    baseline_ms  final_ms  delta
classdef          1537       871  −43.4%
```

Full benchmark table post-change showed no regressions: `mapset` and `json` had a noisy
first post-change run (1616ms, 1738ms) but stabilized to 1059ms and 1209ms (≤baseline)
on the second run.

## Verification

Build:
```text
ok dotnet build: 2 projects, 0 errors, 0 warnings
```

Tests:
```text
Passed! — Failed: 0, Passed: 4550, Skipped: 2, Total: 4552, Duration: 35 s
```

Allocation regression check (smoke set):
```text
profile             baseline_bytes  current_bytes  delta_%  status
fib                          84056          84056      +0.0  ok
forloop                     221552         246336     +11.2  ok
ir-arithmetic                80848          80848      +0.0  ok
functioncalls              24167488       24167488      +0.0  ok
functioncalls-lite          4831528        4831528      +0.0  ok
OK: no allocation regression beyond 15% tolerance
```
