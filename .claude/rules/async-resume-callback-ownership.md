# Async Resume Callback Ownership

When changing async function or async generator pending-await resume callbacks,
keep callback lifetime explicit and pool-owned. When changing nearby
await/resume scheduler state, keep state lifetime explicit and thread-local-free.

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
9. For code-reduction work in this callback path, keep lifecycle cleanup
   helpers local to the callback type unless a broader ownership change is
   proven. A small `ClearState()` helper may centralize captured-reference
   cleanup, but do not merge the async-function and async-generator callback
   types or reshape `_isRejection` / pool-return mapping just to remove a few
   repeated assignments.
10. Do not use `[ThreadStatic]`, `AsyncLocal<T>`, or shared static state for
    `AwaitScheduler` await-state helpers such as `PromiseAwaitState`. If a
    future optimization reduces await-state allocation, it must use an explicit
    owner such as a bounded pool, per-runner state, or per-await-site state with
    visible rent/reset/return semantics.
11. When updating roadmap or architecture wording for async generators, keep
    callback ownership and await scheduler state ownership distinct. The current
    bridge is `ExecutionPlanRunner.ExecuteAsyncStep`; do not describe it as a
    sync-generator wrapper or ThreadStatic callback cache unless that coupling
    is actually present in code.

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

Issue `autrun-dit5s0fwvhpc-3e5ac4503a` / PR #2272 reduced duplicated
captured-reference reset code by adding local `ClearState()` helpers in the
async-function and async-generator resume callbacks. The useful lesson was the
boundary: local cleanup dedupe is acceptable, but cross-type merging would have
blurred two callback lifecycles for a tiny code-size win.

Related ADR:
- `docs/adrs/0179-keep-async-generator-resume-callbacks-pool-owned.md`
- `docs/adrs/0235-keep-await-scheduler-state-explicit-and-thread-local-free.md`

Issue #2409 / PR #2411 found that the async-generator callback cache lesson had
already landed, but `AwaitScheduler.CachedState` still used `[ThreadStatic]` for
`PromiseAwaitState` reuse and `docs/roadmap.md` still repeated stale
sync-generator-wrapper plus ThreadStatic wording. `AwaitScheduler` is shared by
async functions, async generators, for-await, and sync helper paths, so its
await state is runtime lifecycle state, not thread-local scratch storage. The
fix removed the cache, kept reset ownership explicit, and updated the roadmap to
name `ExecutionPlanRunner.ExecuteAsyncStep` as the current bridge.
