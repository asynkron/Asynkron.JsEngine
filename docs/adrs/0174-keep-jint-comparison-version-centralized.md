# ADR 0174: Keep Jint comparison version centralized

## Status

Accepted

## Context

Issue #2099 was a recurring dependency-maintenance child for 2026-05-26. The
selected slice found the same Jint package version hard-coded in the two
comparison surfaces:

- `tools/ProfileRunner/ProfileRunner.csproj`
- `benchmarks/Asynkron.JsEngine.Benchmarks/Asynkron.JsEngine.Benchmarks.csproj`

Both projects used `Jint` `4.9.2`. That duplication did not require a package
upgrade, but it created a quiet drift risk: profiling comparisons and
BenchmarkDotNet comparison runs could end up measuring against different Jint
versions after a future routine dependency sweep.

PR #2107 preserved the resolved package version and changed only dependency
metadata. It added `JintVersion` to `Directory.Build.props` and changed both
Jint `PackageReference` entries to use `$(JintVersion)`.

The accepted proof was intentionally narrow:

```text
rtk git diff --check
rtk dotnet build tools/ProfileRunner/ProfileRunner.csproj -c Release
rtk dotnet build benchmarks/Asynkron.JsEngine.Benchmarks/Asynkron.JsEngine.Benchmarks.csproj -c Release
```

Both Release builds completed with 3 projects, 0 errors, and 0 warnings.

## Decision

Keep the Jint comparison package version centralized in `Directory.Build.props`.

`tools/ProfileRunner/ProfileRunner.csproj` and
`benchmarks/Asynkron.JsEngine.Benchmarks/Asynkron.JsEngine.Benchmarks.csproj`
are one comparison-tooling owner set for Jint version maintenance. Future Jint
dependency work should update the shared `JintVersion` property and keep both
project files referencing that property.

Do not reintroduce project-local Jint version literals in one of those project
files during routine recurring dependency maintenance. If profiling and
BenchmarkDotNet comparisons ever need intentionally different Jint versions,
that split needs a dedicated issue with compatibility rationale, changed proof
commands, and explicit notes explaining why the comparison baselines should no
longer share one version.

Do not use this small centralization as a reason to migrate the repository to
full Central Package Management or to bundle unrelated package upgrades into a
routine dependency child run.

## Consequences

- ProfileRunner and BenchmarkDotNet comparison runs use the same Jint package
  line by default, so performance comparisons cannot drift through independent
  manifest edits.
- Routine Jint updates have one metadata target, `JintVersion`, with two
  direct owner projects to prove.
- The narrow proof remains project-owned: build ProfileRunner and the benchmark
  project after dependency metadata changes, and add comparison smoke only when
  the package update itself changes runtime behavior risk.
- Any intentional Jint version split becomes visible as policy work rather than
  accidental dependency drift.

## Related

- Issue #2099 / PR #2107
- `.claude/rules/dependency-maintenance.md`
- `Directory.Build.props`
- `tools/ProfileRunner/ProfileRunner.csproj`
- `benchmarks/Asynkron.JsEngine.Benchmarks/Asynkron.JsEngine.Benchmarks.csproj`
