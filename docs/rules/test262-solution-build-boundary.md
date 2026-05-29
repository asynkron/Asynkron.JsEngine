# Test262 Solution Build Boundary

Default solution builds must not select the generated Test262 project.

## Rules

1. Keep `tests/Asynkron.JsEngine.Tests.Test262` listed in
   `Asynkron.JsEngine.sln` for IDE navigation and explicit project-targeted
   Test262 commands.
2. Do not add solution `Build.0` entries for the Test262 project in Debug or
   Release solution platforms as part of routine solution-file maintenance.
3. When editing `Asynkron.JsEngine.sln`, check the Test262 project GUID remains
   `ActiveCfg`-only unless the task explicitly redesigns the build contract.
4. Run Test262 through its project, runsettings, focused filters, or maintained
   regression-pack scripts. Do not rely on `dotnet build Asynkron.JsEngine.sln`
   to generate or validate Test262.
5. If a future task intentionally reintroduces Test262 solution-build selection,
   require a new ADR or an update to ADR 0127 plus current proof that the
   main-health solution build completes without triggering the generated-suite
   inactivity failure.

## Why

Issue #1859 / PR #1862 fixed a red main-health build where
`dotnet build Asynkron.JsEngine.sln` selected
`tests/Asynkron.JsEngine.Tests.Test262`, generated 93,709 cases, and then
produced no output long enough for the Faktorial guard to fail the build. The
durable fix was to keep the Test262 project in the solution but remove its
solution `Build.0` entries, so ordinary solution builds stay bounded while
explicit Test262 commands still work.
