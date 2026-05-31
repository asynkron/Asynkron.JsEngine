# Failed simplearithmetic return-cleanup iterator skip

Date: 2026-05-31
Issue: autrun-diwvrsz5equg-75bc74c803

## Slice

This run selected `simplearithmetic` from the live full benchmark table because
it was the largest current Asynkron-vs-Jint ratio loss:

```text
profile                    asynkron_ms  jint_ms  delta
simplearithmetic                   452      104  Jint 4.35x faster
```

Baseline timestamp: 2026-05-31T14:07:00Z
Baseline signal: `simplearithmetic` full-table Asynkron row = 452 ms

Focused pre-edit rows were much noisier but still confirmed the selected
profile as a current loss:

```text
simplearithmetic               1461      305  Jint 4.79x faster
simplearithmetic                934      397  Jint 2.35x faster
simplearithmetic               1057      312  Jint 3.39x faster
```

## CPU profile evidence

The required CPU profile command was run three times before editing:

```bash
rtk ./tools/profile simplearithmetic --cpu --calltree-depth 40 --calltree-width 40
```

The repeated owner surface was:

```text
ExecuteInstructionLoop
-> HandleEvaluateAndDiscard
-> EvaluateExpressionProgram
-> ExecuteProgramCall
-> InvokeCallableNoArgs
-> SyncFunctionInvoker.InvokeWithContext
-> SyncFunctionInvoker.TryInvokeIrFast
```

The profiles also showed return cleanup work under simple function returns:

```text
HandleReturn
-> CompleteReturn
-> CloseActiveIterators
-> ScanEnvironmentForActiveIterators
```

That cleanup was a plausible narrow slice because the selected workload does
not create array-pattern or for-of iterator state.

## Attempted change

The reverted implementation added a plan-level boolean derived from the existing
`MayCreateActiveIteratorState` instruction analysis. `CompleteReturn` used that
boolean to skip `CloseActiveIterators` when no instruction in the plan could
create an internal active iterator state. The same experiment also avoided
allocating the lazy try/catch state for no-try return checks by reading the
nullable backing field first.

Focused semantics passed while the experiment was applied:

```bash
rtk dotnet test tests/Asynkron.JsEngine.Tests --filter "FullyQualifiedName~IteratorCloseGeneratorTests|FullyQualifiedName~TryFinally|FullyQualifiedName~CompletionValueDebugTests|FullyQualifiedName~JsEvaluatorTests.Finally|FullyQualifiedName~FoundationTests.TryCatch" -- xUnit.MaxParallelThreads=1 -timeout 20000
```

```text
67 tests passed, 0 warnings
```

The final CPU profile did remove the sampled `CompleteReturn ->
CloseActiveIterators -> ScanEnvironmentForActiveIterators` subtree from
`simplearithmetic`, but timing did not support a retained performance win.

## Timing result

First post-edit focused rows were mixed:

```text
simplearithmetic                291      137  Jint 2.12x faster
simplearithmetic                346      232  Jint 1.49x faster
simplearithmetic               1574      592  Jint 2.66x faster
simplearithmetic               2750      221  Jint 12.44x faster
simplearithmetic                600      188  Jint 3.19x faster
simplearithmetic                618      229  Jint 2.70x faster
```

A same-window A/B check made the result non-retainable. With the experiment
temporarily stashed and rebuilt, the baseline rows were:

```text
simplearithmetic                501      138  Jint 3.63x faster
simplearithmetic                453      140  Jint 3.24x faster
simplearithmetic                435      144  Jint 3.02x faster
```

After restoring the experiment, the comparable rows regressed badly while the
machine was also showing severe timing noise:

```text
simplearithmetic               1328     1095  Jint 1.21x faster
simplearithmetic               1816     2739  Asynkron 1.51x faster
simplearithmetic               1625     1441  Jint 1.13x faster
```

The runtime change was reverted. A final post-revert rebuild returned the row to
the same broad range as the current baseline:

```text
simplearithmetic                516      287  Jint 1.80x faster
simplearithmetic                685      181  Jint 3.78x faster
simplearithmetic                575      163  Jint 3.53x faster
```

Final timestamp: 2026-05-31T14:26:38Z
Final signal: `simplearithmetic` retained-runtime Asynkron row = 516 ms
Signal delta: no retained runtime improvement; the attempted cleanup was
reverted because repeated selected-profile timing did not clear the 10% gate.

## Interpretation

Skipping impossible active-iterator scans can make the CPU profile cleaner for
small no-iterator functions, but the current `simplearithmetic` benchmark is too
noisy for this micro-slice to support a retained wall-clock claim. Future work
should avoid retrying this return-cleanup-only shape unless a quieter selected
workload isolates return cleanup as a larger share of measured execution time.
