# Benchmarking

Use `benchmark.sh` as the stable entrypoint for the ProfileRunner/Jint
comparison matrix.

## Timing Matrix

Run the default sync JavaScript comparison set:

```bash
rtk ./benchmark.sh
```

Run the short smoke set:

```bash
rtk ./benchmark.sh --smoke
```

Run selected profiles:

```bash
rtk ./benchmark.sh fib forloop ir-arithmetic
```

Skip rebuilding ProfileRunner when it is already current:

```bash
rtk ./benchmark.sh --no-build fib
```

The default matrix intentionally avoids async/generator throughput profiles.
Use this separate probe for tiny Jint async/await behavior:

```bash
rtk ./benchmark.sh --async-smoke-only
```

Do not use `rtk ./tools/profile --compare` for this matrix. That command runs
the separate BenchmarkDotNet Jint comparison suite.

After the table identifies a top case where Asynkron.JsEngine loses to Jint,
use [CPU and allocation profiling workflow](how-to-profile.md) to inspect that
profile with `asynkron-profiler`.

## Allocation Comparison

Use `--allocations` (alias: `--memory`) to add managed allocation columns to the
same comparison table:

```bash
rtk ./benchmark.sh --allocations
```

For a narrower allocation check:

```bash
rtk ./benchmark.sh --allocations simplearithmetic recursion-lite
```

Expected columns:

```text
profile  asynkron_ms  asynkron_kb  jint_ms  jint_kb  time_delta  alloc_delta
```

`asynkron_kb` and `jint_kb` are managed allocated KB during the measured
iteration loop. `alloc_delta` reports which engine allocated less and by what
ratio.

ProfileRunner measures allocations with `GC.GetTotalAllocatedBytes(precise:
true)` around the measured iterations. It forces a full GC before measurement.
Treat the result as managed allocation pressure, not retained heap size.

## Allocation Regression Gate

`tools/check-allocation-regression` guards the evaluator hot-path allocation
wins (PR #2661 SyncFunctionInvoker static-analysis caching, commit `bd70ca63`
`JsValue` struct coercion, PR #2658 typed-AST property-key caching) against
silent regression. It re-measures **Asynkron-only** managed allocations for the
benchmark smoke set (`fib forloop ir-arithmetic functioncalls
functioncalls-lite`) and compares them to the committed baseline at
`tools/allocation-baseline.txt`.

> Note: this repo has no GitHub Actions CI (Actions were intentionally removed).
> The gate is a standalone, deterministic command wired into the local
> [`/pre-pr`](../.claude/commands/pre-pr.md) quality path (Step 5). It does not
> run Jint, so it is cheap. If true PR-CI is ever desired, that is a separate
> owner decision; the gate command is ready to be called from any CI.

Run the gate:

```bash
rtk ./tools/check-allocation-regression
```

It prints a per-profile table and exits non-zero with a clear diff when any
profile regresses beyond tolerance. Skip the rebuild when ProfileRunner is
already current:

```bash
rtk ./tools/check-allocation-regression --no-build
```

### Tolerance model

Allocation counts are nearly deterministic, but a fixed ~24 KB measurement blip
occasionally lands inside the measured loop. That blip is a large percentage on
the small profiles (`ir-arithmetic`, `forloop`) yet negligible on large ones.
So the gate fails a profile only when its increase exceeds the **larger** of:

- a percentage headroom (`--tolerance`, default `15`%, or `ALLOC_GATE_TOLERANCE`), and
- an absolute floor (`--abs-floor`, default `49152` bytes, or `ALLOC_GATE_ABS_FLOOR`).

The percentage dominates large profiles (catches proportional regressions); the
floor absorbs the fixed noise on small profiles.

### Baseline-refresh path (one command)

When an intentional change legitimately moves the allocation numbers, refresh
the committed baseline and commit it:

```bash
rtk ./tools/check-allocation-regression --update
git add tools/allocation-baseline.txt && git commit
```

`--update` re-measures the smoke set and rewrites `tools/allocation-baseline.txt`
(values are Asynkron managed allocated bytes per profile, with a header noting
how they were generated).

## Fairness Contract

The comparison runner parses/prepares each script before the measured loop:

- Asynkron parses to `ProgramNode` once and reuses it.
- Jint prepares the script once with `Engine.PrepareScript(...)` and reuses it.

This keeps timing and allocation numbers focused on execution instead of parser
setup. Profiles that run with fresh engines still reuse the prepared script but
create a new engine for each measured iteration.

## Profile Inventory

List available profiles through the underlying runner:

```bash
rtk ./tools/ProfileRunner/bin/Release/net10.0/ProfileRunner list
```

Profile definitions live in `tools/profile-manifest.json`; scripts live in
`tools/profile-scripts/`.
