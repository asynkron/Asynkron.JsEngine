# ADR 0001: Preserve quality build-then-no-build test contract

## Status

Accepted

## Context

Issue #753 fixed `Array.prototype.lastIndexOf` Test262 behavior, but the
delivery lifecycle hit a build-back failure after the implementation was ready.
The failure was not an assertion failure: the local quality gate was SIGTERMed
while running `test-internal` after debug builds had already completed.

The repair in PR #888 changed `make quality` to build
`tests/Asynkron.JsEngine.Tests/Asynkron.JsEngine.Tests.csproj` once and then run
the already-built internal test assembly through `rtk proxy dotnet test
--no-build`. The standalone `test-internal` target still performs a normal
build-and-test run.

Issue #755 / PR #905 later exposed the same Makefile surface in review: the
quality target still needed this build-before-no-build shape, but repository
Make targets should not hardcode the local agent wrapper. The wrapper belongs
around the interactive shell command that invokes make; the Makefile itself
should use normal tool variables so non-agent environments can run the same
targets.

Issue #1830 / PR #1850 exposed a second build-gate stability boundary after the
feature fix was otherwise ready. The quality run failed while building
`tests/Asynkron.JsEngine.Tests.csproj` with `MSBUILD : error MSB4166: Child node
"2" exited prematurely`. The repair kept the same `quality` lifecycle but added
`DOTNET_BUILD_STABILITY_ARGS ?= /m:1 /nr:false` to `build-internal` so quality
builds run single-node and without MSBuild node reuse.

## Decision

Keep `make quality` as an explicit two-step contract:

1. `build-internal` compiles the internal test project.
2. `test-internal-no-build` runs that exact compiled assembly.

Do not simplify `quality` back to a single `test-internal` invocation unless the
gate timeout behavior is remeasured and the replacement is proven under the
same Faktorial quality window.

The usual guidance to avoid `dotnet test --no-build` still applies to ad hoc
test runs. The `test-internal-no-build` target is a narrow quality-gate
exception because the build step is explicit and immediately precedes it.

Keep `build-internal` stable before fast. Quality build invocations should use
the shared `DOTNET_BUILD_STABILITY_ARGS` variable, whose default serializes
MSBuild and disables node reuse. Callers may override the variable explicitly
when doing local experiments, but the default gate should not re-enable parallel
MSBuild nodes without a current proof under the same quality-run environment.

## Consequences

- Future Makefile edits must preserve the build-before-no-build relationship in
  the `quality` target.
- Future `build-internal` project additions must include
  `$(DOTNET_BUILD_STABILITY_ARGS)` on their `dotnet build` invocation.
- Makefile commands should route through configurable tool variables such as
  `DOTNET ?= dotnet` and `GIT ?= git` rather than embedding `rtk` or another
  local agent wrapper in the repository contract.
- `test-internal` remains safe as a standalone local target because it still
  builds before testing.
- Build-guide and pre-PR guidance must describe the exception so future agents
  do not "clean up" the quality gate back into the slower, previously failing
  shape.
