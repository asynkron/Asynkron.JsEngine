# Generator IR Next Steps

## Current State
- IR now includes `JumpInstruction` and `StoreResumeValueInstruction`, so `.next(value)` payloads are captured without replaying statements.
- The builder lowers blocks, expression statements, `while` loops, and variable declarations whose initializer is a direct `yield`, synthesizing hidden slots for resume values.
- Every lowered `yield` emits the store instruction, so resume payloads are consumed consistently.
- The interpreter executes the new instruction types, stages pending resume payloads, and still falls back to the replay runner for unsupported programs.
- Tests `Generator_WhileLoopsExecuteWithIrPlan` and `Generator_IrPathReceivesSentValues` verify the new IR paths.

## Next Iteration Plan

1. **Structured Control Flow**
   - Emit IR for `break`/`continue` (including labeled forms) so loops behave correctly inside the plan.
   - Extend the interpreter to respect these control-flow instructions without falling back.

2. **Resume Propagation**
   - Support direct assignments that include `yield` (e.g., `value = yield foo();`) by lowering them to resume slots similar to declarations.
   - Begin routing `.throw`/`.return` payloads through the IR interpreter (instead of short-circuiting) so try/catch/finally can observe them.

3. **Additional Loops**
   - Lower `do/while` and classic `for` loops using the new jump/branch infrastructure.
   - Continue rejecting complex patterns (`yield*`, nested yields inside expressions we can't yet lower) to preserve graceful fallback.

4. **Testing**
   - Add coverage for `break`/`continue` inside IR-managed loops (including labeled forms).
   - Add IR-path tests for `.throw`/`.return` interacting with try/catch and for `do/while` or `for` loops.
