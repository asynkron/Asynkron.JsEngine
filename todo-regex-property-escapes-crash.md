# Investigation Report: RegExp Property Escapes Generated Tests Crash Test Host

## Problem Summary
`RegExp_propertyEscapes_generated` Test262 tests crash the dotnet test host process. Specific crashing tests include `Script_Extensions_-_Old_Uyghur.js`, `Script_Extensions_-_Limbu.js`, `Script_Extensions_-_Katakana.js`, and others with large Unicode ranges.

## Affected Components
- `src/Asynkron.JsEngine/Ast/TypedAstEvaluator.ExecutionPlanRunner.Loop.cs` — **PRIMARY**: missing CancellationToken check
- `src/Asynkron.JsEngine/JsEngine.cs` — `ExecutionTimeout` / `CreateEvaluationCancellationToken()` (line 563)
- `src/Asynkron.JsEngine/EvaluationContext.cs` — `ThrowIfCancellationRequested()` (line 334)
- `src/Asynkron.JsEngine/StdLib/Function/FunctionPrototype.cs` — `.apply()` (line 59), allocates 10K-element `List<JsValue>` per chunk
- `src/Asynkron.JsEngine/StdLib/String/StringConstructor.cs` — `FromCodePoint()` (line 25)

## Evidence Collected

### Test Execution Timing (Hex_Digit test, ~1M code points)
- Pure .NET Regex match on 2M+ char string: **< 1 second** (PASS)
- `buildString()` through JS engine for 1M code points: **~9 seconds**
- Full Hex_Digit test via unit test harness: **~12 seconds**
- Full Hex_Digit test via Test262 runner: **3 minutes 36 seconds** (despite 10-second timeout!)

### Root Cause Evidence: Missing CancellationToken Check

Grep for cancellation/timeout in ExecutionPlanRunner files returned **ZERO matches**:

```
# All ExecutionPlanRunner files
src/Asynkron.JsEngine/Ast/TypedAstEvaluator.ExecutionPlanRunner.Loop.cs
src/Asynkron.JsEngine/Ast/TypedAstEvaluator.ExecutionPlanRunner.cs
src/Asynkron.JsEngine/Ast/TypedAstEvaluator.ExecutionPlanRunner.Calls.cs
src/Asynkron.JsEngine/Ast/TypedAstEvaluator.ExecutionPlanRunner.Try.cs

# Zero references to: cancel, timeout, CancellationToken, ThrowIfCancellation
```

The legacy evaluator paths DO check cancellation:
- `Ast/Legacy/LoopPlanExtensions.cs:103`
- `Ast/Legacy/BlockStatementExtensions.cs:355`
- `Ast/Legacy/StatementNodeExtensions.cs:32`
- `Ast/ProgramNodeExtensions.cs:591`

### The Critical Code Path

`TypedAstEvaluator.ExecutionPlanRunner.Loop.cs` line 191:

```csharp
private JsValue ExecuteInstructionLoop(ref JsEnvironment environment, EvaluationContext context)
{
    var instructions = _plan!.Instructions;
    var instructionsLength = instructions.Length;
    // ...
    while ((uint)_programCounter < (uint)instructionsLength)
    {
        // NO cancellation token check anywhere in this loop!
        _currentInstructionIndex = _programCounter;
        var instruction = ProfileFetchInstruction(ref instructionsRef, _programCounter);
        // ... process instruction ...
    }
}
```

### Component Isolation Results

| Step | What was tested | Result |
|------|----------------|--------|
| 1 | .NET Regex compile `\p{Hex_Digit}` pattern | PASS, instant |
| 2 | .NET Regex match on 2M+ char string | PASS, < 1s |
| 3 | .NET Regex with surrogate pair patterns | PASS |
| 4 | .NET Regex with full astral range string | PASS |
| 5-7 | JS engine `buildString()` components | PASS, slow |
| 8 | Full `buildString()` 1M code points via JS | PASS, ~9s |
| 9 | Full Hex_Digit test in unit test | PASS, ~12s |
| 10 | `testPropertyEscapes()` regex test only | PASS, < 1s |
| 11 | Test262 harness Hex_Digit test | PASS but **3m36s** (timeout ignored!) |

### Crash Mechanism

1. Test262 runs tests in parallel (`[Parallelizable(ParallelScope.All)]`)
2. Each property escape test allocates massive strings (1M+ code points = multi-MB strings)
3. `ExecutionTimeout = TimeSpan.FromSeconds(10)` is set but **never enforced** in IR execution
4. Multiple tests run simultaneously, each taking minutes instead of being killed at 10s
5. Combined memory pressure from parallel long-running tests causes OOM / host process crash

