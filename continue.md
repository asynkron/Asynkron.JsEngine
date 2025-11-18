# Generator IR Next Steps

## Current State
- IR now includes `JumpInstruction`, `StoreResumeValueInstruction`, and a delegated `YieldStarInstruction`, so `.next/.throw/.return` payloads are captured without replaying statements and delegated iterators remain on the IR fast path.
- The builder lowers blocks, expression statements (including `yield*`), `while`, `do/while`, classic `for` loops (with labels), `if/else`, variable declarations, plain assignments (`target = yield <expr>`), and `try/catch/finally` statements by emitting hidden slots and explicit IR instructions.
- Loop scopes track break/continue targets and now emit dedicated `BreakInstruction`/`ContinueInstruction` nodes so loop exits unwind active `finally` blocks before resuming, including across delegated `yield*` frames.
- `StoreResumeValueInstruction` consumes pending `.next/.throw/.return` payloads; `.throw`/`.return` flow through the interpreter before short-circuiting so try/catch/finally blocks can observe them, and a try-frame stack guarantees finally blocks execute during abrupt completion (including nested finalizers and mid-final `.throw/.return` overrides).
- Delegated `yield*` now awaits promise-returning iterator completions on both paths: `DelegatedYieldState.MoveNext` feeds promise-like `next/throw/return` results through `TryAwaitPromise`, and the IR `YieldStarInstruction` path now plumbs delegated `.throw/.return` completion (including async rejections) back through `HandleAbruptCompletion` and `CompleteReturn` so generators see the final completion record rather than the inner cleanup value.
- Tests `Generator_YieldStar_DelegatesValues`, `Generator_YieldStar_ReturnValueUsedByOuterGenerator`, `Generator_YieldStarReceivesSentValuesIr`, `Generator_YieldStarThrowDeliversCleanupIr`, `Generator_YieldStarReturnDeliversCleanupIr`, `Generator_YieldStarThrowContinuesWhenIteratorResumesIr`, `Generator_YieldStarThrowRequiresIteratorResultObjectIr`, `Generator_YieldStarReturnRequiresIteratorResultObjectIr`, `Generator_YieldStarThrowRequiresIteratorResultObjectInterpreter`, `Generator_YieldStarReturnRequiresIteratorResultObjectInterpreter`, `Generator_YieldStarThrowAwaitedPromiseIr`, `Generator_YieldStarThrowAwaitedPromiseInterpreter`, `Generator_YieldStarThrowPromiseRejectsIr`, `Generator_YieldStarThrowPromiseRejectsInterpreter`, `Generator_YieldStarReturnAwaitedPromiseIr`, `Generator_YieldStarReturnAwaitedPromiseInterpreter`, and `Generator_YieldStarReturnDoneFalseContinuesIr` now lock in delegated `.next/.throw/.return` semantics for synchronous iterators, promise-returning completions, and rejection propagation (IR + interpreter).
- `for await...of` remains on the replay path, but tests `Generator_ForAwaitFallsBackIr`, `Generator_ForAwaitAsyncIteratorAwaitsValuesIr`, `Generator_ForAwaitPromiseValuesAreAwaitedIr`, and `Generator_ForAwaitAsyncIteratorRejectsPropagatesIr` verify that async iterators and promise-valued elements are awaited and that rejections surface as `ThrowSignal`s.
- Remaining generator IR mismatches are isolated to a small set of tests: `Generator_ForOfLetCreatesNewBindingIr` (loop-scoped `let` capture in `for...of`), and `Generator_TryFinallyNestedThrowIr` / `Generator_TryFinallyNestedReturnIr` (nested `try/finally` stacks interacting with `.throw/.return` across multiple `finally` frames). All other `Generator_*Ir` tests are green.
- `docs/GENERATOR_IR_LIMITATIONS.md` captures which generator constructs lower to IR, which ones intentionally fall back, and what follow-up work is still open.

## Next Iteration Plan

1. **Nested Try/Finally IR Parity**
   - Align IR handling of nested `try/finally` with the replay engine so `Generator_TryFinallyNestedThrowIr` and `Generator_TryFinallyNestedReturnIr` see the same completion value as the interpreter (propagating the final `.throw/.return` payload after all cleanup yields run, rather than stopping at the innermost `finally` value).
   - Audit how `HandleAbruptCompletion` + `PendingCompletion` interact with `YieldStarInstruction` and direct `YieldInstruction` frames so abrupt completions never get “downgraded” when multiple `finally` blocks re-schedule the same completion.

2. **For-of Let Binding Semantics**
   - Fix `for...of` lowering so `Generator_ForOfLetCreatesNewBindingIr` matches spec: each loop iteration with `let` (and destructuring) must receive a fresh binding environment, ensuring captured callbacks see the per-iteration value instead of the last one.
   - Keep the IR fast path by rewriting the synthetic per-iteration block / loop environment rather than falling back to the replay engine.

3. **Async Await Scheduling**
   - `TryAwaitPromise` currently blocks the managed thread until a promise settles. Investigate integrating the event queue (e.g., resume generators via `ScheduleTask`) so long-running promises yield control instead of blocking.
   - Explore exposing instrumentation hooks (trace or debug) so we can observe nested awaits inside generators and detect potential starvation.
