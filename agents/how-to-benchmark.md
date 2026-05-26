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
