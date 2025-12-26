## Reference array iterator hang (fast paths off)

- Repro script (fails only when `EnableFastPaths = false`):
  ```js
  let arr = [10, 20, 30];
  let keys = Array.from(arr.keys());
  let logs = [];
  let finalI = -1;
  for (let i = 0; i < keys.length; i++) {
      logs.push(i);
      finalI = i;
      if (logs.length > 5) {
          break;
      }
  }
  JSON.stringify({ logs, finalI, length: keys.length });
  ```
  - Fast paths ON: `{"logs":[0,1,2],"finalI":2,"length":3}`
  - Fast paths OFF: `{"logs":[0,0,0,0,0,0],"finalI":0,"length":3}` (loop keeps iterating, observable in `Reference_ArrayIteratorMethodsTests.Array_Keys_CanBeIterated` hang)

- Likely culprit: reference (non-fast) path for `++/--` on `let` loop variables inside `LoopPlan` execution with per-iteration environments. With fast paths disabled the increment goes through `EvaluateUnaryMemberIncrement`/`AssignmentReferenceResolver`, and the binding for `i` in the per-iteration environment does not update, so the loop condition never progresses.

- Fix ideas:
  1) Make `LoopPlan` execution respect `EnableFastPaths` and route to the reference loop evaluator when fast paths are disabled, or
  2) Fix the reference increment path for per-iteration `let` bindings (check `ResolveIdentifierAssignmentReference` → `AssignmentReference.ForDeclarativeBinding` write path) so `i++` actually mutates the per-iteration slot/environment.

- Impact: Reference mode array iterator tests hang; potential infinite loop risk for user code with `for (let i...)` when fast paths are off.

## Additional investigation (logging run)

- Instrumented a standalone harness with `Logger = Console` and `EnableFastPaths=false`. Logged identifier slot reads/writes.
- Observed in reference mode:
  - `i` writes happen and appear to increment (`Write binding 'i' ... = 1/2/3/4/5`), but the loop body still sees `i` as `0` (logs array becomes `[0,0,0,0,0,0]`, `finalI` stays `0`).
  - `Loop iteration N: i=M` log from the loop plan increments (reaches `i=5`), but the body computations (push/assign) read `i=0`.
  - Slot read traces show hits on `i` with `scopeId=1 slot=0`; `finalI` assignments always write `0`, confirming the value the body sees is still `0`.
- This indicates reads and writes are happening against different bindings/environments:
  - Increment (`i++`) writes land on one binding (likely the loop environment), while body reads use another binding (the per-iteration environment) that never updates.
  - Result: the loop condition sees `i` stuck at `0` in the per-iteration environment, so it never terminates.

### Suspected root cause

- Per-iteration lexical env and loop env are diverging in reference mode: `AssignmentReferenceResolver`/`WriteResolvedBindingJsValue` is updating the loop env binding, but `EvaluateStatement`/identifier slot reads for `i` in the body/condition come from the per-iteration env.
- Possible contributors:
  - `LoopPlan` still forces fast-path execution even when `RealmState.EnableFastPaths` is false, so the pooled per-iteration environment path is used without the reference write path being aware of per-iteration slots.
  - Slot maps might be attached to the loop env while the per-iteration env gets a separate binding; the reference increment path updates the loop env value but not the per-iteration env slot, leaving the per-iteration binding at its initial `0`.
  - `CreatePerIterationEnvironment` uses `outerEnvironment = currentIterationEnvironment.Enclosing ?? currentIterationEnvironment`; on the first call (`currentIterationEnvironment` = loop env), the new per-iteration env encloses the loop **parent** (global), not the loop env. This could cause identifier resolution to prefer the loop env binding while the body evaluates in the per-iteration env, leading to split bindings.

### Next steps to confirm

