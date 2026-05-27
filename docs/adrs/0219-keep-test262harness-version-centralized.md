# ADR 0219: Keep Test262Harness version centralized

## Status

Accepted

## Context

Issue #2291 was a recurring dependency-maintenance child for 2026-05-27. The
selected slice found the same `Test262Harness` package version hard-coded in
the two project files that own Test262 harness consumption:

- `tests/Asynkron.JsEngine.Tests.Test262/Asynkron.JsEngine.Tests.Test262.csproj`
- `tools/ProfileRunner/ProfileRunner.csproj`

Both projects used `Test262Harness` `1.0.6`. That duplication did not require a
package upgrade, but it created drift risk after issue #1972 / PR #1978 had
already established those two projects as one aligned owner set for harness
updates.

PR #2299 preserved the resolved package version and changed only dependency
metadata. It added `Test262HarnessVersion` to `Directory.Build.props` and
changed both `Test262Harness` `PackageReference` entries to use
`$(Test262HarnessVersion)`.

The accepted proof was intentionally narrow because no package version changed:

```text
targeted Test262Harness PackageReference scan over the three touched files
rtk git diff --check
```

Review initially sent the build stage back because the handoff omitted the
recurring-child `Sibling check` and `Scope note` evidence required by AC-5. The
accepted build re-entry was evidence-only; it restated the same baseline/final
metadata signal and added the missing scope evidence without changing source
again.

## Decision

Keep the Test262Harness package version centralized in `Directory.Build.props`.

`tests/Asynkron.JsEngine.Tests.Test262/Asynkron.JsEngine.Tests.Test262.csproj`
and `tools/ProfileRunner/ProfileRunner.csproj` are one Test262 harness owner
set. Future Test262Harness dependency work should update the shared
`Test262HarnessVersion` property and keep both project files referencing that
property.

Do not reintroduce project-local Test262Harness version literals in one of
those project files during routine recurring dependency maintenance. If the
generated Test262 project and ProfileRunner ever need intentionally different
harness versions, that split needs a dedicated issue with compatibility
rationale, changed proof commands, and explicit notes explaining why fixture
generation and profiler-owned Test262 scenarios should no longer share one
harness baseline.

Do not use this small centralization as a reason to migrate the repository to
full Central Package Management or to bundle unrelated package updates into a
routine dependency child run.

## Consequences

- Test262 fixture generation and ProfileRunner scenarios use the same
  Test262Harness package line by default.
- Routine Test262Harness updates have one metadata target,
  `Test262HarnessVersion`, with two direct owner projects to prove.
- Pure centralization changes that preserve the resolved package version can
  use targeted metadata scans plus diff hygiene as proof.
- Actual Test262Harness package updates still need package-compatibility proof
  for both owner surfaces, such as Test262 project restore and ProfileRunner
  build.
- Any intentional Test262Harness version split becomes visible as policy work
  rather than accidental dependency drift.

## Related

- Issue #2291 / PR #2299
- Issue #1972 / PR #1978
- `.claude/rules/dependency-maintenance.md`
- `Directory.Build.props`
- `tests/Asynkron.JsEngine.Tests.Test262/Asynkron.JsEngine.Tests.Test262.csproj`
- `tools/ProfileRunner/ProfileRunner.csproj`
