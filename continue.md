# Generator IR Next Steps

## Current State
- IR now includes `JumpInstruction`, `StoreResumeValueInstruction`, and a delegated `YieldStarInstruction`, so `.next/.throw/.return` payloads are captured without replaying statements and delegated iterators remain on the IR fast path.
- The builder lowers blocks, expression statements (including `yield*`), `while`, `do/while`, classic `for` loops (with labels), `if/else`, variable declarations, plain assignments (`target = yield <expr>`), and `try/catch/finally` statements by emitting hidden slots and explicit IR instructions.
- Loop scopes track break/continue targets and now emit dedicated `BreakInstruction`/`ContinueInstruction` nodes so loop exits unwind active `finally` blocks before resuming, including across delegated `yield*` frames.
- `StoreResumeValueInstruction` consumes pending `.next/.throw/.return` payloads; `.throw`/`.return` flow through the interpreter before short-circuiting so try/catch/finally blocks can observe them, and a try-frame stack guarantees finally blocks execute during abrupt completion (including nested finalizers and mid-final `.throw/.return` overrides).
- Delegated `yield*` now awaits promise-returning iterator completions on both paths: `DelegatedYieldState.MoveNext` feeds promise-like `next/throw/return` results through `TryAwaitPromise`, and the IR `YieldStarInstruction` path now plumbs delegated `.throw/.return` completion (including async rejections) back through `HandleAbruptCompletion` and `CompleteReturn` so generators see the final completion record rather than the inner cleanup value.
- Tests `Generator_YieldStar_DelegatesValues`, `Generator_YieldStar_ReturnValueUsedByOuterGenerator`, `Generator_YieldStarReceivesSentValuesIr`, `Generator_YieldStarThrowDeliversCleanupIr`, `Generator_YieldStarReturnDeliversCleanupIr`, `Generator_YieldStarThrowContinuesWhenIteratorResumesIr`, `Generator_YieldStarThrowRequiresIteratorResultObjectIr`, `Generator_YieldStarReturnRequiresIteratorResultObjectIr`, `Generator_YieldStarThrowRequiresIteratorResultObjectInterpreter`, `Generator_YieldStarReturnRequiresIteratorResultObjectInterpreter`, `Generator_YieldStarThrowAwaitedPromiseIr`, `Generator_YieldStarThrowAwaitedPromiseInterpreter`, `Generator_YieldStarThrowPromiseRejectsIr`, `Generator_YieldStarThrowPromiseRejectsInterpreter`, `Generator_YieldStarReturnAwaitedPromiseIr`, `Generator_YieldStarReturnAwaitedPromiseInterpreter`, and `Generator_YieldStarReturnDoneFalseContinuesIr` now lock in delegated `.next/.throw/.return` semantics for synchronous iterators, promise-returning completions, and rejection propagation (IR + interpreter).
- `for await...of` remains on the replay path, but tests `Generator_ForAwaitFallsBackIr`, `Generator_ForAwaitAsyncIteratorAwaitsValuesIr`, `Generator_ForAwaitPromiseValuesAreAwaitedIr`, and `Generator_ForAwaitAsyncIteratorRejectsPropagatesIr` verify that async iterators and promise-valued elements are awaited and that rejections surface as `ThrowSignal`s.
- Remaining generator IR mismatches are isolated to nested `try/finally` stacks interacting with `.throw/.return` across multiple `finally` frames (`Generator_TryFinallyNestedThrowIr`, `Generator_TryFinallyNestedReturnIr`). All other `Generator_*Ir` tests are green.
- `docs/GENERATOR_IR_LIMITATIONS.md` captures which generator constructs lower to IR, which ones intentionally fall back, and what follow-up work is still open.

## Next Iteration Plan

1. **Unify Generator Semantics (Replay + IR)**
   - Refactor generator `try/catch/finally` so the replay engine and IR interpreter share a common model for pending completions:
     - Introduce a “pending completion” concept in the replay path (mirroring IR `TryFrame.PendingCompletion`) so a `.throw/.return` captured before entering `finally` survives all nested `finally` yields and is only applied once the outermost finalizer completes (unless overridden by an inner `throw/return` in `finally`).
     - Use the shared model to bring `Generator_TryFinallyNestedThrowIr` and `Generator_TryFinallyNestedReturnIr` to green on both paths without relying on ad‑hoc fallbacks.
   - While doing this, audit how `HandleAbruptCompletion` + `PendingCompletion` interact with `YieldInstruction` / `YieldStarInstruction` so abrupt completions never get “downgraded” when multiple `finally` blocks re-schedule the same completion.

2. **Expand IR Coverage to Match Replay**
   - Identify remaining generator constructs that only run on the replay engine today (complex `yield` placements, nested `try` inside `finally`, any other shapes documented in `GENERATOR_IR_LIMITATIONS.md`).
   - Incrementally extend `GeneratorIrBuilder` + IR interpreter to cover those shapes while reusing the unified completion model from step 1, so all existing generator tests can run on the IR path without semantic drift.

3. **Make Fallbacks Explicit and Measurable**
   - Add lightweight instrumentation (trace or counters) around IR lowering so we know when a generator body falls back to replay and why (e.g., “contains nested try in finally”, “yield inside unsupported expression shape”).
   - Use this to drive a small “no‑fallback” test set (e.g., all `Generator_*Ir` tests) that must lower to IR; any fallback within that set should be treated as a regression.

4. **Sunset the Replay Generator Path**
   - Once all generator constructs we care about either:
     - successfully lower to IR with spec‑correct semantics, or
     - are explicitly rejected at parse time as unsupported,
     replace the replay engine for generators with a thin compatibility shim (or remove it entirely):
     - Make `GeneratorIrBuilder` the only execution path for supported generator bodies; fail fast (with a clear error) when the builder cannot produce a plan.
     - Remove `YieldTracker` / `YieldResumeContext`‑based replay logic for generators once all tests are green on IR and we have no remaining legitimate fallbacks.

5. **Async Await Scheduling (Post‑Sunset)**
   - After the replay generator is no longer in the hot path, revisit `TryAwaitPromise` and generator scheduling:
     - Integrate the event queue (e.g., resume generators via `ScheduleTask`) so long-running promises in generators don’t block the managed thread.
     - Optionally expose instrumentation hooks (trace or debug) so we can observe nested awaits inside generators and detect potential starvation.
