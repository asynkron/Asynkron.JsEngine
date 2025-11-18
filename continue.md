# Generator IR Next Steps

## Current State
- IR now includes `JumpInstruction`, `StoreResumeValueInstruction`, and a delegated `YieldStarInstruction`, so `.next/.throw/.return` payloads are captured without replaying statements and delegated iterators remain on the IR fast path.
- The builder lowers blocks, expression statements (including `yield*`), `while`, `do/while`, classic `for` loops (with labels), variable declarations, plain assignments (`target = yield <expr>`), and now `try/catch/finally` statements by emitting hidden slots and explicit IR instructions.
- Loop scopes track break/continue targets and now emit dedicated `BreakInstruction`/`ContinueInstruction` nodes so loop exits unwind active `finally` blocks before resuming.
- `StoreResumeValueInstruction` consumes pending `.next/.throw/.return` payloads; `.throw`/`.return` flow through the interpreter before short-circuiting so try/catch/finally blocks can observe them, and a try-frame stack guarantees finally blocks execute during abrupt completion (including nested finalizers and mid-final `.throw/.return` overrides).
- Tests `Generator_TryCatchHandlesThrowIr`, `Generator_TryFinallyRunsOnReturnIr`, `Generator_TryFinallyRunsOnThrowIr`, `Generator_TryFinallyNestedThrowIr`, `Generator_TryFinallyNestedReturnIr`, `Generator_TryFinallyThrowMidFinalIr`, `Generator_TryFinallyReturnMidFinalIr`, `Generator_CatchFinallyNestedThrowIr`, `Generator_CatchFinallyNestedReturnIr`, `Generator_YieldStar_DelegatesValues`, `Generator_YieldStarReceivesSentValuesIr`, `Generator_YieldStarThrowDeliversCleanupIr`, `Generator_YieldStarReturnDeliversCleanupIr`, `Generator_ForAwaitFallsBackIr`, `Generator_ForAwaitAsyncIteratorAwaitsValuesIr`, `Generator_ForAwaitPromiseValuesAreAwaitedIr`, `Generator_ForAwaitAsyncIteratorRejectsPropagatesIr`, `Generator_BreakRunsFinallyIr`, `Generator_ContinueRunsFinallyIr`, `Generator_DoWhileLoopsExecuteWithIrPlan`, `Generator_ForLoopsExecuteWithIrPlan`, `Generator_ForLoopContinueRunsIncrement`, `Generator_AssignmentReceivesSentValuesIr`, `Generator_ReturnSkipsRemainingStatementsIr`, and `Generator_ThrowSkipsRemainingStatementsIr` cover the new IR behavior and documented guardrails.
- `docs/GENERATOR_IR_LIMITATIONS.md` captures which generator constructs lower to IR, which ones intentionally fall back, and what follow-up work is still open.

## Next Iteration Plan

1. **For-Await IR Execution**
   - Move the new promise-aware behavior into the generator IR path so `for await...of` loops no longer fall back to the legacy evaluator. This likely means adding dedicated instructions to await iterator results/value payloads and wiring them into the event queue.
   - Ensure awaited promises resume the generator without replay, and propagate `.throw/.return` through the iterator (including rejection paths). Use the new tests (`Generator_ForAwaitAsyncIteratorAwaitsValuesIr`, `Generator_ForAwaitPromiseValuesAreAwaitedIr`, `Generator_ForAwaitAsyncIteratorRejectsPropagatesIr`) as the acceptance bar.
   - Once IR support lands, clean up the legacy evaluator hook to just be a fallback and update docs to reflect the expanded fast path.

2. **Delegated Spec Parity**
   - Add coverage for exotic `iterator.throw/return` combinations (e.g., custom iterators that return non-completion objects) and adjust the new `YieldStarInstruction` pipeline if gaps surface.
   - Document any remaining differences between our delegated semantics and the ECMAScript algorithm so future work can focus on spec compliance.
