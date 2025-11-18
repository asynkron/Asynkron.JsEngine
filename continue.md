# Generator IR Next Steps

## Current State
- IR now includes `JumpInstruction` and `StoreResumeValueInstruction`, so `.next(value)` payloads are captured without replaying statements.
- The builder lowers blocks, expression statements, `while`, `do/while`, and classic `for` loops (including labeled forms), variable declarations, and plain assignments of the form `target = yield <expr>`, creating hidden resume slots as needed.
- Loop scopes track break/continue targets, so both unlabeled and labeled `break`/`continue` statements become jumps inside the plan.
- `StoreResumeValueInstruction` now consumes pending `.next/.throw/.return` payloads; `.throw`/`.return` flow through the interpreter before short-circuiting so upcoming try/catch support can observe them.
- Tests `Generator_IrPathReceivesSentValues`, `Generator_AssignmentReceivesSentValuesIr`, `Generator_DoWhileLoopsExecuteWithIrPlan`, `Generator_ForLoopsExecuteWithIrPlan`, `Generator_ForLoopContinueRunsIncrement`, `Generator_ReturnSkipsRemainingStatementsIr`, `Generator_ThrowSkipsRemainingStatementsIr`, and the loop-control cases exercise the new IR behavior.

## Next Iteration Plan

1. **Exception Propagation**
   - Add IR lowering for `try/catch/finally` so `.throw/.return` payloads delivered via `StoreResumeValueInstruction` can be intercepted mid-plan.
   - Decide whether we need explicit instructions (e.g., `EnterTry`, `LeaveTry`, `EndFinally`) or can re-use statement instructions while pushing/popping a try stack during interpretation.

2. **Additional Constructs**
   - Explore lowering simple `for...of` (non-async) loops using existing iterator helpers once try/catch handling lands.
   - Continue rejecting `yield*`, nested yields inside complex expressions, and `try/finally` until the new IR can faithfully represent them.

3. **Testing & Guardrails**
   - Add IR-path tests for try/catch/finally once emitted, ensuring `.throw/.return` interactions, nested loops, and finally blocks behave identically to the fallback interpreter.
   - Cover `for...of` and fallback behavior once supported, keeping labeled `break/continue` validated via regression tests.
