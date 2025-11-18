# Generator IR Next Steps

## Current State
- IR now includes `JumpInstruction`, `StoreResumeValueInstruction`, and a delegated `YieldStarInstruction`, so `.next/.throw/.return` payloads are captured without replaying statements and delegated iterators remain on the IR fast path.
- The builder lowers blocks, expression statements (including `yield*`), `while`, `do/while`, classic `for` loops (with labels), variable declarations, plain assignments (`target = yield <expr>`), and now `try/catch/finally` statements by emitting hidden slots and explicit IR instructions.
- Loop scopes track break/continue targets and now emit dedicated `BreakInstruction`/`ContinueInstruction` nodes so loop exits unwind active `finally` blocks before resuming.
- `StoreResumeValueInstruction` consumes pending `.next/.throw/.return` payloads; `.throw`/`.return` flow through the interpreter before short-circuiting so try/catch/finally blocks can observe them, and a try-frame stack guarantees finally blocks execute during abrupt completion (including nested finalizers and mid-final `.throw/.return` overrides).
- Tests `Generator_TryCatchHandlesThrowIr`, `Generator_TryFinallyRunsOnReturnIr`, `Generator_TryFinallyRunsOnThrowIr`, `Generator_TryFinallyNestedThrowIr`, `Generator_TryFinallyNestedReturnIr`, `Generator_TryFinallyThrowMidFinalIr`, `Generator_TryFinallyReturnMidFinalIr`, `Generator_CatchFinallyNestedThrowIr`, `Generator_CatchFinallyNestedReturnIr`, `Generator_YieldStar_DelegatesValues`, `Generator_YieldStarReceivesSentValuesIr`, `Generator_YieldStarThrowDeliversCleanupIr`, `Generator_YieldStarReturnDeliversCleanupIr`, `Generator_YieldStarThrowRequiresIteratorResultObjectIr`, `Generator_YieldStarReturnRequiresIteratorResultObjectIr`, `Generator_YieldStarThrowRequiresIteratorResultObjectInterpreter`, `Generator_YieldStarReturnRequiresIteratorResultObjectInterpreter`, `Generator_ForAwaitFallsBackIr`, `Generator_ForAwaitAsyncIteratorAwaitsValuesIr`, `Generator_ForAwaitPromiseValuesAreAwaitedIr`, `Generator_ForAwaitAsyncIteratorRejectsPropagatesIr`, `Generator_BreakRunsFinallyIr`, `Generator_ContinueRunsFinallyIr`, `Generator_DoWhileLoopsExecuteWithIrPlan`, `Generator_ForLoopsExecuteWithIrPlan`, `Generator_ForLoopContinueRunsIncrement`, `Generator_AssignmentReceivesSentValuesIr`, `Generator_ReturnSkipsRemainingStatementsIr`, and `Generator_ThrowSkipsRemainingStatementsIr` cover the new IR behavior and documented guardrails.
- `docs/GENERATOR_IR_LIMITATIONS.md` captures which generator constructs lower to IR, which ones intentionally fall back, and what follow-up work is still open.

## Next Iteration Plan

1. **Delegated Spec Parity**
   - Now that non-object iterator results are guarded (IR + interpreter), extend coverage to promise-returning completion records and async iterators so we know whether the current await plumbing matches the spec.
   - Document remaining differences between our delegated semantics and the ECMAScript algorithm (e.g., how we brand generator objects, how we stash delegated state) so the next contributor knows exactly what gaps remain.

2. **Async Await Scheduling**
   - `TryAwaitPromise` currently blocks the managed thread until a promise settles. Investigate integrating the event queue (e.g., resume generators via `ScheduleTask`) so long-running promises yield control instead of blocking.
   - Explore exposing instrumentation hooks (trace or debug) so we can observe nested awaits inside generators and detect potential starvation.
