# Generator IR Next Steps

## Current State
- IR now includes `JumpInstruction` and `StoreResumeValueInstruction`, so `.next(value)` payloads are captured without replaying statements.
- The builder lowers blocks, expression statements, `while`, `do/while`, classic `for` loops (with labels), variable declarations, plain assignments (`target = yield <expr>`), and now `try/catch/finally` statements by emitting hidden slots and explicit IR instructions.
- Loop scopes track break/continue targets, so both unlabeled and labeled `break`/`continue` statements become jumps inside the plan.
- `StoreResumeValueInstruction` now consumes pending `.next/.throw/.return` payloads; `.throw`/`.return` flow through the interpreter before short-circuiting so try/catch/finally blocks can observe them, and a try-frame stack guarantees finally blocks execute during abrupt completion.
- Tests `Generator_TryCatchHandlesThrowIr`, `Generator_TryFinallyRunsOnReturnIr`, `Generator_TryFinallyRunsOnThrowIr`, `Generator_DoWhileLoopsExecuteWithIrPlan`, `Generator_ForLoopsExecuteWithIrPlan`, `Generator_ForLoopContinueRunsIncrement`, `Generator_AssignmentReceivesSentValuesIr`, `Generator_ReturnSkipsRemainingStatementsIr`, and `Generator_ThrowSkipsRemainingStatementsIr` cover the new IR behavior.

## Next Iteration Plan

1. **Abrupt Completion Coverage**
   - Revisit how `break`/`continue` (currently emitted as raw jumps) interact with `finally` blocks so loop exits still trigger cleanup.
   - Ensure nested try/finally stacks handle combinations of `break`, `continue`, and `return` the same way as the replay interpreter.

2. **Additional Constructs**
   - Lower simple `for...of` (non-async) loops onto the IR path, reusing the existing iterator helpers and honoring labeled `break`/`continue`.
   - Keep rejecting `yield*`, nested yields inside complex expressions, and async iteration until the control-flow model can cover them.

3. **Testing & Guardrails**
   - Expand coverage for nested try/finally scenarios, `break/continue` inside finally blocks, and `.throw/.return` delivered mid-finalizer.
   - Add tests for the eventual `for...of` lowering plus negative tests that confirm unsupported constructs still fall back.
