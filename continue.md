# Generator IR Next Steps

## Current State
- IR now includes `JumpInstruction` and `StoreResumeValueInstruction`, so `.next(value)` payloads are captured without replaying statements.
- The builder lowers blocks, expression statements, `while`, `do/while`, classic `for` loops (with labels), variable declarations, plain assignments (`target = yield <expr>`), and now `try/catch/finally` statements by emitting hidden slots and explicit IR instructions.
- Loop scopes track break/continue targets and now emit dedicated `BreakInstruction`/`ContinueInstruction` nodes so loop exits unwind active `finally` blocks before resuming.
- `StoreResumeValueInstruction` now consumes pending `.next/.throw/.return` payloads; `.throw`/`.return` flow through the interpreter before short-circuiting so try/catch/finally blocks can observe them, and a try-frame stack guarantees finally blocks execute during abrupt completion (including nested finalizers and mid-final `.throw/.return` overrides).
- Tests `Generator_TryCatchHandlesThrowIr`, `Generator_TryFinallyRunsOnReturnIr`, `Generator_TryFinallyRunsOnThrowIr`, `Generator_TryFinallyNestedThrowIr`, `Generator_TryFinallyNestedReturnIr`, `Generator_TryFinallyThrowMidFinalIr`, `Generator_TryFinallyReturnMidFinalIr`, `Generator_CatchFinallyNestedThrowIr`, `Generator_CatchFinallyNestedReturnIr`, `Generator_YieldStarThrowDeliversCleanupIr`, `Generator_YieldStarReturnDeliversCleanupIr`, `Generator_ForAwaitFallsBackIr`, `Generator_BreakRunsFinallyIr`, `Generator_ContinueRunsFinallyIr`, `Generator_DoWhileLoopsExecuteWithIrPlan`, `Generator_ForLoopsExecuteWithIrPlan`, `Generator_ForLoopContinueRunsIncrement`, `Generator_AssignmentReceivesSentValuesIr`, `Generator_ReturnSkipsRemainingStatementsIr`, and `Generator_ThrowSkipsRemainingStatementsIr` cover the new IR behavior.

## Next Iteration Plan

1. **Boundary Documentation**
   - Capture which generator constructs still fall back to the replay runner (delegated `yield*`, `for await`, async generators) and document how/why so contributors know the supported envelope.
   - Note any behavioral gaps (e.g., delegated `.throw/.return` semantics, promise-based async iterators) plus pointers to existing tests that lock current behavior.

2. **Delegation & Async Planning**
   - Outline the work needed for IR support of delegated `yield*` and async iteration constructs (e.g., extra resume slots, iterator protocol hooks) so the next iteration can prioritize a spike.
