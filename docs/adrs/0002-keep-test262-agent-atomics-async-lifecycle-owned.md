# ADR 0002: Keep Test262 agent Atomics async lifecycle owned

## Status

Accepted

## Context

Issue #754 fixed the first `Atomics_waitAsync` Test262 agent crash cluster.
Issue #755 / PR #905 then fixed the BigInt method group after all 18 listed
agent cases were still crashing or leaving the testhost alive after the case
results had passed. The deliveries touched two coupled areas:

- `Atomics.waitAsync` waiter lifetime in
  `src/Asynkron.JsEngine/StdLib/Atomics/`
- `$262.agent` worker runtime broadcast/report/shutdown behavior in
  `tests/Asynkron.JsEngine.Tests.Test262/Test262AgentRuntime.cs`

The original risk was not a plain assertion mismatch. It was an async lifecycle
problem: waiters, timeout cancellation, promise settlement, cross-agent shared
buffers, reports, and worker teardown can race each other. A later quality-gate
failure also exposed a load-sensitive test synchronization issue in
`EventQueueTests`, where a host callback and an event-loop scheduled task wrote
to the same capture list without synchronization.

The #755 re-entry showed a second harness-specific trap: `$262.agent` can call a
broadcast callback that returns an arbitrary thenable. Only internal
`JsPromise` instances expose settlement to the host harness. Waiting as though
every thenable is observable can turn a passing method group into a hung
testhost, especially when worker threads remain blocked on the broadcast queue.

## Decision

Treat Test262 agent Atomics fixes as lifecycle ownership work, not as isolated
value-shape repairs.

When touching `Atomics.waitAsync`, `$262.agent`, or event-queue proofs for this
class of failures:

1. Keep waiter cleanup idempotent and owned by the path that knows whether the
   waiter was completed, timed out, notified, or cancelled.
2. Do not dispose or remove resources early while a promise-resolution or
   worker-report path may still observe them.
3. In the Test262 agent runtime, wait for broadcast callbacks only when the
   returned promise is an internal `JsPromise` whose settlement can be observed.
   For non-internal thenables, drain microtasks once and let the script-level
   report/teardown path drive completion.
4. Do not leave agent worker threads indefinitely blocked on broadcast queues
   after the relevant broadcast window has completed. Use bounded idle exits or
   explicit completion signals so successful cases can release the testhost.
5. Synchronize shared test captures when both host callbacks and scheduled
   event-loop work can write to them.
6. Prove with the narrow crashing Test262 method group or exact listed files
   first, then run the internal quality gate needed for the delivery branch.

## Consequences

- Future `Atomics.waitAsync` repairs should inspect both the engine waiter
  manager and the Test262 agent runtime before deciding ownership.
- Test code that models event-loop concurrency should use explicit
  synchronization for shared host-side collections instead of depending on
  single-test timing.
- Reviewers should treat unsynchronized capture lists, early waiter disposal,
  unobservable thenable waits, and background worker teardown races as
  first-class risks in this area.
- This ADR is caused by issue #754 / PR #887 and issue #755 / PR #905, and
  complements ADR 0001's separate quality-gate build/test contract.
