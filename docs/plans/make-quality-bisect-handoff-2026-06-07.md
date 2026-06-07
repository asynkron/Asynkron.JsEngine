# make quality regression bisect handoff - 2026-06-07

## Current status

Resolved locally on 2026-06-07:

- `rtk make quality` passes: 6,884 passed, 2 skipped.
- The TDZ/catch-slot failure is fixed by allocating missing activation slot mappings around flat slots already used by other scopes.
- The stack overflow in `FinallyReturnCallAdmissionTests.SloppySelfRecursiveFinallyReturnCall_Computes` is fixed by:
  - declining all production function routes with a call returned from inside `finally`; and
  - evaluating direct legacy `return f(...)` expressions through the legacy call evaluator instead of standalone expression bytecode when the strict tail-restart fast path did not apply.
- The large Test262 async-generator/destructuring wall was localized with layered tests and addressed by restoring the last-known-good async-generator `ExecutionPlanRunner` fallback behind the unified resumable bytecode decline path. Unified-admitted async generators still route through `UnifiedBytecodeVirtualMachine.ExecuteResumable`; declined shapes now log `async-generator-runner-fallback` and compute instead of throwing.
- A late `make quality` regression in `UnifiedBytecodeResumableWithTests.AsyncFunctionWithBodyAfterAwait_RoutesResumableAndKeepsWithScope` was fixed by mirroring dynamic assignment references that resolve to declarative bindings back into unified flat slots. This keeps materialized `with`/body environments and flat-slot `LoadSlot` reads consistent after async resume.

Verification that passed:

```bash
rtk dotnet test tests/Asynkron.JsEngine.Tests/Asynkron.JsEngine.Tests.csproj -c Debug --filter "FullyQualifiedName~FinallyReturnCallAdmissionTests|FullyQualifiedName~Test262AsyncGeneratorDestructuringLayeredTests" --logger "console;verbosity=minimal" -- xUnit.MaxParallelThreads=1 -timeout 20000

rtk dotnet test tests/Asynkron.JsEngine.Tests/Asynkron.JsEngine.Tests.csproj -c Debug --filter "FullyQualifiedName~UnifiedBytecodeProductionInvocationTests.SourceGate_ProductionUnifiedBytecodeScriptAndResumableAcceptedPaths_DoNotDelegateToAstOrExecutionPlanRunner|FullyQualifiedName~ProductionRouteCoverageRatchetTests" --logger "console;verbosity=minimal" -- xUnit.MaxParallelThreads=1 -timeout 20000

rtk make quality
```

Attempted direct Test262 filtered verification with:

```bash
rtk dotnet test tests/Asynkron.JsEngine.Tests.Test262/Asynkron.JsEngine.Tests.Test262.csproj -c Debug --filter "FullyQualifiedName~Statements_forAwaitOf" --logger "console;verbosity=minimal" -- xUnit.MaxParallelThreads=1 -timeout 20000
```

That command did not emit output for several minutes and no matching `dotnet`/`testhost` process was visible afterwards, so the direct Test262 project proof was not completed in this run. The internal layered reproduction for the same cluster is covered by `Test262AsyncGeneratorDestructuringLayeredTests`.

## Current Test262 residual

Latest user-reported full Test262 run after the follow-up fixes is 12 failures:

- `BuiltInsTests.Array_prototype_entries("built-ins/Array/prototype/entries/resizable-buffer.js", false/true)`
- `BuiltInsTests.TypedArray_prototype_entries("built-ins/TypedArray/prototype/entries/resizable-buffer.js", false/true)`
- `Intl402Tests.DateTimeFormat_prototype_formatRangeToParts("intl402/DateTimeFormat/prototype/formatRangeToParts/temporal-objects-resolved-time-zone.js", false/true)`
- `Intl402Tests.DateTimeFormat_prototype_formatToParts("intl402/DateTimeFormat/prototype/formatToParts/temporal-objects-resolved-time-zone.js", false/true)`
- `LanguageTests.Statements_forIn("language/statements/for-in/head-var-bound-names-dup.js", false/true)`
- `LanguageTests.Statements_try("language/statements/try/S12.14_A15.js", false/true)`

