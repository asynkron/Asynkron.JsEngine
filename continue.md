# Generator IR Next Steps

## Current State
- IR now includes `JumpInstruction` and `StoreResumeValueInstruction`, so `.next(value)` payloads are captured without replaying statements.
- The builder lowers blocks, expression statements, `while` loops (labeled or unlabeled), and variable declarations whose initializer is a direct `yield`, synthesizing hidden slots for resume values.
- Loop scopes now track break/continue targets, so both unlabeled and labeled `break`/`continue` statements are emitted as jumps in the plan.
- The interpreter executes the new instruction types, stages pending resume payloads, and still falls back to the replay runner for unsupported programs.
- Tests `Generator_WhileLoopsExecuteWithIrPlan`, `Generator_IrPathReceivesSentValues`, and the new break/continue cases validate the IR path.

## Next Iteration Plan

1. **Resume Propagation**
   - Support direct assignments that contain `yield` (e.g., `value = yield expr;`) by reusing the resume-slot mechanism instead of forcing fallback.
   - Route `.throw(value)` / `.return(value)` payloads through the IR interpreter so try/catch/finally blocks can observe them without replaying.

2. **Loop Coverage**
   - Lower `do/while` and classic `for` loops using the existing branch/jump primitives and loop-scope bookkeeping.
   - Keep rejecting constructs we still can’t lower (`yield*`, nested yields in expressions, `for...of`, try/finally) so unsupported programs fall back cleanly.

3. **Testing**
   - Add IR-path tests for assignment-style `yield` consumption and for `.throw`/`.return` interacting with try/catch.
   - Add regression tests for `do/while` and `for` loops once supported, plus edge cases around nested labeled loops.
