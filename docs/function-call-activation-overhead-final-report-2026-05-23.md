# Function Call Activation Overhead Closeout (2026-05-23)

## Scope

This report closes the proof-and-performance slice for the
`reduce function-call activation overhead` plan. No runtime activation code was
changed in this closeout pass; the work here is evidence capture and comparison
against the baselines available in this worktree.

Relevant semantic guardrails:
- `docs/adrs/0099-keep-function-activation-slot-shape-plan-owned.md`
- `docs/adrs/0100-keep-observable-arguments-binding-eval-aware-through-arrows.md`
- `docs/adrs/0101-keep-function-call-argument-carriers-typed-through-hot-paths.md`

## Commands And Results

### Focused Activation Semantics

```bash
rtk dotnet test tests/Asynkron.JsEngine.Tests --filter "FullyQualifiedName~ActivationSemanticsProofPackTests"
```

Result:
- Passed: 26
- Failed: 0
- Skipped: 0
- Duration reported by `rtk`: 1.5 s
- Existing warnings only; no activation proof failure.

### Test262 Activation-Adjacent Filter

```bash
rtk dotnet test tests/Asynkron.JsEngine.Tests.Test262 -c Release --settings tests/Asynkron.JsEngine.Tests.Test262/LanguageTests.runsettings --filter "FullyQualifiedName~EvalCode_direct|FullyQualifiedName~EvalCode_indirect|FullyQualifiedName~Expressions_function|FullyQualifiedName~FunctionCode|FullyQualifiedName~Statements_function|FullyQualifiedName~Statements_class_arguments"
```

Result:
- VSTest summary: Passed 1,223, Failed 0, Skipped 0, Total 1,223
- VSTest duration: 6 m 41 s
- Results file:
  `/var/folders/2s/_6fw48n95h59ks_q6mn61_5w0000gn/T/rtk_dotnet_testresults_19e556b1ba2f5741/_Plutten_2026-05-23_17_21_05.trx`
- Caveat: the broad combined command was interrupted after the VSTest result
  summary was printed, so `rtk` reported a non-zero wrapper status even though
  the assembly summary itself showed 1,223/1,223 passing.

### Aggregate Performance Profiler

```bash
rtk ./tools/performance-profiler.sh
```

Result:
- Output directory: `tools/profile-output/performance_20260523_172838`
- Summary: `tools/profile-output/performance_20260523_172838/summary.txt`
- Insights: `tools/profile-output/performance_20260523_172838/insights.txt`
- Static checks: `tools/profile-output/performance_20260523_172838/static-checks.txt`
- Included activation profiles:
  - `activation-noargs`
  - `activation-params`
  - `activation-arguments`
  - `activation-closures`
  - `activation-evalscope`
  - `functioncalls-lite`

### Canonical Quality Gate

The in-agent closeout did not run `rtk make quality` after writing this report.
The build-stage handoff requests the orchestrator `run-quality` profile so the
canonical internal build/test gate runs after the committed report is present.

### Big Test262 Throughput

The full `LanguageTests.runsettings` throughput run was not completed in this
agent turn. The attempted activation-adjacent Test262 command generated 93,709
test cases and completed the filtered 1,223-test subset successfully. A complete
LanguageTests throughput number remains a follow-up proof item if the plan owner
requires whole-suite throughput instead of focused activation/eval/function
coverage for this closeout.

## Current Profiler Snapshot

Memory numbers below are sampled allocation totals from
`tools/profile-output/performance_20260523_172838/summary.txt`.

