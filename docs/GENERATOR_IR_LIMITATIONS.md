# Generator IR: Supported Surface & Rejections

The typed generator interpreter now runs **entirely on an IR-backed state machine** for the supported subset of JavaScript constructs. Generator functions must successfully lower to IR at creation time; unsupported shapes are rejected with a `NotSupportedException` and no legacy replay runner is used. This document captures the current boundaries so contributors know which generators *do* get IR plans and which ones are currently rejected.

## What IR currently covers

IR lowering succeeds when a generator body only contains:

- Block/empty statements, including standalone `yield` and `yield*` expressions.
- `switch` statements whose discriminant and case test expressions are yield-free, with at most a single
  `default` clause in the final position and case bodies that contain no `break` except for an optional
  single trailing unlabeled `break;` per case (fallthrough between cases is preserved).
- Variable declarations with simple bindings (identifier or destructuring) where initializer expressions are free of `yield`, or the initializer is a single `yield` / `yield*` whose result is assigned via a hidden resume slot.
- Classic `while`, `do/while`, and `for` loops (with labels) whose header expressions are `yield`-free, or whose conditions contain a single non-delegated `yield` that can be factored out into a per-iteration `yield` + resume slot pattern (e.g. `while (yield "probe")` or `while (1 + (yield "probe"))`).
- `try/catch/finally` statements, including nested loops and labeled `break/continue`.
- Plain assignment statements of the form `target = yield <expr>`.
- Basic `for...of` loops that iterate over synchronous iterables, including loops with `var` / `let` / `const`
  bindings and destructuring in the loop head, where closures capture the per-iteration binding.

See `GeneratorIrBuilder` for the full matching logic.

## Currently rejected shapes

The builder deliberately rejects the following constructs, causing generator creation to fail with a clear `Generator IR not implemented for this function: ...` message:

| Construct | Why it is rejected | Code reference |
|-----------|--------------------|----------------|
| `for await (... of ...)` loops | Generator IR is currently scoped to synchronous generators; async iteration is handled by the async/CPS pipeline instead. | `GeneratorIrBuilder.TryBuildStatement` |
| Async generator functions (`async function*`) | Async generators are not yet implemented in the typed evaluator, so IR never engages and the shape is rejected up front. | `TypedGeneratorFactory` + parser |
| Any `yield` that appears inside unsupported expression shapes (e.g. multiple `yield`s in the same expression, nested `yield` inside the operand of another `yield`) | `ContainsYield` checks bubble up and abort lowering to preserve correctness until those shapes are modeled. Single non-delegated `yield` occurrences in conditions and simple assignments are now rewritten into IR-friendly patterns. | `GeneratorIrBuilder.ContainsYield*` helpers + `TryRewriteConditionWithSingleYield` |

## Documented behavior

The following tests lock our current expectations for the IR and fallback paths:

- `Generator_YieldStarDelegatesValues`, `Generator_YieldStarReceivesSentValuesIr`, `Generator_YieldStarThrowDeliversCleanupIr`, `Generator_YieldStarReturnDeliversCleanupIr`, `Generator_YieldStarThrowContinuesWhenIteratorResumesIr`, and `Generator_YieldStarReturnDoneFalseContinuesIr` ensure delegated `yield*` expressions stay on the IR path, forward `.next/.throw/.return` payloads to the underlying iterator (even when the delegate reports `done: false`), and still unwind nested `try/finally` stacks.
- `Generator_YieldStarThrowRequiresIteratorResultObjectIr`, `Generator_YieldStarReturnRequiresIteratorResultObjectIr`, and their interpreter twins ensure both execution paths reject delegates whose `.throw/.return` helpers return non-object completion records (matching ECMAScript’s TypeError guardrail).
- All remaining `Generator_*Ir` tests listed in `continue.md` execute on the IR path and assert catch/finally semantics, loop unwinding, and resume behavior. `for await...of` is treated as a pure async-iteration construct and is tested separately under `AsyncIterationTests` / `AsyncIterableDebugTests` using spec-compliant async functions.

## Known gaps & future work

- While the IR path now hosts `yield*`, delegated `.throw` / `.return` handling still reuses the simplified `DelegatedYieldState`, so we do not yet model the full ECMAScript algorithm (for example, if `iterator.throw` returns a non-completion object). More spec-focused tests are required.
- Awaiting promise-returning iterators currently blocks the managed thread until the promise settles (we synchronously wait on a `TaskCompletionSource`). Integrating the event queue so long-running promises yield back to the host remains future work.
- There is no IR support for async generators (`async function*`), so adding those will require both parser work and a coroutine-aware interpreter.

Use this file when triaging future bugs or planning work: if a generator construct isn’t in the supported list above, it is expected to be rejected at generator creation time until we extend the IR.
