# Generator IR Next Steps

## Current State
- IR now includes `JumpInstruction`, `StoreResumeValueInstruction`, and a delegated `YieldStarInstruction`, so `.next/.throw/.return` payloads are captured without replaying statements and delegated iterators remain on the IR fast path.
- The builder lowers blocks, expression statements (including `yield*`), `while`, `do/while`, classic `for` loops (with labels), variable declarations, plain assignments (`target = yield <expr>`), and now `try/catch/finally` statements by emitting hidden slots and explicit IR instructions.
- Loop scopes track break/continue targets and now emit dedicated `BreakInstruction`/`ContinueInstruction` nodes so loop exits unwind active `finally` blocks before resuming.
- `StoreResumeValueInstruction` consumes pending `.next/.throw/.return` payloads; `.throw`/`.return` flow through the interpreter before short-circuiting so try/catch/finally blocks can observe them, and a try-frame stack guarantees finally blocks execute during abrupt completion (including nested finalizers and mid-final `.throw/.return` overrides).
- Tests `Generator_TryCatchHandlesThrowIr`, `Generator_TryFinallyRunsOnReturnIr`, `Generator_TryFinallyRunsOnThrowIr`, `Generator_TryFinallyNestedThrowIr`, `Generator_TryFinallyNestedReturnIr`, `Generator_TryFinallyThrowMidFinalIr`, `Generator_TryFinallyReturnMidFinalIr`, `Generator_CatchFinallyNestedThrowIr`, `Generator_CatchFinallyNestedReturnIr`, `Generator_YieldStar_DelegatesValues`, `Generator_YieldStarReceivesSentValuesIr`, `Generator_YieldStarThrowDeliversCleanupIr`, `Generator_YieldStarReturnDeliversCleanupIr`, `Generator_ForAwaitFallsBackIr`, `Generator_ForAwaitAsyncIteratorThrowsIr`, `Generator_ForAwaitPromiseValuesAreNotAwaitedIr`, `Generator_BreakRunsFinallyIr`, `Generator_ContinueRunsFinallyIr`, `Generator_DoWhileLoopsExecuteWithIrPlan`, `Generator_ForLoopsExecuteWithIrPlan`, `Generator_ForLoopContinueRunsIncrement`, `Generator_AssignmentReceivesSentValuesIr`, `Generator_ReturnSkipsRemainingStatementsIr`, and `Generator_ThrowSkipsRemainingStatementsIr` cover the new IR behavior and documented guardrails.
- `docs/GENERATOR_IR_LIMITATIONS.md` captures which generator constructs lower to IR, which ones intentionally fall back, and what follow-up work is still open.

## Next Iteration Plan

1. **For-Await Execution**
   - Decide on a strategy for async iterables: extend the IR interpreter/event loop so `for await...of` can await promises, or enhance the legacy evaluator so it can consume promise-based async iterators (guardrail tests `Generator_ForAwaitAsyncIteratorThrowsIr` / `Generator_ForAwaitPromiseValuesAreNotAwaitedIr` capture the current gap).
   - Spike a proof of concept that awaits real promises (and propagates rejections) so generators can consume async sources without raising host exceptions.

2. **Delegated Spec Parity**
   - Add coverage for exotic `iterator.throw/return` combinations (e.g., custom iterators that return non-completion objects) and adjust the new `YieldStarInstruction` pipeline if gaps surface.
   - Document any remaining differences between our delegated semantics and the ECMAScript algorithm so future work can focus on spec compliance.
