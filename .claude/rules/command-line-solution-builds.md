# Command-Line Solution Builds

Default command-line solution builds must stay bounded and observable enough for
main-health verification.

## Rules

1. Keep the repository-level default that sets `RunAnalyzers=false` for
   command-line builds outside Visual Studio when the caller did not provide an
   explicit value.
2. Do not move this policy solely into `Makefile` targets or ad hoc shell
   wrappers. Raw commands such as `dotnet build Asynkron.JsEngine.sln` are part
   of the main-health contract and must inherit the default from
   `Directory.Build.props`.
3. Preserve explicit analyzer opt-in. A task that needs analyzer validation may
   pass `/p:RunAnalyzers=true` or use a dedicated analyzer command, but routine
   solution-build health checks should not become accidental analyzer runs.
4. If a future change removes or weakens this default, require a current
   main-health proof showing raw command-line solution builds complete inside
   the inactivity window and continue to emit useful progress output.

## Why

Issue #1873 / PR #1877 fixed a red main-health build where
`dotnet build Asynkron.JsEngine.sln` was not failing compilation, but produced
no output for five minutes and tripped the local verifier inactivity guard. The
repo already disabled analyzers for `make quality`; the durable fix was to put
the same bounded-build policy in `Directory.Build.props` so raw command-line
solution builds also inherit it while Visual Studio and explicit analyzer runs
remain available.
