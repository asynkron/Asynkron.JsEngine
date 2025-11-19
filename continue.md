# Generator IR Next Steps

## Current State
- IR now includes `JumpInstruction`, `StoreResumeValueInstruction`, and a delegated `YieldStarInstruction`, so `.next/.throw/.return` payloads are captured without replaying statements and delegated iterators remain on the IR fast path.
- The builder lowers blocks, expression statements (including `yield*`), `while`, `do/while`, classic `for` loops (with labels), `if/else`, variable declarations, plain assignments (`target = yield <expr>`), simple `if (yield <expr>)` conditions, `return yield <expr>` statements, and `try/catch/finally` statements by emitting hidden slots and explicit IR instructions.
- Loop scopes track break/continue targets and now emit dedicated `BreakInstruction`/`ContinueInstruction` nodes so loop exits unwind active `finally` blocks before resuming, including across delegated `yield*` frames.
- `StoreResumeValueInstruction` consumes pending `.next/.throw/.return` payloads; `.throw`/`.return` flow through the interpreter before short-circuiting so try/catch/finally blocks can observe them, and a try-frame stack guarantees finally blocks execute during abrupt completion (including nested finalizers and mid-final `.throw/.return` overrides).
- Delegated `yield*` now awaits promise-returning iterator completions on both paths: `DelegatedYieldState.MoveNext` feeds promise-like `next/throw/return` results through `TryAwaitPromise`, and the IR `YieldStarInstruction` path now plumbs delegated `.throw/.return` completion (including async rejections) back through `HandleAbruptCompletion` and `CompleteReturn` so generators see the final completion record rather than the inner cleanup value.
- Tests `Generator_YieldStar_DelegatesValues`, `Generator_YieldStar_ReturnValueUsedByOuterGenerator`, `Generator_YieldStarReceivesSentValuesIr`, `Generator_YieldStarThrowDeliversCleanupIr`, `Generator_YieldStarReturnDeliversCleanupIr`, `Generator_YieldStarThrowContinuesWhenIteratorResumesIr`, `Generator_YieldStarThrowRequiresIteratorResultObjectIr`, `Generator_YieldStarReturnRequiresIteratorResultObjectIr`, `Generator_YieldStarThrowRequiresIteratorResultObjectInterpreter`, `Generator_YieldStarReturnRequiresIteratorResultObjectInterpreter`, `Generator_YieldStarThrowAwaitedPromiseIr`, `Generator_YieldStarThrowAwaitedPromiseInterpreter`, `Generator_YieldStarThrowPromiseRejectsIr`, `Generator_YieldStarThrowPromiseRejectsInterpreter`, `Generator_YieldStarReturnAwaitedPromiseIr`, `Generator_YieldStarReturnAwaitedPromiseInterpreter`, and `Generator_YieldStarReturnDoneFalseContinuesIr` now lock in delegated `.next/.throw/.return` semantics for synchronous iterators, promise-returning completions, and rejection propagation (IR + interpreter).
- Tests `Generator_IfConditionYieldIr` and `Generator_ReturnYieldIr` lock in IR semantics for `if (yield <expr>)` and `return yield <expr>` so resume payloads are threaded through the IR pending-completion model rather than via replay.
- `for await...of` remains on the replay path, but tests `Generator_ForAwaitFallsBackIr`, `Generator_ForAwaitAsyncIteratorAwaitsValuesIr`, `Generator_ForAwaitPromiseValuesAreAwaitedIr`, and `Generator_ForAwaitAsyncIteratorRejectsPropagatesIr` verify that async iterators and promise-valued elements are awaited and that rejections surface as `ThrowSignal`s.
- All generator IR tests, including nested `try/finally` cases (`Generator_TryFinallyNestedThrowIr`, `Generator_TryFinallyNestedReturnIr`), are now green and exercise the IR pending-completion model.
- New tests `Generator_YieldStarNestedTryFinallyThrowMidFinalIr` and `Generator_YieldStarNestedTryFinallyReturnMidFinalIr` combine `yield*` with nested `try/finally` and mid-final `.throw/.return`, and `YieldStarInstruction` now preserves pending abrupt completions across multiple `finally` frames so later resumes override earlier ones without downgrading throws/returns.
- `Generator_ForOfLetCreatesNewBindingIr_FallsBackToReplay`, `Generator_ForOfDestructuringIr_FallsBackToReplay`, and `Generator_VariableInitializerWithMultipleYields_FallsBackToReplayIr` now lock in which generator shapes still deliberately fall back to the replay engine (`for...of` with block-scoped bindings, destructuring in `for...of`, and complex `yield` in variable initializers).
- `GeneratorIrDiagnostics` exposes lightweight counters for IR plan attempts/successes/failures, and `Generator_ForOfYieldsValuesIr_UsesIrPlan` asserts that plain `for...of` with `var` is always hosted on the IR path (no silent fallbacks).
- `docs/GENERATOR_IR_LIMITATIONS.md` captures which generator constructs lower to IR, which ones intentionally fall back, and what follow-up work is still open.