The `for-in/head-var-bound-names-dup.js` pair was probed with a broad `AddPlanSlotSymbols` experiment. It made the focused internal repro pass but regressed `LanguageTests.Statements_forIn` from 2 failures to 18 by making internal `__forIn_value_*` names visible to script-level dynamic lookup. That experiment was reverted; the broad method is back to 196 passed / 2 failed.

## Earlier Test262 residual after async-generator fallback restore

Earlier `.testrunner/summary.md` snapshot:

- Timestamp: 2026-06-07 17:27:19
- Duration: 529.6s
- Passed: 4,870
- Failed: 218
- Crashed: 28
- Hanging: 0

Largest failed method buckets:

```text
 51  LanguageTests.Expressions_yield
 20  LanguageTests.Expressions_super
 18  LanguageTests.Expressions_class_elements
 18  LanguageTests.Statements_class_elements
 11  LanguageTests.Expressions_compoundAssignment
 10  LanguageTests.Expressions_object
 10  LanguageTests.Statements_class
  9  BuiltInsTests.Object_defineProperties
  6  AnnexBTests.Language_globalCode
  6  LanguageTests.Statements_with
```

Largest crashed method buckets:

```text
  8  LanguageTests.Expressions_leftShift
  8  LanguageTests.Expressions_rightShift
  8  LanguageTests.Expressions_unsignedRightShift
  2  BuiltInsTests.AsyncGeneratorPrototype_next
  2  BuiltInsTests.GeneratorPrototype_next
```

Representative yield run:

```bash
rtk /opt/homebrew/bin/timeout 120s dotnet test tests/Asynkron.JsEngine.Tests.Test262/Asynkron.JsEngine.Tests.Test262.csproj -c Debug --no-build --filter "FullyQualifiedName~LanguageTests.Expressions_yield" --logger "console;verbosity=minimal" -- xUnit.MaxParallelThreads=1 -timeout 20000
```

Result: 72 passed, 51 failed. The failures are now generator/yield semantics, not the old async-generator destructuring wall. Observed patterns:

- `language/expressions/yield/from-with.js` yields `1` for the second result instead of `2`, so a suspended generator is not restoring or observing the active `with` environment for the resumed yield operand.
- `rhs-unresolvable.js` and `star-rhs-unresolvable.js` let the `ReferenceError` escape as an unhandled top-level throw instead of being caught by the generator's internal `try/catch`.
- Many `yield *` abrupt-completion/protocol cases throw through `ExecutionPlanRunner.HandleContextSignals` instead of completing inside the generator and reaching its local `catch`.

Representative crash run:

```bash
rtk /opt/homebrew/bin/timeout 120s dotnet test tests/Asynkron.JsEngine.Tests.Test262/Asynkron.JsEngine.Tests.Test262.csproj -c Debug --no-build --filter "FullyQualifiedName~LanguageTests.Expressions_leftShift" --logger "console;verbosity=minimal" -- xUnit.MaxParallelThreads=1 -timeout 20000
```

Result: test host aborts with stack overflow after 33 passing cases. The repeated stack is:

```text
UnifiedBytecodeCompiler.TryAppendFirstBoundaryCallTargetPreparation
UnifiedBytecodeCompiler.TryAppendExpressionProgramOps
UnifiedBytecodeCompiler.TryCompileBlock
UnifiedBytecodeCompiler.TryCompileTarget
UnifiedBytecodeCompiler.TryCompileBlock
UnifiedBytecodeCompiler.TryCompileTarget
...
```

This looks like a unified-bytecode compiler recursion/CFG traversal failure exposed by old bit-shift stress tests, not a left-shift arithmetic assertion failure.

Recommended next order:

1. Fix or conservatively decline the compiler recursion crash path first; stack overflow kills the host and makes broader Test262 runs less trustworthy.
2. Then attack `LanguageTests.Expressions_yield` with layered generator tests around `yield`, `yield *`, abrupt completion, `try/catch`, and `with` environment restoration.
3. Then handle the class/private/super clusters, which currently account for most of the remaining non-generator language failures.

## Historical symptoms

`make quality` was failing on current `main` after the recent unified-bytecode work.

Observed failures:

- `TdzClosureTest.ClosureTdz_AssignBeforeInit_ShouldThrowReferenceError`
  - Expected: `true|ReferenceError`
  - Actual: `true|TypeError`
