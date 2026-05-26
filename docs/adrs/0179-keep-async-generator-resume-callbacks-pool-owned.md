# ADR 0179: Keep async generator resume callbacks pool-owned

## Status

Accepted

## Context

Issue #2124 / PR #2131 cleaned up async-generator pending-await resume flow.
`TypedAstEvaluator.AsyncGeneratorInvoker.AsyncResumeCallback` still used
`[ThreadStatic]` fulfilled/rejected callback caches, while async functions had
already moved to bounded `ObjectPool<AsyncResumeCallback>` instances with paired
fulfilled/rejected callbacks. The same file also carried stale wording that made
the async-generator bridge sound like a sync-generator IR shim instead of the
current shared `ExecutionPlanRunner.ExecuteAsyncStep` contract.

The accepted delivery replaced the thread-static callback cache with explicit
fulfilled and rejected pools, linked each rented callback pair as siblings, and
made whichever callback fires clear and return both callbacks. It also added a
DEBUG `PoolDebug.AssertOwned` check on callback invocation and pinned the path
with `AsyncGenerator_DirectNextAcrossPendingAwaits_PreserveOrdering`.

The risky boundary is callback ownership across pending promise settlement:
the callback pair captures the async generator executor plus the current step
promise's resolve/reject callbacks, but only one of the fulfilled/rejected pair
should be invoked for a settled promise.

## Decision

Keep async-generator resume callbacks pool-owned and sibling-returned.

Async-generator pending-await resume callbacks must use explicit bounded pools
or a future owner with equivalent lease semantics. Do not reintroduce
`[ThreadStatic]`, `AsyncLocal<T>`, or other shared callback caches for this
runtime path.

Fulfilled and rejected callbacks are rented as a pair. The pair owns the
executor, resolve, and reject references until one callback is invoked. The
invoked callback must:

1. assert ownership in DEBUG builds before reading the captured state;
2. copy the captured state to locals, then clear its own references before
   resuming execution;
3. resume with `ResumeMode.Next` for fulfillment and `ResumeMode.Throw` for
   rejection;
4. settle the resumed step through the existing async-generator step resolver;
   and
5. in a `finally` path, clear the sibling's captured references and return both
   callbacks to their corresponding pools.

The async-generator invoker continues to execute through
`ExecutionPlanRunner.ExecuteAsyncStep`: each `.next`, `.return`, or `.throw`
call drives one async step and wraps the result in the returned promise. Future
dedicated async-generator IR work may replace that bridge only if it preserves
the external async-step contract and carries equivalent pending-await ownership
proof.

## Consequences

- Async generators no longer depend on thread-local callback cache state for
  pending-await continuation.
- Pool ownership is visible to the existing DEBUG `PoolDebug` invariant system,
  matching the async-function resume callback pattern.
- Callback pool return is an ownership decision, not just an allocation cleanup:
  the sibling callback must be cleared and returned even though it is not the
  callback invoked by the promise settlement.
- Future async-generator resume changes should prove at least one pending-await
  path with direct iterator `.next()` sequencing so observable yield ordering
  and final `done: true` settlement stay pinned.
- The current bridge is intentionally documented as shared async-step execution,
  not as a sync-generator shim. A later dedicated async-generator executor
  should update this ADR if it changes the ownership model.

## Related

- `.claude/rules/async-resume-callback-ownership.md`
- `.claude/rules/async-runtime-tests.md`
- `docs/adrs/0176-keep-sync-ir-activation-environment-pooling-ownership-guarded.md`
- `src/Asynkron.JsEngine/Ast/TypedAstEvaluator.AsyncGeneratorInvoker.cs`
- `src/Asynkron.JsEngine/Ast/TypedAstEvaluator.AsyncFunctionInvoker.cs`
