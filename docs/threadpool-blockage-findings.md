# Threadpool blockage & blocking waits (engine runtime)

This note captures the remaining *engine-level* sources of threadpool blockage discovered while profiling Test262 runs after the big per-test setup wins (base realm snapshot + suite-on-disk cache + AST caching).

## Status (what changed since the first write-up)

### Fixe

- Engine sync-over-async wait sites (`.GetAwaiter().GetResult()`) are removed:
  - Sync entry points fail fast on async modules instead of blocking.
  - Promise await logic no longer blocks on a TCS task; it pumps microtasks/event loop without `.GetResult()`.
- Timers no longer use `Task.Run`:
  - `setTimeout`/`setInterval` are driven by `Task.Delay` loops and schedule callbacks via the engine event queue.
  - Timer bookkeeping is thread-safe and drain completion is signaled when timers are removed.

### Still likely contributors (engine)

- The event loop is still started via `Task.Run`, so continuations execute on the shared threadpool.
- Top-level await / non-generator `await` still relies on a synchronous “pump” (`Thread.Yield` + microtask drain) rather than a continuation-based suspension model.

## What the profiler was showing

- A large share of wall time attributed to “threadpool blockage/starvation”.
- The remaining slow time was not dominated by parsing or per-test suite IO anymore; it clustered around:
  - synchronous waits on async work (promises / module evaluation)
  - threadpool scheduling churn (event loop + timers)

## Root causes found in the current engine

### 1) Threadpool scheduling churn (event loop)

`src/Asynkron.JsEngine/JsEngine.cs` starts the event loop with:
- `Task.Run(() => ProcessEventQueue(...))`

This means every engine instance that needs an event loop schedules work onto the shared threadpool. Under a large test suite this can show up as scheduler contention (especially when the host/test runner is also threadpool-heavy).

### 2) `await` expressions outside async-generator stepping still “pump” synchronously

In normal evaluation (non-generator), `await` ultimately routes into a synchronous “pump” loop.

**Where this happens**
- `src/Asynkron.JsEngine/Ast/AwaitExpressionExtensions.cs` calls `AwaitScheduler.TryAwaitPromiseSync(...)` when not running under a generator instance.
- Additional blocking uses of `AwaitScheduler.TryAwaitPromiseSync(...)` exist in:
  - `src/Asynkron.JsEngine/Ast/JsObjectExtensions.cs` (`IteratorClose` awaiting promise-like return)
  - `src/Asynkron.JsEngine/Ast/TypedAstEvaluator.DelegatedYieldState.cs` (delegated yield awaiting async-iterator results)
  - `src/Asynkron.JsEngine/Ast/TypedAstEvaluator.cs` (helper wrapper)

**Important detail**
`AwaitScheduler.TryAwaitPromiseSync` currently implements awaiting by:
- attaching `.then(onFulfilled, onRejected)`
- draining microtasks and (optionally) the event loop while yielding (`Thread.Yield`)

This avoids thread-blocking waits but can still occupy a caller thread for a long time. When the caller is a threadpool worker, it can still contribute to perceived “threadpool starvation”.

## What we already have that can be reused for a non-blocking design

### A) Async-generator stepping is already non-blocking

`src/Asynkron.JsEngine/Ast/TypedAstEvaluator.TypedGeneratorInstance.cs`
- Has an `_asyncStepMode` that changes await semantics:
  - in async-step mode, promise-like values are surfaced as **Pending** instead of blocking
  - resumption is driven by attaching `then` and continuing execution later

`src/Asynkron.JsEngine/Ast/TypedAstEvaluator.AsyncGeneratorInstance.cs`
- Demonstrates the “attach then + resume” pattern:
  - step → PendingPromise → `then(onFulfilled/onRejected)` → resume step

This is the closest existing implementation of “don’t block threads; suspend and resume”.

### B) Typed CPS transformer avoids blocking by rewriting some `await` into Promise chains

`src/Asynkron.JsEngine/Ast/TypedCpsTransformer.cs`
- Rewrites certain shapes into `Promise(...).then(...)` chains (non-blocking at runtime).
- Uses internal helpers like `__awaitHelper` (see `src/Asynkron.JsEngine/StdLib/StandardLibrary.IteratorHelpers.cs`).

However, module evaluation today executes many statements via `ExecuteTypedStatement(...)` (direct typed eval) which bypasses this program-level transformation for module bodies.

## Recommended next steps (to remove engine threadpool blockage)

### 1) Make top-level-await module evaluation fully non-blocking

Current async module evaluation still hits blocking waits via `await` expression handling.

Candidate designs:
- **Option A: module-specific step runner**
  - Build a lightweight “module plan” executor that can suspend at awaited promises and resume via `then`.
  - Reuse the async-generator `Pending` model (but execute module statements, not generator statements).
- **Option B: extend generator IR building to handle module bodies**
  - Add an instruction type for `AwaitExpression` (similar to `YieldInstruction` + resume slot), then execute via a step API.
  - This keeps the “program counter + resume value” model that already exists.
- **Option C: restructure module execution to run a transformed program**
  - Avoid per-statement evaluation for module bodies when possible so the typed CPS transformer can apply.
  - Still requires preserving module import/export semantics (imports/exports are not normal statements).

### 2) Remove `AwaitScheduler.TryAwaitPromiseSync` from supported runtime paths

Either:
- replace it with a continuation-based scheduler integrated with the engine’s event queue, or
- ensure supported paths never call it (i.e., always run async code through CPS or a step runner).

As long as this synchronous pump stays reachable, long-running `await` chains can keep a worker thread busy.

### 3) Reduce threadpool churn for the event loop

Once synchronous waits are removed, threadpool pressure is dominated by:
- per-engine event loop tasks (`Task.Run`)

Possible direction (needs design + isolation validation):
- run the event loop on a dedicated single-thread scheduler per engine (not the shared threadpool)
- (optional) install a custom single-thread `SynchronizationContext` so `await` continuations stay on that engine thread

## Notes / constraints to keep in mind

- Engine should remain ECMAScript-aligned (strict vs sloppy must remain correct).
- No thread-blocking (`Task.Wait`, `Task.Result`, `.GetAwaiter().GetResult()`, `Thread.Sleep`) in engine runtime.
- Unsupported runtime features/AST shapes should fail fast with clear `NotSupportedException` (no silent fallback).
