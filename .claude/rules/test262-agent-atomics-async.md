# Test262 Agent and Atomics Async Lifecycle

When fixing Test262 agent crashes involving `Atomics.waitAsync`, `$262.agent`,
or event-loop scheduled work, treat the failure as an async lifecycle ownership
problem until proven otherwise.

## Rules

1. Inspect both sides of the boundary before patching:
   `src/Asynkron.JsEngine/StdLib/Atomics/` and
   `tests/Asynkron.JsEngine.Tests.Test262/Test262AgentRuntime.cs`.
2. Keep waiter cleanup idempotent. Do not dispose waiters, cancellation sources,
   semaphores, shared buffers, or report queues while timeout, notify,
   promise-resolution, or worker-shutdown paths may still observe them.
3. When draining a `$262.agent` broadcast callback, wait for settlement only if
   the callback returned an internal `JsPromise` whose state is observable from
   the harness. For non-internal thenables, drain microtasks once and let the
   script report/teardown path complete the case.
4. Do not leave agent workers blocked indefinitely on broadcast queues after
   the useful broadcast window has passed. Use bounded idle exits or explicit
   completion signals so a passing method group can release the testhost.
5. Do not introduce blocking waits such as `Task.Wait()`, `.Result`, or
   `Thread.Sleep()` to make agent tests pass. Use the engine event queue and
   async promise path.
6. Synchronize host-side test captures when a host callback and a scheduled
   event-loop task can write to the same collection.
7. Prove the exact crashing Test262 file or method group first, then widen only
   as needed.

## Why

Issue #754 / PR #887 fixed 18 crashing `Atomics_waitAsync` agent cases. The
root lesson was that waitAsync waiter lifetime, Test262 agent broadcast/report
plumbing, and event-loop scheduled work interact through shared async state.
Treating the change as a local value-shape fix risks preserving the race or
adding a new one.

Issue #755 / PR #905 fixed the BigInt `Atomics_waitAsync` agent group where the
individual cases passed but the testhost could still hang. That incident showed
that the harness must not wait on arbitrary thenables it cannot observe, and
agent worker queues need bounded teardown behavior.