## Next Iteration Plan

1. **Eliminate Replay-Only IR Gaps**
   - Enumerate generator shapes that still fall back to the replay engine (e.g., `for...of` with `let`/`const` + closures, nested `try` inside `finally`, more complex `yield` placements flagged by `ContainsYield`).
   - For each shape, decide whether it should be:
     - fully supported on the IR path (and update `GeneratorIrBuilder` + IR interpreter accordingly), or
     - explicitly rejected at parse/analysis time with a clear error so we never silently rely on replay.
   - Add or extend `Generator_*Ir` tests (and interpreter twins where appropriate) to lock in the chosen semantics.
   - In particular, for `for...of` with `let`/`const` + closures:
     - Design per-iteration lexical environments in the IR interpreter so each iteration gets its own block environment (matching the replay engine and spec semantics for loop-scoped bindings).
     - Adjust `GeneratorIrBuilder.TryBuildStatement` and `TryBuildForOfStatement` so that:
       - `for...of` with `let`/`const` no longer falls back by default, and
       - the generated IR explicitly switches into a fresh per-iteration environment before executing the loop body.
     - Update `CreateForOfIterationBlock` so closures over the loop variable capture the per-iteration binding rather than a single shared slot.
     - Flip `Generator_ForOfLetCreatesNewBindingIr_FallsBackToReplay` / `Generator_ForOfDestructuringIr_FallsBackToReplay` into “uses IR plan” tests once the implementation is correct, and add a `Generator_ForOfLetCreatesNewBindingIr_UsesIrPlan` assertion via `GeneratorIrDiagnostics`.

2. **Enforce No-Fallback for Supported Generators**
   - Use `GeneratorIrDiagnostics` in tests to assert that all `Generator_*Ir` scenarios actually build IR plans (no failures recorded for the supported set).
   - For any newly IR-hosted shapes (from step 2), add “uses IR plan” tests similar to `Generator_ForOfYieldsValuesIr_UsesIrPlan` so future regressions are caught.

3. **Sunset the Replay Generator Path**
   - Once the supported generator surface is fully IR-hosted and covered by no-fallback tests:
     - Make generator creation treat `GeneratorIrBuilder.TryBuild` failures for supported shapes as hard errors (or parse-time rejections), rather than silently falling back to replay.
     - Remove the generator replay machinery from `TypedAstEvaluator` (`YieldTracker`, `YieldResumeContext`, and generator-specific replay branches), leaving at most a thin compatibility shim for explicitly unsupported constructs.

4. **Async Await Scheduling (Post‑Sunset)**
   - After the replay generator is no longer in the hot path, revisit `TryAwaitPromise` and generator scheduling:
     - Integrate the event queue (e.g., resume generators via `ScheduleTask`) so long-running promises in generators don’t block the managed thread.
     - Optionally expose instrumentation hooks (trace or debug) so we can observe nested awaits inside generators and detect potential starvation.
