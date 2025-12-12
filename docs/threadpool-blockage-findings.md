# Threadpool blockage & blocking waits (engine runtime)

This note captures the remaining *engine-level* sources of threadpool blockage discovered while profiling Test262 runs after the big per-test setup wins (base realm snapshot + suite-on-disk cache + AST caching).

## What the profiler was showing

- A large share of wall time attributed to “threadpool blockage/starvation”.
- The remaining slow time was not dominated by parsing or per-test suite IO anymore; it clustered around:
  - synchronous waits on async work (promises / module evaluation)
  - threadpool scheduling churn (event loop + timers)

## Root causes found in the current engine

### 1) Synchronous blocking waits still exist in the runtime

These are effectively the same class of issue as `Task.Wait()`/`Task.Result`: a thread is blocked while waiting for async completion.

**Call sites**
- `src/Asynkron.JsEngine/JsEngine.cs`
  - `EvaluateSyncInternal`: blocks async module evaluation via `EnsureModuleEvaluatedAsync(...).GetAwaiter().GetResult()`
  - `EvaluateInline`: same pattern for async modules
  - `EnsureModuleEvaluated`: wraps async method with `.GetResult()`
  - `WaitForAsyncModule`: blocks via `.GetResult()` (used by import evaluation in some cases)
- `src/Asynkron.JsEngine/Execution/AwaitScheduler.cs`
  - `DrainEventLoopAsync(...).GetAwaiter().GetResult()`
  - `tcs.Task.GetAwaiter().GetResult()`

**Why it hurts**
- Blocking the caller thread is expensive by itself.
- If the caller is a threadpool worker, this can amplify into threadpool starvation (especially under parallel test runners).
- Some of these waits are inside loops that repeatedly drain microtasks and/or event loop work, making the stall both long-lived and CPU-heavy.

### 2) `await` expressions outside async-generator stepping still block

In normal evaluation (non-generator), `await` ultimately routes into the blocking scheduler.

**Where this happens**
- `src/Asynkron.JsEngine/Ast/AwaitExpressionExtensions.cs` calls `AwaitScheduler.TryAwaitPromiseSync(...)` when not running under a generator instance.
- Additional blocking uses of `AwaitScheduler.TryAwaitPromiseSync(...)` exist in:
  - `src/Asynkron.JsEngine/Ast/JsObjectExtensions.cs` (`IteratorClose` awaiting promise-like return)
  - `src/Asynkron.JsEngine/Ast/TypedAstEvaluator.DelegatedYieldState.cs` (delegated yield awaiting async-iterator results)
  - `src/Asynkron.JsEngine/Ast/TypedAstEvaluator.cs` (helper wrapper)

**Important detail**
`AwaitScheduler.TryAwaitPromiseSync` currently implements awaiting by:
- building a `TaskCompletionSource`
- calling `.then(onFulfilled, onRejected)`
- spinning by draining microtasks / event loop until the TCS is completed
- finally blocking on `tcs.Task.GetAwaiter().GetResult()`

That last part is the hard thread-blocking point.

### 3) The event loop is started on the threadpool

`src/Asynkron.JsEngine/JsEngine.cs` starts the event loop with:
- `Task.Run(() => ProcessEventQueue(...))`

This means every engine instance that needs an event loop consumes threadpool resources. Under a large test suite this becomes visible as scheduling churn and contention.

### 4) Timers explicitly use `Task.Run` (threadpool) for delayed waits

`src/Asynkron.JsEngine/JsEngine.cs`
- `setTimeout` (non-zero delay) uses `Task.Run(async () => await Task.Delay(...))`
- `setInterval` uses `Task.Run(async () => while (...) await Task.Delay(...))`

These are expected to use threadpool threads. If tests schedule timers frequently, this contributes to threadpool pressure.

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

### 1) Stop blocking in sync APIs (fail fast instead)

`EvaluateSync` is already documented as not supporting async/event-loop dependent features.

So for async modules / async dependencies:
- replace `.GetAwaiter().GetResult()` in `EvaluateSyncInternal` and `EvaluateInline` with a clear `NotSupportedException`
- similarly, stop providing sync wrappers (`EnsureModuleEvaluated`, `WaitForAsyncModule`) for async modules

This removes the highest-risk thread blocking without changing async-capable entry points (`Evaluate(...)`).

### 2) Make top-level-await module evaluation non-blocking

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

### 3) Remove `AwaitScheduler.TryAwaitPromiseSync` from supported runtime paths

Either:
- replace it with a continuation-based scheduler integrated with the engine’s event queue, or
- ensure supported paths never call it (i.e., always run async code through CPS or a step runner).

As long as this blocking helper stays reachable, threadpool starvation can still reappear.

### 4) Reduce threadpool churn for the event loop and timers

Once blocking waits are removed, threadpool pressure will be dominated by:
- per-engine event loop tasks (`Task.Run`)
- per-timer tasks (`Task.Run` + `Task.Delay`)

Possible direction (needs design + isolation validation):
- run the event loop on a dedicated long-running thread per engine (not the shared threadpool)
- consolidate timers into a single scheduler per engine instead of spawning many `Task.Run` loops

## Notes / constraints to keep in mind

- Engine should remain ECMAScript-aligned (strict vs sloppy must remain correct).
- No thread-blocking (`Task.Wait`, `Task.Result`, `.GetAwaiter().GetResult()`, `Thread.Sleep`) in engine runtime.
- Unsupported runtime features/AST shapes should fail fast with clear `NotSupportedException` (no silent fallback).