- Confirm whether the per-iteration env’s `_values`/slots ever change during the reference run (e.g., add targeted logging for per-iteration env binding updates).
- Verify the `outerEnvironment` choice in `CreatePerIterationEnvironment` and whether per-iteration env should enclose the loop env instead of the loop env’s parent.
- Try running without forcing fast paths inside `LoopPlan.EvaluateLoopPlanJsValue` when `EnableFastPaths` is false to see if the reference evaluator updates the per-iteration binding correctly.

### 2025-02-10 updates

- Removed hard-coded `enableFastPaths = true` sites in the engine so loop/assignment fast paths now honor `RealmState.EnableFastPaths` (LoopPlan numeric fast path and assignment slot fast path now read the setting instead of forcing true).
- Test attempt after this change: `dotnet test ... --filter "Reference_ArrayIteratorMethodsTests.Array_Keys_CanBeIterated"` with a 120s timeout still hung; process was killed by timeout and MSBuild reported `MSB4166 Child node exited prematurely` (likely due to the timeout). Need a shorter, instrumented run or a smaller repro to see if the hang remains with fast paths disabled and the forced fast-path toggle removed.
- Logging hooks: Realm logger/FakeLogger can track environment creation. Existing patterns in tests (`IdentifierSlotLoggingTests`, `ReferenceLoopDiagnosticTests`, `ForAwaitOfLayeredTests`) show how to inspect `logger.Collector.Snapshot()` for env/slot events. Plan: wire FakeLogger into the repro and log `CreatePerIterationEnvironment` + binding writes/reads to confirm which env carries `i`.
- Spec reminder: for `for (let ...)` the loop has a declarative environment for loop variables and creates a fresh per-iteration environment each turn; the loop body should execute against that per-iteration env (no separate extra body env unless the body is a block creating its own scope). Need to ensure the per-iteration env encloses the loop env and that reads/writes for `i` hit the same env instance per iteration.

### Next actions (research mode)

- Use the existing `ReferenceLoopDiagnosticTests` + `FakeLogger` pattern to add logging for environment creation/copy inside `CreatePerIterationEnvironment` and identifier reads/writes for `i` to see if writes land on the loop env while reads pull from the per-iteration env. (Realm logger already logs slot miss/hit events; confirm it covers env creation or add temporary logging.)
- Re-run the minimal repro via `ReferenceLoopDiagnosticTests.LetLoop_ReferencePath_ShouldWork` with a shorter timeout and logging to avoid the MSBuild timeout and capture the env chain per iteration.
- Double-check spec alignment: `CreatePerIterationEnvironment` currently sets outer = `currentIterationEnvironment.Enclosing` (loop env parent). If the first iteration starts from the loop env, this makes the per-iteration env sibling to the loop env, not child. Confirm whether this matches 13.7.4.9 and whether it explains why the body reads differ from the increment writes.

### 2025-02-10 attempt: adjust per-iteration env parent and re-run

- Changed `CreatePerIterationEnvironment` to set `outerEnvironment = currentIterationEnvironment` (so the per-iteration env directly encloses the loop env). Rationale: keep loop env slot maps visible to per-iteration env.
- Targeted test `Reference_ArrayIteratorMethodsTests.Array_Keys_CanBeIterated` still hangs; `dotnet test --filter` was aborted after ~77s (manual Ctrl+C). So the change did not resolve the reference-mode hang.
- Next: instrument env creation with FakeLogger (like `ReferenceLoopDiagnosticTests`) to capture which env holds `i` per iteration, and inspect whether non-pooled path in `CreateNextIterationEnvironment`/`CreatePerIterationEnvironment` is using the prior per-iteration env as the outer (likely wrong).

### 2025-02-11 attempt: disable pooling in reference mode + short test run

- In `LoopPlanExtensions` now:
  - Per-iteration env parent set to the loop env (as above).
  - Iteration env pooling is disabled when `EnableFastPaths == false` to avoid reusing env instances in reference mode.
  - `CreatePerIterationEnvironment` signature updated to accept the loop env explicitly; non-pooled path uses current iteration env as source and its enclosing (loop env) as the outer.
