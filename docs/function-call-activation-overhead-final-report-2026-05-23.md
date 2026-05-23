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

The post-commit orchestrator `run-quality` profile ran the canonical
`make quality` gate against commit `305e21f7`.

Result:
- `git diff --check`: passed
- `make build-internal`: passed
- `make test-internal-no-build`: passed
- Internal VSTest summary: Passed 4,074, Failed 0, Skipped 2, Total 4,076
- Internal VSTest duration: 36 s
- Embedded web app asset freshness: skipped; `internal/web/app` is not present
  in this worktree.
- Post-quality worktree cleanliness: clean
- Verification result:
  `planitem-planmanual1779530433702731000-reduce-function-call-activation-overhead-p-0b25b4f88b-1779550438838778000`
  finished with status `pass`.

### Big Test262 Throughput

```bash
rtk dotnet test tests/Asynkron.JsEngine.Tests.Test262 -c Release --settings tests/Asynkron.JsEngine.Tests.Test262/LanguageTests.runsettings
```

Result:
- VSTest summary: Passed 43,010, Failed 4, Skipped 801, Total 43,815
- VSTest duration: 20 m 33 s
- `rtk` summary: 43,010 passed, 4 failed, 801 skipped, 3 warnings in 2
  projects, 1,540.1 s
- Process result: non-zero because the full LanguageTests run currently has 4
  failures.
- Results file:
  `/var/folders/2s/_6fw48n95h59ks_q6mn61_5w0000gn/T/rtk_dotnet_testresults_19e557e140d127621/_Plutten_2026-05-23_17_40_32.trx`
- Captured console log:
  `/tmp/jsengine-throughput/language-tests-2026-05-23.log`

Failed tests:
- `ModuleCode("language/module-code/instn-star-ambiguous.js", True)`:
  ambiguous export expectation mismatch. This path is covered by existing ADR
  `docs/adrs/0091-keep-module-namespace-construction-resolution-lazy.md`, so
  it is not activation-specific evidence.
- `Statements_for_dstr("language/statements/for/dstr/var-ary-ptrn-rest-id-iter-close.js", True)`:
  `ReferenceError: x is not defined` during iterator-close destructuring.
- `Statements_forOf_dstr("language/statements/for-of/dstr/var-ary-ptrn-rest-id-iter-close.js", True)`:
  `ReferenceError: x is not defined` during iterator-close destructuring.
- `Statements_with("language/statements/with/S12.10_A1.11_T5.js", False)`:
  dynamic `with` scope lookup produced `ReferenceError: value is not defined`.
  The `Statements_with` area has existing durable guidance in
  `docs/adrs/0052-keep-dynamic-with-scope-cleanup-boundaries-identity-based.md`.

Interpretation:
- AC-4 is now satisfied by whole-suite LanguageTests throughput evidence.
- The failures are broad language/module/dynamic-scope cases rather than
  focused function-call activation regressions.
- The run does not prove improved broad-suite throughput because no prior
  comparable whole-suite throughput baseline was present in this worktree.

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
- The full LanguageTests throughput run provides a current broad-suite point
  at 43,815 tests in 1,540.1 s, but no comparable prior whole-suite throughput
  report was present, so it cannot support an improvement claim.
- The strongest remaining owner surfaces are still `JsEnvironment`, `JsSlot[]`,
  `ExecutionPlanRunner`, `EvaluationContext`, mapped/observable arguments object
  storage, and eval-sensitive scope construction.

## Closeout Conclusion

Focused activation semantics are green, the activation-adjacent Test262 subset
produced a 1,223/1,223 passing VSTest summary, and the full LanguageTests run
completed with 43,010 passed, 4 failed, 801 skipped, and 43,815 total. The
post-commit canonical `run-quality` gate also passed. The aggregate profiler ran
successfully with all activation profiles included.

The evidence does not support a strong "activation overhead is reduced" claim in
this closeout, because the comparable activation baseline referenced by the
handoff is absent from this worktree. With the available committed baseline, the
safe conclusion is:

- no focused semantic regression was observed;
- full LanguageTests throughput is now recorded, but does not prove a broad
  throughput win without a retained comparable baseline;
- `forloop` allocation remains stable at about 7.03-7.05 MB;
- activation setup cost is still dominated by environment, slot-array, runner,
  context, arguments-object, and eval-scope allocations;
- any next optimization should target those owner surfaces and rerun the same
  activation profiles with a retained comparable baseline directory.
