# ADR 0041: Keep microtask queue mutations serialized

## Status

Accepted

## Context

Issue #1032 / PR #1106 fixed an async module continuation race in
`JsEngine`. The runtime already had careful top-level-await scheduling rules
from ADR 0027: import-time waits may detach and restore unrelated pending
microtasks, and already-settled awaits still resume through the engine
microtask queue.

The missing piece was ownership of the queue data structure itself. The engine
is single-threaded at the JavaScript semantic level, but async module
continuations can resume on different managed threads. That makes operations
such as queueing, detaching, prepending, and draining microtasks observable as
a shared runtime mutation surface even when JavaScript execution remains
logically ordered.

The delivery kept the existing scheduling semantics and added serialization
around `_microtaskQueue` and `_isDrainingMicrotasks` in `JsEngine`. It did not
move async module continuations to synchronous callbacks or force broader
microtask drains to hide the race.

## Decision

Microtask queue mutation in `JsEngine` is a serialized runtime boundary.

When changing microtask scheduling, top-level-await imports, or async module
continuations:

1. protect enqueue, detach, prepend, dequeue, deferred requeue, and drain-state
   mutation with the shared queue lock;
2. do not hold the queue lock while executing the microtask callback itself;
3. keep `_isDrainingMicrotasks` protected by the same queue lock as the queue
   whose drain it describes;
4. preserve the module-body drain gate from ADR 0027 instead of using a lock as
   permission to force-drain pending work; and
5. keep detached microtasks restored in their original relative order when an
   import wait temporarily removes unrelated pending work.

The lock exists to protect host-side queue structure from managed-thread
interleaving. It is not a license to make JavaScript execution multi-threaded,
to run callbacks under the lock, or to collapse ECMAScript tick boundaries.

## Consequences

- Future async module fixes should inspect queue mutation ownership alongside
  module dependency waits and continuation scheduling.
- Reentrant drains should return when another drain is active, but the active
  flag and queue mutations must be read and written under the same protection.
- Regression proof for this boundary should include a local import-resolution
  tick-ordering test that preserves unrelated pending microtasks around an
  async module wait.
- This ADR is caused by issue #1032 / PR #1106 and complements ADR 0027's
  top-level-await scheduling decision.