- Test `Reference_ArrayIteratorMethodsTests.Array_Keys_CanBeIterated` re-run with `dotnet test --filter` and aborted after ~10s (per instruction to keep runs short). Still appears to hang; no completion within 10s.
- Next: add FakeLogger instrumentation around env creation/copy and identifier read/write for `i` in this test to see if the per-iteration env is still disconnected from writes despite pooling being off and parent fixed.

### 2025-02-11 diagnostic run: ReferenceLoopDiagnosticTests.LetLoop_ReferencePath_ShouldWork

- Ran `ReferenceLoopDiagnosticTests.LetLoop_ReferencePath_ShouldWork` (timeout 5s). It still failed with OperationCanceled after 5s; logger messages show `Loop iteration {power of two} : i=1` pattern (i stuck at 1, iterations doubling up to 8192) and `Slot read miss`/`Slot write miss` for `name=i` repeated. This confirms the per-iteration `i` binding remains stuck despite disabling pooling and fixing parent.
- Snapshot highlights: messages include lots of `Identifier slot read miss name=i scopeId=1 slot=0` and `Identifier slot write miss name=i scopeId=1 slot=0`, plus per-iteration logs showing `i` stays 1 while iteration counts explode (fast-path loop logging from LoopPlan).
- Takeaway: reference path still resolves reads/writes via slot miss fallback to binding resolution, but the value read is frozen at 1; loop condition never progresses. Need to instrument env creation/copy to see whether the slot map or binding for `i` is missing on the per-iteration env, or whether writes are hitting loop env while reads hit per-iteration env.

### 2025-02-11 guard update for diagnostic test

- Updated `ReferenceLoopDiagnosticTests.LetLoop_ReferencePath_ShouldWork` script to include a `guard` declared outside the loop, incremented each iteration, and a break when `guard > 10`. The test now parses `{ result, guard }` and asserts `guard <= 10` and `result == 10` (so a stalled loop will fail quickly without hanging).

### 2025-02-11 guard test outcome

- New guard assertion fires: test fails quickly with "Loop guard tripped at 11" (no timeout). FakeLogger still shows `i` stuck at 1 and slot read/write misses for `i`, plus `CreatePerIterationEnvironment` log showing new env encloses loop env (same hash). So even with pooling off and parent fixed, per-iteration env keeps `i` at 1 and the guard increments past 10.

### Hypothesis about analysis vs execution

- Slot metadata is correct (scopeId=1, slot=0 for `i`), and per-iteration env is created/encloses loop env. The failure suggests the reference write path targets the loop env while reads use the per-iteration env. That implies a mismatch between scope analysis (slots on loop env) and loop execution when fast paths are OFF: the loop plan executor still routes through slot-based references resolved against the loop env, not the per-iteration env copy, so writes don't reach the per-iteration binding.
- Need to check whether reference-mode loop execution is still using slot hints (ScopeId/SlotIndex) even though fast paths are disabled; if so, slot writes may be landing on the loop env because `FindByScopeId` finds the loop env before the per-iteration env. Investigate assignment/increment resolution in reference mode to ensure it resolves to the active per-iteration env, not the original loop env.

### 2025-02-12 scope analysis assertion

- Added `ScopeAnalyzerTests.ForLetLoop_AssignsPerIterationScopeAndSlots` to assert scope analysis for `for (let i...)`: per-iteration scope id set, slot count 1, slot index 0, increment assignment carries ScopeId/SlotIndex metadata. This confirms the analyzer is stamping loop metadata correctly; the runtime divergence happens during evaluation, not analysis.

### Reference diagnostics logging tweak

- Extended env logging in `ReferenceLoopDiagnosticTests.LetLoop_ReferencePath_ShouldWork` to include slot read/write messages. Still seeing slot misses for `i` and `i` stuck at 1; runtime writes/read mismatch persists.