## Root Cause Analysis

### Hypothesis 1 (Confirmed): ExecutionPlanRunner ignores CancellationToken

The IR-based execution loop in `ExecuteInstructionLoop()` never checks the `CancellationToken` that `ExecutionTimeout` creates. The timeout mechanism only works in:
- Legacy evaluator paths (which check it in loop/block/statement extensions)
- Event loop drain (`JsEngine.cs` line 927-948)

Since property escape tests use tight loops (for-loop in `buildString()`, iterating 1M+ code points), the IR executor runs for minutes without ever checking if it should stop.

- **Evidence supporting**: Hex_Digit test completed in 3m36s despite 10-second timeout. Zero cancellation references in any ExecutionPlanRunner file.
- **Evidence against**: None. This is definitively confirmed.

### Hypothesis 2 (Contributing): Massive memory allocation under parallel execution

Each test allocates:
- ~100 `List<JsValue>` of 10K elements each (via `.apply()`)
- Multi-MB strings from `String.fromCodePoint()`
- Compiled .NET Regex objects with large character classes

When dozens of these tests run in parallel (NUnit `ParallelScope.All`), combined allocation exceeds available memory.

- **Evidence supporting**: Individual tests complete (slowly). Crashes only happen when many run together.
- **Evidence against**: This alone wouldn't crash if timeouts worked — tests would be killed at 10s before accumulating memory.

## Recommended Fix

### Option A: Add CancellationToken check to ExecutionPlanRunner (Primary Fix)

Add a periodic cancellation check in `ExecuteInstructionLoop()`. To minimize performance impact, check every N instructions (e.g., every 1024 or 4096 iterations):

```csharp
// In ExecuteInstructionLoop(), inside the while loop:
private int _instructionsSinceLastCheck;

while ((uint)_programCounter < (uint)instructionsLength)
{
    if (++_instructionsSinceLastCheck >= 4096)
    {
        _instructionsSinceLastCheck = 0;
        context.ThrowIfCancellationRequested();
    }
    // ... existing instruction processing ...
}
```

Alternative: Check on backward jumps only (loop iterations), since those are the instructions that can cause unbounded execution:

```csharp
// In the jump/branch instruction handler, when jumping backward:
if (targetPc < _programCounter)
{
    context.ThrowIfCancellationRequested();
}
```

- **Pros**: Fixes the root cause. Backward-jump-only check has near-zero overhead for non-looping code. Consistent with legacy evaluator behavior.
- **Cons**: Every-N-instructions approach adds a branch to the hot path (mitigated by large N). Backward-jump approach requires identifying the right instruction handlers.

### Option B: Optimize buildString() (Secondary / Performance)

The `buildString()` pattern (100 calls to `String.fromCodePoint.apply(null, array_of_10K)`) is inherently slow in any JS engine for 1M+ code points. This is a test harness pattern, not user code, so optimizing it is lower priority. However, if needed:
- Fast-path `String.fromCodePoint` for contiguous ranges
- Optimize `.apply()` to avoid `List<JsValue>` allocation for array-like arguments

- **Pros**: Would make these specific tests faster even without timeout fix.
- **Cons**: Doesn't fix the underlying timeout bug. Only helps this specific pattern.

## Test Plan
- [ ] After adding cancellation check: run a single property escape test and verify it terminates within ~10-15 seconds
- [ ] Run `dotnet test --filter "RegExp_propertyEscapes_generated"` subset and verify no host crashes
- [ ] Run full internal test suite: `dotnet test tests/Asynkron.JsEngine.Tests` — verify no regressions
- [ ] Benchmark IR execution performance before/after to confirm minimal overhead:
  ```bash
  ./tools/profile fibonacci --cpu
  ./tools/profile sieve --cpu
  ```
- [ ] Verify other timeout-dependent tests still pass

## Additional Notes

- The .NET Regex engine itself handles these patterns fine — compilation and matching of huge Unicode property escape patterns is fast (< 1 second). The crash is purely about execution time of JS code, not regex.
- The backward-jump cancellation check (Option A variant) is preferred because it targets exactly the scenario that causes unbounded execution (loops) while adding zero overhead to straight-line code.
- Legacy evaluator paths already have this fix — the IR evaluator was simply never given the same treatment when it was written.
- This is a correctness bug, not just a test issue: any user code with `ExecutionTimeout` set will also fail to be cancelled during IR execution.
