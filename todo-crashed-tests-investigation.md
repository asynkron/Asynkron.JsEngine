# Investigation Report: Test262 Crashed Tests (Non-RegExp Categories)

## Problem Summary
531 Test262 tests are recorded as "crashed" in `.testrunner/crashed-tests.txt`. The user asked about ~50 specific tests spanning class elements, generators, module syntax, numeric literals, eval, and subtraction. All of these tests pass when run individually or in targeted batches -- they are **collateral damage** from the actual crash source: RegExp Unicode property escape tests.

## Key Finding: Stale Crash List
The `.testrunner/crashed-tests.txt` is a stale artifact from before commits `87ab5f85` (CancellationToken on backward jumps) and `48decb1e` (RegExp property escape fix). The 531 "crashed" entries represent tests that were running in the same NUnit parallel process when the actual crashing test killed the host.

## Affected Components
- `.testrunner/crashed-tests.txt` -- stale crash list, needs re-generation
- `src/Asynkron.JsEngine/Ast/TypedAstEvaluator.ExecutionPlanRunner.Loop.cs:239-241` -- CancellationToken check on Jump backward jumps (fixed)
- `src/Asynkron.JsEngine/Ast/TypedAstEvaluator.ExecutionPlanRunner.cs:471-531` -- HandleBranchFastPath (no cancellation check)
- `src/Asynkron.JsEngine/Ast/TypedAstEvaluator.ExecutionPlanRunner.Handlers.ControlFlow.cs:223-247` -- HandleBranch slow path (no cancellation check)
- `src/Asynkron.JsEngine/StdLib/String/StringConstructor.cs:26-64` -- FromCodePoint (no cancellation check)
- `src/Asynkron.JsEngine/StdLib/Function/FunctionPrototype.cs:59-102` -- Apply (no cancellation check in loop)

## Evidence Collected

### Test Execution Results
All user-specified tests from every category pass cleanly:

```
# Class elements (static private methods): 82 tests -- ALL PASSED
# Generators (yield-as-*): 16 tests -- ALL PASSED
# Module top-level-await: included in above batch -- ALL PASSED
# Numeric separators: 17 tests -- ALL PASSED
# Eval with generators: 4 tests -- ALL PASSED
# Subtraction: 7 tests -- ALL PASSED
Total: 126+ tests, 0 failures, 0 crashes
```

### Crash Distribution Analysis
```
226  built-ins/RegExp/property-escapes/generated    <-- THE ACTUAL CRASHER (43% of all crashes)
 16  language/expressions/class/dstr                 <-- collateral
 15  language/statements/class/dstr                  <-- collateral
 15  built-ins/Object/defineProperty                 <-- collateral
 10  built-ins/decodeURI                             <-- collateral
  8  (12 different categories with 8 each)           <-- collateral
  ... remaining spread across 30+ categories
```

226 of 531 crashes (43%) are from RegExp property escapes. The remaining 305 are spread uniformly across 30+ unrelated categories in groups of 4-16, consistent with being bystanders in the same parallel batch.

### RegExp Property Escapes: Still Slow
The actual crash source (RegExp property escapes) no longer crashes but is still very slow:
```
Alphabetic.js:              28-29 seconds per variant
Script_Extensions_-_Limbu:  17-18 seconds per variant
Script_Extensions_-_Katakana: 24 seconds per variant
```
Despite `ExecutionTimeout = TimeSpan.FromSeconds(10)`, these tests run 2-3x beyond the timeout.

### Remaining Timeout Enforcement Gaps

#### Gap 1: BranchInstruction backward jumps not checked
`ExecutionPlanRunner.Loop.cs:233-242` checks cancellation on Jump backward jumps:
```csharp
// Jump instruction -- HAS cancellation check
if (instructionKind == InstructionKind.Jump)
{
    var target = ProfileHandleJump(...);
    if (target <= _programCounter)  // backward?
    {
        context.ThrowIfCancellationRequested();  // CHECK
    }
    _programCounter = target;
    continue;
}
```

But `HandleBranchFastPath` (line 471-531 of ExecutionPlanRunner.cs) does NOT:
```csharp
// Branch instruction -- NO cancellation check
runner._programCounter = testValue.IsTruthy
    ? instruction.ConsequentIndex   // could be backward!
    : instruction.AlternateIndex;
return InstructionResult.Continue;
```

