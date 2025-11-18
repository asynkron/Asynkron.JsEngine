# Generator IR Next Steps

## Current State
- `Execution/GeneratorIr.cs` defines the instruction set (`StatementInstruction`, `YieldInstruction`, `ReturnInstruction`, `BranchInstruction`).
- `Execution/GeneratorIrBuilder.cs` lowers generator bodies that contain:
  - Blocks, expression statements, variable declarations without nested yields.
  - Top-level `yield <expr>` and `return <expr>`.
  - Basic `if/else` (emits `BranchInstruction`).
- `TypedGeneratorInstance` builds a `GeneratorPlan` when possible and executes it synchronously (else falls back to the replay runner). The IR interpreter currently handles statements, yields, returns, and branches.
- Tests `Generator_CanReceiveSentValues` (fallback path) and `Generator_IfBranchesExecute` (IR path) pass.

## Next Iteration Plan

1. **IR Enhancements**
   - Add `JumpInstruction` for unconditional jumps so loops can return to their condition.
   - Introduce instructions to store/load `.next(value)` payloads (e.g., `StoreResumeValue`, `LoadResumeValue`, or synthetic locals) so resumed `yield` expressions can consume sent values without replaying.

2. **Builder Improvements**
   - Lower `while` loops by emitting:
     - Instructions for the loop body.
     - A branch that tests the condition and jumps to either the body or the loop exit.
     - A `JumpInstruction` from the end of the body back to the condition.
   - Around each `yield`, emit instructions that capture the resumed value into a predictable slot so subsequent statements can read it (mimicking `const value = yield expr;`).
   - Continue rejecting unsupported constructs (yield inside complex expressions, `yield*`, `for` loops, try/finally, etc.) so we fall back gracefully.

3. **Interpreter Updates**
   - Execute the new `JumpInstruction` and resume-value load/store instructions.
   - When `.next(value)` runs, push the payload into the interpreter state before executing the next instruction.
   - `.throw`/`.return` should keep their short-circuit behavior but eventually route through the IR so try/catch can intercept.

4. **Testing**
   - Add generator tests covering:
     - `while` loops (`function* gen() { while (i < n) yield i++; }`).
     - Consuming `.next(value)` after a `yield` on the IR path (e.g., `const sent = yield; yield sent * 2;`).
   - Ensure fallback still works for unsupported patterns (existing tests cover this, but add more if needed).

## Notes
- Sent-value support may require synthesizing a local `Symbol` per `yield` to store the resumed value so existing binding logic can reuse `environment.Assign(...)`.
- Eventually we should emit IR for other constructs (`for`, `yield*`, try/finally) but loops + sent values are the immediate goal.
