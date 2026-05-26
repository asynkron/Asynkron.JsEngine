# CPU and Allocation Profiling

Use this workflow when benchmark results show Asynkron.JsEngine losing to Jint
and you need the concrete hot path or allocation source.

## Start From Benchmarks

Start with the comparison matrix:

```bash
rtk ./benchmark.sh
```

For managed allocation pressure in the same table:

```bash
rtk ./benchmark.sh --allocations
```

Pick one of the top cases where Asynkron.JsEngine loses to Jint. Prefer a
profile with a large ratio and a clear feature owner, for example `classdef`,
`arrayops`, `activation-arguments-lite`, or `recursion-lite`.

## CPU Profiling

Run CPU profiling through the repo wrapper:

```bash
rtk ./tools/profile BENCHMARK-NAME --cpu --calltree-depth 40 --calltree-width 40
```

Example:

```bash
rtk ./tools/profile classdef --cpu --calltree-depth 40 --calltree-width 40
```

The wrapper builds `tools/ProfileRunner`, then runs `asynkron-profiler` against
that profile. For CPU mode it also applies repo defaults when you do not
override them:

- `--filter Asynkron.JsEngine`
- `--root ExecuteInstructionLoop` for most IR script profiles
- `--root InvokeWithContextSlow` for `activation-*` profiles
- `--root EvaluateExpressionProgram` for `bytecode`

Override the root when the default hides the relevant frame:

```bash
rtk ./tools/profile activation-arguments --cpu --root InvokeWithContextSlow --calltree-depth 40 --calltree-width 40
```

## Allocation Profiling

Run allocation profiling with `--memory`:

```bash
rtk ./tools/profile BENCHMARK-NAME --memory --calltree-depth 40 --calltree-width 40
```

Example:

```bash
rtk ./tools/profile classdef --memory --calltree-depth 40 --calltree-width 40
```

In memory mode the wrapper defaults the root to `Asynkron.JsEngine` unless you
pass `--root` yourself. Use this to find allocation-heavy types and call trees
inside the engine after `rtk ./benchmark.sh --allocations` has shown an
allocation gap versus Jint.

If you need both CPU and memory for the same profile, omit the mode:

```bash
rtk ./tools/profile classdef --calltree-depth 40 --calltree-width 40
```

With no explicit `--cpu` or `--memory`, the wrapper runs separate CPU and memory
passes with the repo defaults above.

## Reading Results

For CPU reports, start with the filtered hot functions and then inspect the
call tree under the selected root. Look for engine-owned frames before changing
general runtime infrastructure.

For memory reports, focus on total allocated size first, then the top allocated
types and allocation call tree. Treat memory reports as sampled allocation
evidence; confirm wins with `rtk ./benchmark.sh --allocations BENCHMARK-NAME`
after a change.

## Useful Variants

List available ProfileRunner profiles:

```bash
rtk ./tools/profile list
```

Run the profiler against Jint for a profile when you need a rough call-shape
comparison:

```bash
rtk ./tools/profile classdef --jint --cpu --calltree-depth 40 --calltree-width 40
```

Use the local profiler checkout instead of the installed `asynkron-profiler`
tool:

```bash
rtk ./tools/profile classdef --debug --cpu --calltree-depth 40 --calltree-width 40
```

Do not use `rtk ./tools/profile --compare` for the ProfileRunner/Jint matrix.
That command runs the separate BenchmarkDotNet comparison suite.
