# ADR 0127: Keep Test262 out of default solution build

## Status

Accepted

## Context

Issue #1859 was filed after local main-health verification failed on
`main@e8151d6` while running:

```bash
dotnet build Asynkron.JsEngine.sln
```

The failure was not a source compile error in the engine. The default solution
build selected `tests/Asynkron.JsEngine.Tests.Test262`, triggered Test262 source
generation for 93,709 cases, and then produced no output long enough for the
Faktorial main-health guard to classify the command as red.

The Test262 project still needs to remain discoverable in the solution for IDE
navigation and explicit targeted Test262 runs. The expensive part is making
ordinary solution builds select the project through solution `Build.0`
configuration entries.

PR #1862 fixed the incident by removing the Test262 project `Build.0` entries
from every Debug/Release solution platform while keeping its `ActiveCfg` entries
and project membership intact. A targeted final build:

```bash
rtk proxy dotnet build Asynkron.JsEngine.sln -v:minimal -nr:false /p:UseSharedCompilation=false
```

passed in 2:01 with warnings only, and
`tests/Asynkron.JsEngine.Tests.Test262/Generated` remained absent, proving the
default solution build no longer generated Test262 sources.

## Decision

Keep `tests/Asynkron.JsEngine.Tests.Test262` listed in `Asynkron.JsEngine.sln`,
but keep it out of default solution builds.

The solution should retain `ActiveCfg` entries for the Test262 project so IDEs
and direct project-targeted commands can find the intended configuration. The
solution must not carry Test262 `Build.0` entries unless a future change
intentionally redesigns the main-health build contract and proves the generated
suite fits within that contract.

Run Test262 explicitly through its project or maintained runner scripts when a
Test262 issue or regression pack requires it. Do not use broad solution builds
as the mechanism for Test262 coverage.

## Consequences

- `dotnet build Asynkron.JsEngine.sln` stays suitable as a routine main-health
  signal without generating the full Test262 suite.
- Test262 remains available for explicit commands such as targeted project
  filters, full class runsettings, and regression-pack runners.
- Future solution-file edits must preserve the absence of Test262 `Build.0`
  entries unless they also update this ADR and provide a current main-health
  proof.
- This decision is caused by issue #1859 / PR #1862 and complements
  `.claude/rules/test262-solution-build-boundary.md`.
