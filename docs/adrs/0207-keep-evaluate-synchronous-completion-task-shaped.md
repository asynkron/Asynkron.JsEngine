# ADR 0207: Keep Evaluate Synchronous Completion Task-Shaped

## Status

Accepted

## Context

Issue `autrun-dit1y6ykons0-facccf278d` / PR #2232 selected the
`simplearithmetic` benchmark from the required optimizer baseline. The initial
row showed Asynkron at 2302 ms versus Jint at 138 ms, while the focused CPU
profile showed the tiny synchronous script spending substantial filtered time in
the async `JsEngine.Evaluate(ProgramNode)` state machine instead of the IR loop.

The accepted delivery changed the private `Evaluate(ProgramNode, ...)` helper
from an unconditional `async Task<object?>` state machine to a task-returning
method. Synchronous programs now execute first, drain microtasks, and when no
event-loop work remains they complete with `Task.FromResult(...)` without
starting the event loop. The final selected-profile evidence moved the
conservative comparison from 2302 ms to 402 ms, and repeated final runs stayed
around 390-402 ms.

Review and build-back also exposed the semantic risks around this optimization:
task-shaping code can silently change observable task state, and synchronous
fault cleanup can erase pending timer work that the next drain should process.
The repair kept ordinary exceptions faulted, returned
`OperationCanceledException` as a canceled `Task<object?>`, and preserved timer
work scheduled before a synchronous JavaScript throw.

## Decision

Keep `JsEngine.Evaluate(ProgramNode, ...)` task-shaped rather than
unconditionally async for the synchronous no-pending-work path.

The durable boundary is:

1. execute the program or synchronous module body before starting the event
   loop;
2. drain microtasks and flush deferred event tasks before deciding whether
   event-loop work is pending;
3. when no work is pending and no event queue exists, return
   `Task.FromResult(unwrappedResult)`;
4. when timers, promises, async modules, or other pending work exist, continue
   through the async drain path and keep the existing cleanup behavior;
5. shape ordinary synchronous exceptions with `Task.FromException<object?>`;
6. shape `OperationCanceledException` with `Task.FromCanceled<object?>`, using
   the exception token when it is canceled and a canceled token otherwise; and
7. if the synchronous prefix schedules timer/deferred work and then throws
   before the drain phase, leave that pending work for a later evaluation/drain
   instead of clearing counters or deferred queues as fault cleanup.

Do not reintroduce an unconditional async state machine around
`Evaluate(ProgramNode, ...)` just to share completion code. Completion helpers
own the task state and cleanup boundary, so future changes must preserve
successful, faulted, canceled, pending-work, and async-module cases explicitly.

## Consequences

- Pure synchronous repeated `Evaluate(ProgramNode)` workloads avoid the async
  state-machine overhead that dominated the `simplearithmetic` profile.
- Public callers still observe an awaitable `Task<object?>`, with canceled
  evaluations producing canceled tasks rather than faulted tasks.
- Event-loop work scheduled during the synchronous prefix remains observable to
  subsequent drains when the prefix throws before post-execution cleanup.
- Future optimizer slices touching this boundary need regression coverage for
  immediate successful completion without event-loop startup, synchronous throw
  with pending timer work, and pre-canceled tokens.
- Performance claims for this boundary still need selected-profile baseline and
  final rows plus focused runtime tests; a cleaner CPU profile alone is not
  enough.

## Related

- `docs/performance/simplearithmetic-synchronous-evaluate-completed-task.md`
- `.claude/rules/performance-profiling-guardrails.md`
- `tests/Asynkron.JsEngine.Tests/PendingAsyncWorkTrackingTests.cs`
