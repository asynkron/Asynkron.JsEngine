# ADR 0235: Keep await scheduler state explicit and thread-local-free

## Status

Accepted

## Context

Issue #2409 / PR #2411 followed up on the async-generator IR roadmap wording
that still described a sync-generator wrapper seam with thread-static callback
caches. Investigation found that the async-generator resume callbacks had
already moved to the explicit pool-owned model captured in ADR 0179, and the
remaining real thread-local state in the owner surface was
`AwaitScheduler.CachedState`.

`AwaitScheduler` is shared by async functions, async generators, for-await, and
sync helper paths that need await-like promise handling. Its
`PromiseAwaitState` captures the currently awaited candidate, resolve/reject
continuations, completion status, and resolved value while a promise is being
driven. Reusing that state through `[ThreadStatic]` made an async runtime
lifetime look like disposable scratch storage and conflicted with the repo
policy against `ThreadStatic`, `AsyncLocal<T>`, and shared state between async
calls.

The delivery removed the `[ThreadStatic]` `CachedState` field and made
`RentState()` allocate a fresh `PromiseAwaitState`. `ReturnState()` still resets
the state, but it no longer publishes it back to any thread-local cache. The
roadmap was also corrected to describe the current shared
`ExecutionPlanRunner.ExecuteAsyncStep` bridge instead of stale sync-generator
wrapper and ThreadStatic callback wording.

Review proof focused on the affected runtime surface: the reviewer confirmed
that `AsyncGeneratorInvoker` and `AwaitScheduler` do not reintroduce
thread-local caches and ran the async-generator proof slice:

```text
rtk dotnet test tests/Asynkron.JsEngine.Tests \
  --filter "FullyQualifiedName~AsyncGeneratorTests|FullyQualifiedName~AsyncGeneratorActivation_PreservesCapturedParameterAcrossAwaitAndYield" \
  -- xUnit.MaxParallelThreads=1 -timeout 20000

ok dotnet test: 21 tests passed, 0 warnings in 1 projects (6.4 s)
```

## Decision

Keep await scheduler state explicit and thread-local-free.

Await/resume state such as `PromiseAwaitState` must be owned by the current
await operation, runner, or explicit pool lease. Do not use `[ThreadStatic]`,
`AsyncLocal<T>`, or shared static state as an allocation shortcut for
`AwaitScheduler`, async-function resume, async-generator resume, for-await, or
nearby promise continuation paths.

If a future optimization needs to reduce `PromiseAwaitState` allocation, it
must make ownership visible. Acceptable future owners include a bounded object
pool with clear rent/reset/return semantics, a per-runner/per-await-site state
object that cannot cross async calls, or a proven lowerer/runner model that no
longer needs the state object. The optimization must not rely on managed-thread
affinity, and it must prove observable async ordering across pending awaits.

The current async-generator bridge remains
`ExecutionPlanRunner.ExecuteAsyncStep`. Roadmap and ADR text should describe
that bridge directly until a dedicated async-generator IR executor exists and
has its own ownership proof.

## Consequences

- Await handling no longer depends on thread-local `PromiseAwaitState` reuse.
- Correctness ownership is prioritized over this small allocation cache. Any
  future allocation reduction must carry explicit lifecycle proof instead of
  hiding state in a managed-thread slot.
- Async-generator follow-up work should keep separating two concerns:
  callback ownership remains pool-owned under ADR 0179, while await scheduler
  state remains operation-owned under this ADR.
- Documentation should not repeat stale "sync-generator wrapper" or
  "ThreadStatic callback cache" claims unless the implementation actually
  reintroduces those seams.
- Focused proof for future changes should include async-generator pending-await
  ordering and captured activation preservation, plus a thread-local state scan
  over the touched async runtime owner surfaces.

## Related

- `docs/adrs/0179-keep-async-generator-resume-callbacks-pool-owned.md`
- `.claude/rules/async-resume-callback-ownership.md`
- `.claude/rules/async-runtime-tests.md`
- `docs/roadmap.md`
- `src/Asynkron.JsEngine/Execution/AwaitScheduler.cs`
- `src/Asynkron.JsEngine/Ast/TypedAstEvaluator.AsyncGeneratorInvoker.cs`
