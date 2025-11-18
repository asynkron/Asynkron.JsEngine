# Generator IR Next Steps

## Current State
- IR now includes `JumpInstruction` and `StoreResumeValueInstruction`, so `.next(value)` payloads are captured without replaying statements.
- The builder lowers blocks, expression statements, `while`, `do/while`, classic `for` loops (with labels), variable declarations, plain assignments (`target = yield <expr>`), and now `try/catch/finally` statements by emitting hidden slots and explicit IR instructions.
- Loop scopes track break/continue targets and now emit dedicated `BreakInstruction`/`ContinueInstruction` nodes so loop exits unwind active `finally` blocks before resuming.
- `StoreResumeValueInstruction` now consumes pending `.next/.throw/.return` payloads; `.throw`/`.return` flow through the interpreter before short-circuiting so try/catch/finally blocks can observe them, and a try-frame stack guarantees finally blocks execute during abrupt completion (including nested finalizers and mid-final `.throw/.return` overrides).
- Tests `Generator_TryCatchHandlesThrowIr`, `Generator_TryFinallyRunsOnReturnIr`, `Generator_TryFinallyRunsOnThrowIr`, `Generator_TryFinallyNestedThrowIr`, `Generator_TryFinallyNestedReturnIr`, `Generator_TryFinallyThrowMidFinalIr`, `Generator_TryFinallyReturnMidFinalIr`, `Generator_BreakRunsFinallyIr`, `Generator_ContinueRunsFinallyIr`, `Generator_DoWhileLoopsExecuteWithIrPlan`, `Generator_ForLoopsExecuteWithIrPlan`, `Generator_ForLoopContinueRunsIncrement`, `Generator_AssignmentReceivesSentValuesIr`, `Generator_ReturnSkipsRemainingStatementsIr`, and `Generator_ThrowSkipsRemainingStatementsIr` cover the new IR behavior.

## Next Iteration Plan

1. **Catch/Finally Interaction**
   - Evaluate whether `try/finally` inside `catch` needs extra metadata (e.g., separate catch-value slots) to prevent stale values when nested, and add targeted coverage if there is a gap.

2. **Guardrails**
   - Confirm unsupported constructs still fall back cleanly (e.g., `yield*`, `for await`) and add negative tests if needed.
   - Document remaining gaps (async generators, `for...of` with destructuring, etc.) so future iterations know the boundaries.
