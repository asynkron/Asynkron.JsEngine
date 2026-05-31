# ADR 0304: Keep simplearithmetic return-cleanup iterator skip performance-gated

## Status

Accepted

## Context

Issue `autrun-diwvrsz5equg-75bc74c803` / PR #2848 continued the recurring
optimizer work by selecting `simplearithmetic` from the current benchmark table:

```text
profile                    asynkron_ms  jint_ms  delta
simplearithmetic                   452      104  Jint 4.35x faster
```

The required focused pre-edit rows were noisy (`1461`, `934`, and `1057` ms for
Asynkron), but the selected profile remained a current loss. Three required CPU
profiles used:

```bash
rtk ./tools/profile simplearithmetic --cpu --calltree-depth 40 --calltree-width 40
```

The repeated owner surface stayed on expression-program execution:

```text
ExecuteInstructionLoop
-> HandleEvaluateAndDiscard
-> EvaluateExpressionProgram
-> ExecuteProgramCall
-> InvokeCallableNoArgs
-> SyncFunctionInvoker.InvokeWithContext
-> SyncFunctionInvoker.TryInvokeIrFast
```

The same profiles also sampled return cleanup under simple function returns:

```text
HandleReturn
-> CompleteReturn
-> CloseActiveIterators
-> ScanEnvironmentForActiveIterators
```

The attempted runtime experiment added a plan-level boolean derived from
`MayCreateActiveIteratorState` instruction analysis and used it in
`CompleteReturn` to skip `CloseActiveIterators` when a plan could not create
internal active iterator state. A companion no-try return check avoided lazy
try/catch state allocation by inspecting the nullable backing field first.
Focused return, iterator, and finally semantics passed while the experiment was
applied.

The post-edit CPU profile removed the sampled
`CompleteReturn -> CloseActiveIterators -> ScanEnvironmentForActiveIterators`
subtree, but selected-profile timing did not retain a wall-clock win. A
same-window A/B check showed the stashed baseline at `501`, `453`, and `435` ms
for Asynkron, while the restored experiment produced noisy/regressed rows of
`1328`, `1816`, and `1625` ms. The runtime edit was reverted, and the retained
delivery was the failed-attempt note only.

## Decision

Keep the `simplearithmetic` return-cleanup iterator-skip shape performance-gated.

Do not retain or retry a `CompleteReturn` optimization that only skips
`CloseActiveIterators` / `ScanEnvironmentForActiveIterators` for
`simplearithmetic` unless fresh repeated selected-profile A/B rows clear the
current issue's improvement threshold. Removing a sampled cleanup subtree proves
the branch was reached and the metadata can describe it, but PR #2848 showed
that this micro-slice did not produce a retainable wall-clock improvement under
the current workload noise.

Future work may still optimize return cleanup, but it needs a quieter or more
isolated workload where active-iterator scanning is a larger measured share of
execution time. If the next profile still names expression-program execution as
the dominant owner, the useful slice is likely under expression bytecode or
function invocation rather than a return-cleanup-only shortcut.

## Consequences

- The failed performance note remains useful evidence, but it is not a retained
  optimization and must not be cited as a runtime win.
- Future `simplearithmetic` performance work should read the failed-attempt
  note before changing `CompleteReturn`, active-iterator scan policy, or
  no-try return-check state.
- Cleaner CPU call trees are supporting evidence only; repeated selected-profile
  timing remains the acceptance boundary for performance-only runtime changes.
- Any future return-cleanup optimization must preserve iterator-close and
  finally semantics and prove the narrowed owner with focused tests plus current
  profile evidence.

## Related

- `docs/performance/failed-simplearithmetic-return-cleanup-iterator-skip.md`
- `docs/adrs/0216-keep-simplearithmetic-profiler-scope-comparable-before-runtime-retries.md`
- `docs/rules/performance-profiling-guardrails.md`