- Full `make quality` also aborts later with a stack overflow. The pasted stack shows a repeated path through:
  - `TypedAstEvaluator.SyncFunctionInvoker.InvokeWithContextSlow`
  - `UnifiedBytecodeVirtualMachine.ExecutePreparedCall`
  - `UnifiedBytecodeExpressionProgramExecutor.ExecuteDynamic`
  - production unified-bytecode script/function invocation

The TDZ failure is deterministic and much faster to reproduce than full `make quality`.

## 2026-06-07 focused TDZ root cause

The TDZ failure was narrowed with an internal `__debug()` probe plus a compiler
program dump. No production `__debug` extension was needed.

Runtime state immediately before `f()`:

- The materialized function body environment was correct.
- `f` existed in body slot `0` and held a `SyncFunctionInvoker`.
- `x` existed in body slot `1` and was still TDZ/uninitialized as expected.

Compiler state before the fix:

```text
activation scope=2000001 count=2
activationNames=0:f, 1:x
flatMapping scope=1000001: 0->0
slotCount=2
slotNames=0:e, 1:x
callTarget[0] kind=Identifier slot=0 name=e
```

That is the concrete bug: catch binding `e` reused flat slot `0` and overwrote
the activation slot name for hoisted function `f`. The VM's
`SyncEnvironmentToUnifiedSlots` uses `UnifiedBytecodeProgram.SlotNames`, so it
looked for `e` before the catch existed and left the call target slot as
`undefined`. The later call to `f()` therefore failed with `TypeError` before the
TDZ write to `x` ran.

Fix direction implemented locally:

- In `UnifiedBytecodeCompiler.EnsureActivationSlotMappings`, when adding missing
  activation-scope mappings, first account for flat slots already used by other
  scopes.
- Allocate activation flat slots around those used slots instead of blindly
  mapping activation slot `N` to flat slot `N`.
- Add a regression test proving catch `e`, hoisted function `f`, and lexical `x`
  occupy distinct unified slots and the call target points at `f`.

Compiler state after the fix:

```text
slotCount=3
slotNames=0:e, 1:f, 2:x
lexicalSlots=2
callTarget[0] kind=Identifier slot=1 name=f
```

Focused proofs that passed after the fix:

```bash
rtk dotnet test tests/Asynkron.JsEngine.Tests/Asynkron.JsEngine.Tests.csproj -c Debug --filter "FullyQualifiedName~TdzClosureTest" --logger "console;verbosity=normal" -- xUnit.MaxParallelThreads=1 -timeout 20000

rtk dotnet test tests/Asynkron.JsEngine.Tests/Asynkron.JsEngine.Tests.csproj -c Debug --filter "FullyQualifiedName~UnifiedBytecodeProductionEligibilityTests.Evaluate_CatchBindingDoesNotOverwriteActivationFunctionCallTargetSlot" --logger "console;verbosity=normal" -- xUnit.MaxParallelThreads=1 -timeout 20000
```

## Fast repro commands

Focused repro:

```bash
rtk dotnet test tests/Asynkron.JsEngine.Tests/Asynkron.JsEngine.Tests.csproj -c Debug --filter "FullyQualifiedName~TdzClosureTest.ClosureTdz_AssignBeforeInit_ShouldThrowReferenceError" -- xUnit.MaxParallelThreads=1 -timeout 20000
```

Related issue-3373-adjacent check that was passing during the investigation:

```bash
rtk dotnet test tests/Asynkron.JsEngine.Tests/Asynkron.JsEngine.Tests.csproj -c Debug --filter "FullyQualifiedName~UnifiedBytecodeProductionInvocationTests.ForOfLetHead_CapturedPerIterationBinding" -- xUnit.MaxParallelThreads=1 -timeout 20000
```

Full repro:

```bash
rtk make quality
```

## Narrowed facts

The failing JS shape is:

```js
var caught = false;
var errorType = null;

(function() {
  function f() { x = 1; }

  try {
    f();
  } catch (e) {
    caught = true;
    errorType = e.name;
  }

  let x;
}());

caught + "|" + errorType;
```

Expected result is `true|ReferenceError`.

Additional probes showed:

- The uncaught closure TDZ assignment shape does throw a real `ReferenceError`.
- The caught shape records `TypeError`.
- A simpler function-declaration-in-try shape also fails:

```js
(function() {
  function f() { return 1; }
  try {
    return f();
  } catch (e) {
    return e.name + ":" + e.message;
  }
}());
```

