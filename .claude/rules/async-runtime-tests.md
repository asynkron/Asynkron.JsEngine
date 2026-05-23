# Async Runtime Tests

When an async test or runnable demo is not specifically testing timer
scheduling, make async completion explicit so the proof is about the runtime
behavior under test instead of incidental host scheduling.

## Rules

1. Use `AsyncTestHelpers.RegisterDelayHelper(engine)` for ordinary promise delay
   fixtures that need deterministic async ordering.
2. Keep raw `setTimeout` calls for tests that intentionally validate timer,
   event-loop, or host callback scheduling semantics.
3. If a test proves `Promise.all`, async/await ordering, or promise resolution
   shape, keep the delay mechanism deterministic so the assertion is about the
   JavaScript behavior under test.
4. Run the narrow async test or async test class with the repository timeout
   arguments before handing the branch to the quality gate:
   `xUnit.MaxParallelThreads=1 -timeout 20000`.
5. Do not pass `async` lambdas to host APIs that accept `Action`, such as
   `JsEngine.ScheduleTask(Action)`. Start background work explicitly, capture
   completion with `TaskCompletionSource` or another tracked task, and await
   that completion before printing or asserting final state.

## Why

Issue #1025 / PR #1097 fixed an unrelated `Intl.DurationFormat` bug, but the
canonical quality gate timed out twice on
`AsyncAwaitTests.AsyncFunction_WithParallelDelays`. That test was proving
parallel promise resolution order through `Promise.all`, not timer behavior, so
using three raw `setTimeout` timers made the delivery fragile for the wrong
reason. The follow-up repair switched the test to
`AsyncTestHelpers.RegisterDelayHelper` while preserving the Promise.all
ordering assertion.

Issue #1627 / PR #1631 fixed `EventQueueDemo` after scheduled async work was
started through callbacks accepted as `Action`. The demo could print its final
completion line before the delayed background work finished, which made the
example claim success early. Future async demos and tests should track the host
work they start and wait for it before reporting final state.
