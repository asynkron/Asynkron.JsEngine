# ADR 0216: Keep SimpleArithmetic Profiler Scope Comparable Before Runtime Retries

## Status

Accepted

## Context

Issue `autrun-dit5vu9h44b4-963ded0d32` / PR #2275 selected the
`simplearithmetic` profile from the recurring optimizer benchmark table. The
required focused baseline stayed around 258-270 ms for Asynkron while Jint stayed
around 74-77 ms, so the slice looked narrow enough to try a runtime optimizer.

The required CPU command was run three times:

```bash
rtk ./tools/profile simplearithmetic --cpu --calltree-depth 40 --calltree-width 40
```

That exact profiler path did not expose a stable engine-owned arithmetic
hotspot. It was dominated by `Program.Main`, `JsEngine.ctor`,
`JsEngine.ParseProgram`, and plan-building frames. A rooted follow-up showed the
reason: `benchmark.sh` wraps `simplearithmetic` in an IIFE, but `tools/profile`
does not pass the equivalent `--wrap-iife` path for this profile. The script has
top-level `let` declarations, so repeated shared-engine profiler iterations hit
caught redeclaration errors after warmup and spend time formatting/reporting
exceptions instead of measuring the intended arithmetic execution path.

Two runtime/tooling experiments were reverted:

1. a completed-task timeout fast path in ProfileRunner; and
2. a public pre-parsed `EvaluateSync(ProgramNode)` path for synchronous
   ProfileRunner profiles.

Neither produced a repeatable 10% win, and both were invalid evidence for this
selected profile while the profiler workload still diverged from the benchmark
workload.

## Decision

Keep future `simplearithmetic` optimizer retries blocked on comparable profiler
scope before changing runtime evaluation APIs or expression-bytecode execution.

For `simplearithmetic`, and any repeated ProfileRunner workload with top-level
lexical declarations, profiler evidence must first prove it is executing the
same scoped workload as the benchmark comparison. Acceptable proof is either:

1. the profiler wrapper uses the same IIFE wrapping/fresh lexical scope behavior
   as `benchmark.sh`; or
2. the profile script is reshaped so repeated iterations cannot trip top-level
   lexical redeclaration before the measured hot path.

Do not treat a call tree from repeated caught redeclaration errors as an engine
hotspot. Do not retry the reverted completed-task timeout fast path or public
`EvaluateSync(ProgramNode)` profile-runner path for `simplearithmetic` without a
clean wrapped/fresh-scope CPU profile first.

## Consequences

- The existing `JsEngine.Evaluate(ProgramNode)` task-shaping boundary from ADR
  0207 remains the accepted synchronous completion optimization.
- A clean CPU profile must come before expression-bytecode or public evaluation
  API changes for this benchmark.
- If the profiler command still exercises repeated top-level `let`
  redeclaration errors, the correct outcome is failed-attempt documentation or a
  dedicated profiling-tooling fix, not a runtime shortcut.
- Performance claims still require repeated focused timing rows and the issue's
  improvement threshold; profiler parity only makes the hotspot evidence usable.

## Related

- `docs/performance/failed-simplearithmetic-profiler-sync-evaluate-trials.md`
- `docs/adrs/0207-keep-evaluate-synchronous-completion-task-shaped.md`
- `.claude/rules/performance-profiling-guardrails.md`