That simpler shape produced a `TypeError` saying the callee was `undefined`, so TDZ is probably incidental. The deeper problem appears to be a hoisted function declaration being read as `undefined` when called inside a protected `try` path.

Temporary diagnostic printing of the IIFE execution plan showed:

```text
=== Execution Plan (10 instructions, entry: 9) ===

  [  0] RETURN
  [  1] VAR Let x -> [0]
  [  2] LEAVE_TRY -> [1]
  [  3] POP_ENV (scopeId: 1000001, pool: False) -> [2]
  [  4] ASSIGN errorType = e .name -> [3]
  [  5] ASSIGN caught = true -> [4]
  [  6] EnterCatchInstruction { ... CatchBindingProgram = IdentifierBindingTargetProgram { Name = e, ScopeId = 1000001, SlotIndex = 0, FlatSlotId = 0 }, ... }
  [  7] EVAL_DISCARD call.f call/0 -> [2]
  [  8] ENTER_TRY (handler: [6], finally: none) -> [7]
-> [  9] FUNC_DECL (hoisted noop) -> [8]
```

Notable details:

- `FUNC_DECL (hoisted noop)` means the function value should already be available through function-entry hoisting.
- `call.f` inside the try reads `undefined` in the failing runtime path.
- Catch binding `e` had `FlatSlotId = 0` in the diagnostic plan, but changing unified-bytecode catch descriptor remapping did not change the failing result, so the failure is earlier than the unified compiler catch descriptor or on a different route.

## Tried and reverted

These experiments did not fix the focused failure and were reverted:

- Reverting commit `7eaece652 updates` with `git revert --no-commit`.
- Changing unified-bytecode catch descriptor binding remap to ignore `IdentifierBindingTargetProgram.FlatSlotId` and map by catch scope.
- Adding a `FlatSlots` fallback to bind root flat slots to an enclosing callable binding when the local slot was undefined.
- Adding a VM mirror after `DeclareFunction`.
- Temporary diagnostic output in `TdzClosureTest`.

At the time this note was written, tracked files were restored to `HEAD` before starting bisect.

## Current hypotheses

Most likely:

- A recent unified-bytecode migration/admission change routes a function body with root hoisted function declarations plus `try/catch` through a production bytecode or flat-slot path before the hoisted function declaration value is visible to the prepared call target.

Secondary:

- Slot stamping may be assigning a flat slot to a hoisted function declaration read even though that flat slot is not populated for protected execution paths.

Less likely:

- TDZ store classification itself. Direct uncaught TDZ closure assignment throws the correct `ReferenceError`; the caught shape only sees `TypeError` because the call to `f()` appears to fail before the TDZ assignment executes.

## Bisect plan

Start from current `main`, use the focused TDZ test first because it is fast and deterministic. Once the first bad commit is found, confirm with `rtk make quality`.

Suggested bisect run command:

```bash
rtk git bisect start
rtk git bisect bad HEAD
rtk git bisect good <known-good-commit>
rtk git bisect run dotnet test tests/Asynkron.JsEngine.Tests/Asynkron.JsEngine.Tests.csproj -c Debug --filter "FullyQualifiedName~TdzClosureTest.ClosureTdz_AssignBeforeInit_ShouldThrowReferenceError" -- xUnit.MaxParallelThreads=1 -timeout 20000
```

Potential known-good anchors to test manually before choosing:

- `6cef82057 Admit private receiver prefix updates`
- `4888decfe docs: record async fallback route ordering lesson (#3371)`
- Older commits before the latest unified-bytecode burndown batch if the first anchor is already bad.

After bisect, reset with:

```bash
rtk git bisect reset
```

## Bisect notes from 2026-06-07 run

The focused TDZ test is not a valid first-bad oracle across history:

- It was introduced at `2d4f939f8 Add TDZ bug analysis and test cases`.
- At that commit it already failed, but with `false|null`.
- At later commits it failed with `true|TypeError`.

Full `rtk make quality` was used as the oracle instead.

Known good:

- `5acdd30a5 Tighten ProfileRunner SLO gate evidence (#2908)` passed full `rtk make quality`.

Known bad during bisect:

- `22044c8c Phase B: admit captured-closure activations into the resumable VM (salvaged from stalled agent)`
  - Failed `ResumableAlreadyRoutingPinTests.B32_TryFinallyNonEmptyRunsFinally_Routes`.
