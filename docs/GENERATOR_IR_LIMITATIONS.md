# Generator IR: Supported Surface & Fallbacks

The typed generator interpreter exposes an IR-backed fast path for a well-defined subset of JavaScript constructs. Anything outside that envelope must fall back to the legacy replay runner in `TypedAstEvaluator`, which replays the generator body and uses a `YieldTracker` to re-deliver resume payloads. This document captures the current boundaries so contributors know when a generator will *not* be lowered to IR and what behavior is guaranteed today.

## What IR currently covers

IR lowering succeeds when a generator body only contains:

- Block/empty statements, including standalone `yield` and `yield*` expressions.
- Variable declarations with simple bindings (identifier or destructuring) where initializer expressions are free of `yield`.
- Classic `while`, `do/while`, and `for` loops (with labels) whose header expressions are `yield`-free.
- `try/catch/finally` statements, including nested loops and labeled `break/continue`.
- Plain assignment statements of the form `target = yield <expr>`.
- Basic `for...of` loops that iterate over synchronous iterables.

See `GeneratorIrBuilder` for the full matching logic.

## Guaranteed fallbacks

The builder deliberately rejects the following constructs, forcing execution to stay on the replay engine:

| Construct | Why it falls back | Code reference |
|-----------|-------------------|----------------|
| `for await (... of ...)` loops | Builder guards out `ForEachKind.AwaitOf` so async iterables remain on the legacy path. | `GeneratorIrBuilder.TryBuildStatement` |
| Async generator functions (`async function*`) | The typed evaluator doesn’t implement async generators at all, so IR never engages. | `TypedGeneratorFactory` + parser |
| Any `yield` that appears inside unsupported expression shapes (e.g. `yield` buried in arithmetic, ternaries, etc.) | `ContainsYield` checks bubble up and abort lowering to preserve correctness. | `GeneratorIrBuilder.ContainsYield*` helpers |

When a construct falls back, execution uses `EvaluateYield` / `EvaluateDelegatedYield` inside `TypedAstEvaluator`. That path replays the function body but still honors `.next(value)`, `.throw(value)`, and `.return(value)` resume payloads via `YieldTracker` + `YieldResumeContext`.

## Documented behavior

The following tests lock our current expectations for the IR and fallback paths:

- `Generator_YieldStarDelegatesValues`, `Generator_YieldStarReceivesSentValuesIr`, `Generator_YieldStarThrowDeliversCleanupIr`, `Generator_YieldStarReturnDeliversCleanupIr`, `Generator_YieldStarThrowContinuesWhenIteratorResumesIr`, and `Generator_YieldStarReturnDoneFalseContinuesIr` ensure delegated `yield*` expressions stay on the IR path, forward `.next/.throw/.return` payloads to the underlying iterator (even when the delegate reports `done: false`), and still unwind nested `try/finally` stacks.
- `Generator_YieldStarThrowRequiresIteratorResultObjectIr`, `Generator_YieldStarReturnRequiresIteratorResultObjectIr`, and their interpreter twins ensure both execution paths reject delegates whose `.throw/.return` helpers return non-object completion records (matching ECMAScript’s TypeError guardrail).
- `Generator_ForAwaitFallsBackIr`, `Generator_ForAwaitAsyncIteratorAwaitsValuesIr`, `Generator_ForAwaitPromiseValuesAreAwaitedIr`, and `Generator_ForAwaitAsyncIteratorRejectsPropagatesIr` prove `for await...of` loops now await promise-returning iterators even outside async functions and surface rejections as `ThrowSignal`s.
- All other `Generator_*Ir` tests listed in `continue.md` execute on the IR path and assert catch/finally semantics, loop unwinding, and resume behavior.

## Known gaps & future work

- While the IR path now hosts `yield*`, delegated `.throw` / `.return` handling still reuses the simplified `DelegatedYieldState`, so we do not yet model the full ECMAScript algorithm (for example, if `iterator.throw` returns a non-completion object). More spec-focused tests are required.
- Awaiting promise-returning iterators currently blocks the managed thread until the promise settles (we synchronously wait on a `TaskCompletionSource`). Integrating the event queue so long-running promises yield back to the host remains future work.
- There is no IR support for async generators (`async function*`), so adding those will require both parser work and a coroutine-aware interpreter.

Use this file when triaging future bugs or planning work: if a generator construct isn’t in the supported list above, it is expected to fall back to the replay engine until we extend the IR.
