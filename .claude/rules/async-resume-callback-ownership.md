# Async Resume Callback Ownership

When changing async function or async generator pending-await resume callbacks,
keep callback lifetime explicit and pool-owned.

## Rules

1. Do not use `[ThreadStatic]`, `AsyncLocal<T>`, or shared static callback
   caches to avoid pending-await callback allocation.
2. If callbacks are pooled, rent fulfilled and rejected callbacks as a pair and
   link them as siblings so the callback that fires can clear and return both
   callbacks.
3. Assert pooled callback ownership in DEBUG before invoking the callback body.
   Use the existing `PoolDebug` lease checks or an equivalent owner-visible
   invariant.
4. Clear captured executor, resolve, reject, and sibling references before pool
   return. Keep the invoked callback's resume in a `try` block and return both
   callbacks from a `finally` path.
5. Preserve resume-mode mapping: fulfillment resumes with `ResumeMode.Next`;
   rejection resumes with `ResumeMode.Throw`.
6. Do not duplicate async-generator step settlement switches when an existing
   owner such as `ResolveFromStep` can settle `Yield`, `Completed`, `Throw`,
   and `Pending` consistently.
7. Prove the pending-await callback path with focused ordering coverage. For
   async generators, include direct iterator `.next()` sequencing across one or
   more delayed awaits and assert the yielded values plus final `done: true`
   result.
8. When documenting this runtime path, describe the current shared
   `ExecutionPlanRunner.ExecuteAsyncStep` bridge. Do not call it a sync-generator
   shim unless the implementation actually reintroduces that coupling.

## Why

Issue #2124 / PR #2131 removed the async-generator `[ThreadStatic]`
fulfilled/rejected resume callback cache. Thread-local callback reuse hides the
lease boundary in an async runtime path and conflicts with the repository rule
against shared state between async calls. The accepted fix matched the
async-function pattern: bounded `ObjectPool<AsyncResumeCallback>` instances,
sibling callback linkage, DEBUG `PoolDebug.AssertOwned`, and a focused
`AsyncGenerator_DirectNextAcrossPendingAwaits_PreserveOrdering` regression that
exercises direct `.next()` calls across pending awaits.

The durable lesson is that pending-await resume callbacks are lifecycle state,
not disposable scratch storage. Future allocation reductions in this area must
make callback ownership and cleanup visible, then prove observable async order.

Related ADR:
`docs/adrs/0179-keep-async-generator-resume-callbacks-pool-owned.md`.