Similarly, the slow-path `HandleBranch` (ControlFlow.cs:223-247) does not check.

**However**, for standard `for` loops this gap may not matter because:
- The loop's increment flows back to the condition via a **Jump** instruction (at `conditionJumpIndex`)
- That Jump is backward and DOES trigger the check
- The Branch at the condition tests and jumps forward to the body (higher index)

For `do-while` loops where `ConditionAfterBody=true`, the Branch at the bottom tests the condition and jumps backward to the body -- this case IS unchecked.

#### Gap 2: C# host functions don't check cancellation
The dominant time consumers are native C# functions:
- `StringConstructor.FromCodePoint` (line 26-64): iterates over 10,000 args in C# with no token check
- `FunctionPrototype.Apply` (line 59-102): the slow path has a C# loop with no token check

These run outside the IR execution loop, so the backward-jump check never fires during their execution.

## Root Cause Analysis

### Hypothesis 1 (Confirmed): Tests are collateral damage from RegExp crashes
**Confidence: HIGH**

The tests listed by the user never crash on their own. They were recorded as "crashed" because NUnit runs tests in parallel and when a RegExp property escape test caused OOM/StackOverflow, the entire dotnet process died, taking all concurrent tests with it.

- Evidence supporting: All 126+ tests pass when run individually or in batches. Distribution analysis shows 43% of crashes are from one test category. Non-regex tests appear in groups of 4-16, consistent with batch collateral.
- Evidence against: None.

### Hypothesis 2 (Contributing): Timeout enforcement is incomplete
**Confidence: MEDIUM**

While the CancellationToken check on backward Jump instructions was added, two gaps remain:
1. BranchInstruction backward jumps (matters for do-while loops)
2. C# host functions (FromCodePoint, Apply) that process large data

This explains why RegExp property escape tests take 28 seconds despite a 10-second timeout -- most time is spent in `FromCodePoint` processing 10,000 code points per call, called hundreds of times.

- Evidence supporting: Alphabetic.js takes 28s despite 10s timeout. Time is in C# host calls.
- Evidence against: For standard for-loops, the backward Jump to condition does check. The Branch gap only affects do-while.

## Recommended Fix

### Option A: Clear and regenerate the crash list (Immediate)
The `.testrunner/crashed-tests.txt` is stale. Re-run the testrunner to regenerate it. The non-regex tests should not appear.

### Option B: Add cancellation check to BranchInstruction handlers (Small fix)
In both `HandleBranchFastPath` and `HandleBranch`, add:
```csharp
// After computing target index:
if (target <= runner._programCounter)
{
    context.ThrowIfCancellationRequested();
}
```
This closes the do-while loop gap.

- Pros: Completes the timeout enforcement for all loop types
- Cons: Minor -- adds a branch to a hot path (but Branch is already heavyweight)

### Option C: Add cancellation check to FromCodePoint (Targeted)
For long-running host functions like `FromCodePoint`, periodically check:
```csharp
// Every N iterations inside the foreach loop:
if (++count % 4096 == 0) context.ThrowIfCancellationRequested();
```

- Pros: Would enforce timeout during the actual slow path
- Cons: Requires threading EvaluationContext/CancellationToken into host function signatures, which may need architectural changes

## Test Plan
- [ ] Re-run testrunner to regenerate crashed-tests.txt
- [ ] Verify non-regex tests from the crash list are no longer reported as crashed
- [ ] Add Branch cancellation check and verify do-while loop timeout works
- [ ] Run full internal test suite: `dotnet test tests/Asynkron.JsEngine.Tests`
- [ ] Run RegExp property escapes batch to verify no regressions

## Additional Notes
- The 226 RegExp property escapes tests still in the crash list are also likely stale. After the `48decb1e` fix, these tests pass (albeit slowly at 17-29 seconds each). They may still cause issues under heavy parallel load due to combined memory pressure, but they no longer cause StackOverflow/OOM individually.
- The crash list mechanism appears to be: when a dotnet process dies, all tests that hadn't reported a result yet get classified as "crashed." This is an accurate detection method but doesn't isolate the culprit from bystanders.
- Future improvement: the testrunner could run previously-crashed tests in isolation (one per process) to distinguish true crashers from collateral.