| Profile | Root | Total allocated | Top allocation rows |
| --- | --- | ---: | --- |
| `activation-noargs` | `InvokeWithContextSlow` | 437.46 MB | `JsEnvironment` 115.18 MB; `ExecutionPlanRunner` 89.97 MB; `EvaluationContext` 56.42 MB |
| `activation-params` | `InvokeWithContextSlow` | 502.00 MB | `JsEnvironment` 92.61 MB; `JsSlot[]` 85.39 MB; `ExecutionPlanRunner` 74.62 MB |
| `activation-arguments` | `InvokeWithContextSlow` | 1.48 GB | `JsSlot[]` 201.61 MB; `Entry<String,PropertyDescriptor>[]` 148.32 MB; `PropertyDescriptor` 112.14 MB |
| `activation-closures` | `InvokeWithContextSlow` | 433.40 MB | `JsSlot[]` 79.60 MB; `EvaluationContext` 46.67 MB; `JsEnvironment` 45.65 MB |
| `activation-evalscope` | `InvokeWithContextSlow` | 1.70 GB | `TokenType[]` 196.51 MB; `HashSet<Symbol>` 105.53 MB; `JsEnvironment` 78.79 MB |
| `functioncalls-lite` | `ExecuteInstructionLoop` | 4.56 GB | `JsEnvironment` 914.98 MB; `JsSlot[]` 791.25 MB; `ExecutionPlanRunner` 731.99 MB |
| `forloop` | `ExecuteInstructionLoop` | 7.03 MB | `JsValue[]` 2.52 MB; `String` 1.42 MB; `PropertyDescriptor` 519.51 KB |

CPU sampling also keeps call-entry overhead visible:
- `activation-noargs`: `TryInvokeSimpleIrActivationFast` appears under
  `InvokeWithContextSlow`, with `ExecutePlan`, `ExecuteInstructionLoop`,
  `EvaluateExpressionProgram`, and `ExecuteProgramCall` prominent in the top
  frames.
- `activation-params`: `EvaluateExpressionProgram`,
  `HandleCompoundAssignmentSlotSlow`, and `ExecuteProgramCall` dominate useful
  frames.
- `activation-arguments`: allocation cost moves toward observable arguments
  shape: `JsSlot[]`, property descriptor tables, `HashSet<Symbol>`, strings,
  `EvaluationContext`, and `JsEnvironment`.
- `activation-evalscope`: eval-sensitive execution still pays parser/token and
  dynamic-scope metadata costs, with `TokenType[]`, `HashSet<Symbol>`,
  `JsEnvironment`, and `EvaluationContext` all visible.
- `functioncalls-lite`: broad function-call throughput remains dominated by
  `JsEnvironment`, `JsSlot[]`, `ExecutionPlanRunner`, and
  `EvaluationContext`.

## Baseline Comparison

The investigation handoff cited
`tools/profile-output/performance_20260523_113537/summary.txt` as a broad
profile baseline. That directory is not present in this worktree, so no
like-for-like activation-profile delta can be computed from the cited path.

The available committed baseline is
`docs/expression-bytecode-baseline-2026-05-22.md`, which includes `forloop`
allocation evidence:

| Evidence | Baseline | Current |
| --- | ---: | ---: |
| `forloop` sampled total allocated | 7.05 MB | 7.03 MB |
| `forloop` `JsValue[]` | 2.52 MB | 2.52 MB |
| `forloop` `JsSlot[]` | not in top rows | 211.17 KB |

Interpretation:
- The loop allocation profile is stable and slightly lower than the 2026-05-22
  baseline by sampled total, but the delta is only 0.02 MB and should be treated
  as stable/no-regression rather than a material win.
- The current aggregate profiler proves activation workloads are included and
  produces useful current numbers, but it does not prove an activation setup
  reduction without the missing comparable activation baseline.
- The strongest remaining owner surfaces are still `JsEnvironment`, `JsSlot[]`,
  `ExecutionPlanRunner`, `EvaluationContext`, mapped/observable arguments object
  storage, and eval-sensitive scope construction.

## Closeout Conclusion

Focused activation semantics are green, and the activation-adjacent Test262
subset produced a 1,223/1,223 passing VSTest summary. The aggregate profiler ran
successfully with all activation profiles included.

The evidence does not support a strong "activation overhead is reduced" claim in
this closeout, because the comparable activation baseline referenced by the
handoff is absent from this worktree. With the available committed baseline, the
safe conclusion is:

- no focused semantic regression was observed;
- `forloop` allocation remains stable at about 7.03-7.05 MB;
- activation setup cost is still dominated by environment, slot-array, runner,
  context, arguments-object, and eval-scope allocations;
- any next optimization should target those owner surfaces and rerun the same
  activation profiles with a retained comparable baseline directory.
