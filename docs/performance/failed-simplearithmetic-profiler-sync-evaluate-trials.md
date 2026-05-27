# Failed SimpleArithmetic Profiler and Sync-Evaluate Trials

Date: 2026-05-27

## Selected Profile

The required full comparison table selected `simplearithmetic` as a narrow
current loss:

```text
profile                    asynkron_ms  jint_ms  delta
simplearithmetic                   256       74  Jint 3.46x faster
```

Repeated focused baseline rows stayed in the same range:

```text
simplearithmetic                270       74  Jint 3.65x faster
simplearithmetic                260       77  Jint 3.38x faster
simplearithmetic                258       75  Jint 3.44x faster
```

Baseline timestamp: 2026-05-27T04:11:00Z
Baseline signal: `simplearithmetic` Asynkron focused rows = 270, 260, 258 ms

## CPU Profile Evidence

The required profile command was run three times:

```bash
rtk ./tools/profile simplearithmetic --cpu --calltree-depth 40 --calltree-width 40
```

The exact command did not expose a stable engine-owned hotspot. The filtered
tables were dominated by `Program.Main lambda`, `JsEngine.ctor`,
`JsEngine.ParseProgram`, and plan-building frames, with only small samples under
`ExecuteInstructionLoop` and `EvaluateExpressionProgram`.

An additional rooted inspection showed why this selected profile was a poor
runtime-edit target through `tools/profile`: unlike `benchmark.sh`, the profile
wrapper does not pass `--wrap-iife` for `simplearithmetic`. The script contains
top-level `let` declarations, so repeated shared-engine profiler iterations
hit redeclaration errors after warmup and spend time formatting and printing
caught exceptions instead of measuring a clean arithmetic hot path.

## Trials

### Completed-task timeout fast path

I tried a narrow `ProfileRunner` fast path that skipped `Task.WaitAsync` when
`JsEngine.Evaluate(ProgramNode)` had already completed successfully. The async
and faulted paths still used the existing timeout/error handling.

Focused timing did not improve and was slightly slower:

```text
simplearithmetic                275      232  Jint 1.19x faster
simplearithmetic                283       85  Jint 3.33x faster
simplearithmetic                275       80  Jint 3.44x faster
```

The change was reverted.

### Pre-parsed sync-evaluate runner path

I tried adding a public pre-parsed `EvaluateSync(ProgramNode)` overload and
using it for synchronous `ProfileRunner` profiles. The intent was to remove
the public `Evaluate` task/event-loop wrapper from sync benchmark iterations.

The selected focused row still missed the required 10% win:

```text
simplearithmetic                265       75  Jint 3.53x faster
```

The rooted CPU profile also made the trial invalid for this selected profile:
the exact profiler command was still exercising repeated unwrapped top-level
`let` redeclaration errors. The sync-evaluate change was reverted.

## Final Signal

No runtime or benchmark-tooling change was retained. After reverting the
experiments, the focused row returned to the baseline range:

```text
simplearithmetic                253       75  Jint 3.37x faster
```

Final timestamp: 2026-05-27T04:20:45Z
Final signal: `simplearithmetic` Asynkron = 253 ms after reverting experiments
Signal delta: no retained speedup; runtime and tooling experiments were
reverted because they did not meet the repeatable 10% improvement threshold.

## Outcome

This run is a failed-attempt evidence slice. Future `simplearithmetic`
optimizer work should first make the profiling evidence comparable to
`benchmark.sh` by ensuring the profile runner uses the same IIFE wrapping for
top-level `let` workloads, then re-run CPU profiles before touching expression
bytecode or public evaluation APIs.

## Follow-up: profile-wrapper parity fix

Build slice `gh2278` aligns `tools/profile` with `benchmark.sh` for the
selected `simplearithmetic` shape by forwarding `--wrap-iife` to ProfileRunner
for this profile (including `tools/profile all`).

Baseline timestamp: 2026-05-27T05:50:53Z
Baseline signal: `rtk ./benchmark.sh simplearithmetic` Asynkron row = 268 ms (Jint 77 ms); `rtk ./tools/profile ...` runner invocation had no `--wrap-iife`
Final timestamp: 2026-05-27T05:51:18Z
Final signal: `rtk ./benchmark.sh simplearithmetic` Asynkron row = 286 ms (Jint 90 ms); `rtk ./tools/profile ...` runner invocation includes `--wrap-iife simplearithmetic`
Signal delta: +18 ms Asynkron and +13 ms Jint in one focused sample (timing-noisy); profiler invocation parity changed from unwrapped to wrapped.
