# ADR 0131: Keep command-line solution builds analyzer-bounded

## Status

Accepted

## Context

Issue #1873 was filed after local main-health verification failed on
`main@6f781a4` while running:

```bash
dotnet build Asynkron.JsEngine.sln
```

The failure was not a C# compile error. The verifier log showed the solution
build had produced no output for five minutes, which tripped the local
main-health inactivity guard. A baseline rerun proved the command could exit
successfully, but it took `00:09:35.07`, leaving the same no-output/inactivity
risk shape in place.

The repository already treated internal quality builds as analyzer-bounded:
`Makefile` passes `/p:RunAnalyzers=false` through its build arguments. The red
main-health path used a raw command-line solution build instead, so it missed
that existing build-stability policy.

PR #1877 fixed the incident by adding a default in `Directory.Build.props`:
command-line builds outside Visual Studio set `RunAnalyzers=false` when the
caller has not already chosen a value. Visual Studio builds can still run live
analyzers, and explicit command-line analyzer runs can still opt in with
`/p:RunAnalyzers=true`.

The final proof was:

```bash
rtk proxy time dotnet build Asynkron.JsEngine.sln
```

It exited successfully with warnings only, emitted project output during the
build, and completed in `00:05:01.51`. `git diff --check` passed, and
`tests/Asynkron.JsEngine.Tests.Test262/Generated` remained absent.

## Decision

Keep ordinary command-line solution builds analyzer-bounded by default.

`Directory.Build.props` owns the cross-cutting default: when `RunAnalyzers` is
unset and the build is not running inside Visual Studio, set `RunAnalyzers` to
`false`. Do not rely only on Makefile-local arguments for this policy, because
main-health and agent repair commands may invoke `dotnet build
Asynkron.JsEngine.sln` directly.

This default is not a ban on analyzers. IDE builds can continue to run live
analyzers, and command-line analyzer validation remains available through an
explicit opt-in such as `/p:RunAnalyzers=true` or a dedicated analyzer command.

## Consequences

- `dotnet build Asynkron.JsEngine.sln` stays usable as a bounded main-health
  signal instead of a long-running analyzer pass.
- Analyzer validation remains an explicit task, not an accidental side effect
  of every raw command-line solution build.
- Future changes to `Directory.Build.props`, solution build policy, or
  main-health commands must preserve this default unless they also provide a
  current main-health proof that raw command-line solution builds complete
  within the inactivity window.
- This decision is caused by issue #1873 / PR #1877 and complements
  `.claude/rules/command-line-solution-builds.md`.
