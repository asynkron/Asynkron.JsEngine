# Async Runtime Tests

When an async test is not specifically testing timer scheduling, use the
repository's tracked async helpers instead of raw `setTimeout` timers.

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

## Why

Issue #1025 / PR #1097 fixed an unrelated `Intl.DurationFormat` bug, but the
canonical quality gate timed out twice on
`AsyncAwaitTests.AsyncFunction_WithParallelDelays`. That test was proving
parallel promise resolution order through `Promise.all`, not timer behavior, so
using three raw `setTimeout` timers made the delivery fragile for the wrong
reason. The follow-up repair switched the test to
`AsyncTestHelpers.RegisterDelayHelper` while preserving the Promise.all
ordering assertion.