- `1695cdcd Bind final rest parameters in bytecode environments (#3066)`
  - Failed with stack overflow through production unified-bytecode function invocation.
  - Stack included `UnifiedBytecodeVirtualMachine.Execute`, `SyncFunctionInvoker.TryInvokeProductionUnifiedBytecode`, `ExecutePreparedCall`, and `TailCallTests.StrictSameFunctionTailCall_InFinallyReturnDoesNotGrowCallDepth`.
- `2c687f44 Tighten unified opcode inventory audit (#2981)`
  - Failed multiple tests: IR loop instruction-form tests, const assignment tests, slot fast-path tests, tail-call tests, and TDZ/var-hoisting tests.
- `5b1d7740 feat: admit parameter var declarations in unified bytecode`
  - Failed multiple tests: IR loop instruction-form tests, const assignment tests, slot fast-path tests, TDZ/var-hoisting tests, tail-call tests.
- `c348285d feat: support reference errors in unified bytecode`
  - Failed multiple tests in the same broad family.
- `3eda504d feat: route slot updates through unified bytecode`
  - Failed const assignment tests and `SlotOptimizationTests.SlotFastPath_ShouldBeUsedForLoopVariables`.

Known good during bisect:

- `d00f75b4 Document gh2955 async generator yield-star learnings (#2961)` passed full `rtk make quality`.
- `09f496c6 test: guard unified bytecode vm opcode coverage` passed full `rtk make quality`.

The bisect run was killed at the user's request while testing:

- `a22b6b6e test: audit unified bytecode instruction compiler coverage`

The narrowed range at stop time was effectively:

```text
good: 09f496c6 test: guard unified bytecode vm opcode coverage
bad:  3eda504d feat: route slot updates through unified bytecode
```

Interpretation:

- The first broad `make quality` break appears to be in the very small window where slot update routing entered unified bytecode.
- This is earlier than the current `main` stack-overflow/TDZ symptoms, so later commits may have changed the visible failure mode from const/slot-fast-path failures into production-call stack overflow plus TDZ `true|TypeError`.
- The working version is very likely still using older IR/AST infrastructure for these shapes, while the failing versions route more of the same semantics through unified bytecode.

Useful next step after a Test262 run:

- Compare failing Test262 clusters against the `09f496c6..3eda504d` diff first.
- Then compare current `main` failures against the later admissions after `3eda504d` to identify where the failure mode changed from const/slot update failures to stack overflow / hoisted function call / TDZ catch behavior.

## Test262 run notes from 2026-06-07

Latest inspected `.testrunner/summary.md`:

- Passed: `261`
- Failed: `5063`
- Crashed: `53`
- Unique failed/crashed method clusters: `69`

The largest clusters were:

```text
1184 Statements_class_dstr
1184 Expressions_class_dstr
 775 Statements_forAwaitOf
 696 Expressions_asyncGenerator_dstr
 348 Statements_asyncGenerator_dstr
 348 Expressions_object_dstr
 120 ArgumentsObject
  51 Expressions_yield
  51 EvalCode_direct
  40 Expressions_asyncGenerator
```

The top six clusters accounted for roughly 89% of the failed/crashed rows.

Focused sampling showed many of those failures are explicit route rejections
rather than silent wrong-state bugs:

- `ArgumentsObject` sample failed with
  `Async-generator body is not eligible for unified bytecode routing after IR fallback retirement: Arguments-object-dependent execution is not eligible for resumable unified bytecode routing.`
- `Statements_class_dstr` / `Expressions_asyncGenerator_dstr` samples failed with
  `Non-simple async-generator parameter lists are not eligible until resumable invocation owns IteratorBindingInitialization.`
- `Statements_forAwaitOf` samples failed through a nested async-generator body
  inside the for-await fixture, again via the retired async-generator fallback
  route.

Interpretation:

- The huge Test262 cluster is largely separate from the TDZ/catch slot collision
  fixed above.
- It aligns with ADR 0363 / checklist E6: async-generator IR fallback has been
  retired and some valid Test262 async-generator shapes now fail as explicit
  unsupported-route exceptions.
- If Test262 is used next, separate "explicit async-generator unsupported route"
  buckets from wrong-result/runtime-state buckets before attempting fixes.
