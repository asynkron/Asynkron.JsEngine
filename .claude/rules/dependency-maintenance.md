# Dependency Maintenance

For recurring dependency-maintenance issues, keep the delivery slice compatible
unless the issue explicitly asks for a major migration.

## Compatible Update Rule

- Prefer patch/minor updates that stay inside the existing major version and
  keep the changed manifest/lockfile pair synchronized.
- For .NET package references that appear in multiple project files, align the
  compatible version across every owning project in the same scoped slice
  unless there is a documented reason for one project to stay pinned.
- When a .NET dependency sweep finds the same package already pinned to the
  same literal version in several project files, prefer centralizing that
  version in `Directory.Build.props` as a named MSBuild property if the slice
  can stay small and the resolved package version does not change. Do not turn
  that cleanup into full Central Package Management or a broad package
  modernization inside a routine recurring child.
- For Jint comparison tooling, treat `tools/ProfileRunner/ProfileRunner.csproj`
  and the benchmark project at
  `benchmarks/Asynkron.JsEngine.Benchmarks/Asynkron.JsEngine.Benchmarks.csproj`
  as one aligned owner set. Keep both `Jint` package references on the shared
  `$(JintVersion)` property in `Directory.Build.props`. Future Jint updates
  should edit that property once and prove both owner projects; do not
  reintroduce project-local Jint literals unless a dedicated issue documents why
  profiling and BenchmarkDotNet comparisons need separate package baselines.
- Do not fold major-version migrations into a routine dependency sweep just
  because `latest` reports one. Major upgrades need their own issue, migration
  notes, and behavior-specific proof.
- A previously deferred .NET test-tooling major may be taken only as its own
  bounded compatibility slice when investigation proves a single owner project.
  Preserve test-only package metadata such as `PrivateAssets` and
  `IncludeAssets`, avoid shared props or runtime manifests unless multiple
  owners exist, and prove the result with the canonical internal quality gate
  plus a current package-state signal.
- A recurring dependency run may take a previously deferred npm major only when
  investigation deliberately scopes that one package as the bounded
  compatibility slice, records the old safe-line/deferred-major signal, keeps
  the manifest, lockfile, and owner docs aligned, and runs behavior-specific
  smoke or build proof for the owning demo/project. Treat that run as the
  dedicated compatibility pass, not as a broad latest sweep.
- For npm dependencies, inspect the package dist-tags before deciding that
  `latest` is an actionable update. If the installed major is current on a
  maintenance tag such as `latest-4`, treat a newer `latest` major as deferred
  compatibility work; if a package only advertises a prerelease channel such as
  `next`, do not move to that channel in a routine sweep without an explicit
  pre-release compatibility issue.
- If a recurring npm sweep re-proves the same safe-line signal and there is no
  compatible manifest or lockfile update to apply, keep the delivery
  evidence-only: refresh the dated dependency note or issue evidence, and leave
  package files unchanged. If the existing dated note is already current for
  the same signal, do not manufacture a repository diff just to make the run
  non-empty.
- When `npm outdated --json` reports a `wanted` version, compare the committed
  `package.json` range and `package-lock.json` package version before treating
  it as committed dependency drift. If both committed files already resolve the
  wanted safe line, record the command as local install-state evidence instead
  of rewriting the manifest or lockfile.
- Record the baseline and final dependency signals in the issue context:
  `outdated`, `audit`, installed versions, and the project build or demo proof
  that owns the dependency.
- For the NodeHostDemo real Express package demo, keep
  `examples/NodeHostDemo/package.json`,
  `examples/NodeHostDemo/package-lock.json`, and the README dependency note in
  agreement about the maintained Express baseline. After issue #2445 / PR
  #2457, that baseline is Express `5.2.1`. Future Express dependency changes
  should smoke at least `/api/status` and a parameterized route such as
  `/api/hello/agent?from=smoke` because route matching and middleware behavior
  are the compatibility surface.
- Before committing an evidence-only dependency sweep, verify `git status`
  against the intended no-diff outcome. Do not commit transient command
  artifacts from metadata gathering, such as curl cookie jars or numeric
  scratch files, as a substitute for a real dependency change.
- Keep dependency-maintenance deliveries out of unrelated proof packs, runtime
  tests, or quality-gate repairs. If verification exposes a flaky or brittle
  non-dependency test, record or route it separately unless the dependency
  change itself caused the failure.
- For Test262 project dependency sweeps, separate package-compatibility proof
  from host runtime inventory. If a narrow Test262 discovery/build signal is
  blocked by `test262harness.console` requiring a locally missing
  `Microsoft.NETCore.App 8.0.0`, keep the slice valid only when the same
  blocker is captured before and after the dependency edit, restore reaches the
  harness generation step, and the canonical internal quality gate still owns
  final source verification. Do not turn that environment gap into a Test262
  harness migration or unrelated runtime-install task inside the dependency
  slice.
- When updating `Test262Harness`, treat
  `tests/Asynkron.JsEngine.Tests.Test262/Asynkron.JsEngine.Tests.Test262.csproj`
  and `tools/ProfileRunner/ProfileRunner.csproj` as the aligned owner set.
  Keep both references on the shared `$(Test262HarnessVersion)` property in
  `Directory.Build.props`. Update that property and both owner projects in the
  same slice unless a current investigation documents a reason to split them.
  For a pure centralization that does not change the resolved package version,
  a targeted metadata scan plus `git diff --check` is enough proof. For an
  actual package update, prove compatibility with at least a Test262 project
  restore plus a ProfileRunner build instead of relying on a literal `rg`
  version check alone.

## Why

Issue #1141 / PR #1149 updated the isolated NodeHostDemo `express` dependency
from `4.22.1` to `4.22.2`, synchronized `package-lock.json`, and proved
`npm install`, `npm audit`, installed versions, `npm outdated`, and
`dotnet build examples/NodeHostDemo/NodeHostDemo.csproj`.

The same sweep deliberately left Express `5.2.1` alone because that was a major
runtime-demo migration, not a bounded dependency-maintenance patch.

Issue #1299 / PR #1305 updated `Microsoft.Extensions.Logging.Abstractions` from
`10.0.1` to `10.0.8` in the runtime project, main test project, and test helper
project together. Keeping the repeated .NET package pin aligned avoided a
partial dependency state where only one project saw the compatible patch update
while sibling projects continued to carry the older package.

Issue #1923 / PR #1928 followed up on that same repeated
`Microsoft.Extensions.Logging.Abstractions` surface after all three project
files already carried the same `10.0.8` literal. The safe maintenance slice was
to move the version literal into `Directory.Build.props` as
`MicrosoftExtensionsLoggingAbstractionsVersion` and reference that property from
the runtime, main test, and test helper projects, without changing the resolved
package version or introducing full Central Package Management.

Issue #1532 / PR #1536 documented the same NodeHostDemo drift after npm
metadata showed `express@5.2.1` on `latest`, `express@4.22.2` on `latest-4`,
and `polka@1.0.0-next.28` only on `next`. The useful maintenance outcome was
recording that Express 5 and Polka 1.x need dedicated compatibility passes,
while `package.json` and `package-lock.json` stayed unchanged because there was
no stable compatible update to apply.

Issue #1649 / PR #1653 repeated the NodeHostDemo npm drift sweep the next day
and found the same actionable state: Express 4 was still current on
`latest-4`, Express 5 still required a dedicated migration, and Polka 1.x still
lived on `next`. The useful recurring-maintenance delivery was refreshing the
dated README dependency signal only, not manufacturing a package update or
pulling a major/pre-release line into the routine sweep.

Issue #1716 / PR #1720 repeated the same NodeHostDemo dependency-maintenance
slice on the same 2026-05-24 signal already recorded in the README:
`express@5.2.1` on `latest`, Express 4 current at `4.22.2`, and
`polka@1.0.0-next.28` only on `next`. Because the durable README note was
already current and there was no compatible package or lockfile update, the
correct delivery was issue-evidence-only with no repository diff. The local
delivery branch still showed why the status check matters: the only changed
file was a generated curl cookie jar named `200`, which should not become a
dependency-maintenance artifact.

Issue #1571 / PR #1576 applied the same bounded-slice rule to .NET test
infrastructure. The main test project moved `Microsoft.NET.Test.Sdk` from
`18.0.1` to `18.5.1`, while `coverlet.collector 6.0.4 -> 10.0.1` stayed
deferred because it was a separate major collector migration. Future .NET
dependency sweeps should keep compatible test SDK updates scoped to their
owning test project and leave unrelated major test tooling migrations for a
dedicated issue.

Issue #1584 / PR #1587 completed that compatible Test SDK alignment for the
Test262 project after the main internal tests already used `18.5.1`. Future
test-infrastructure sweeps should check sibling test project files for the same
package before declaring a dependency slice complete, while still keeping the
slice to the compatible package and avoiding unrelated Test262 or collector
migrations.

Issue #2517 / PR #2520 completed the deferred `coverlet.collector` major as its
own bounded .NET test-tooling compatibility pass. The package had exactly one
owner in `tests/Asynkron.JsEngine.Tests/Asynkron.JsEngine.Tests.csproj`, so the
accepted slice was the single version change `6.0.4 -> 10.0.1`, preserving
`PrivateAssets` and `IncludeAssets` instead of centralizing or widening into
shared props. The proof boundary was the canonical `make quality` gate plus
`dotnet list tests/Asynkron.JsEngine.Tests package --outdated`, which reported
no remaining updates for the changed project.

Issue #1755 / PR #1763 refreshed only the NodeHostDemo README dependency drift
signal for 2026-05-25: Express 4 remained current on `latest-4`, Express 5
remained a dedicated migration, and Polka 1.x remained on `next`. Review had to
remove an unrelated `ActivationSemanticsProofPackTests` weakening from the
delivery branch before the dependency slice was accepted. Future dependency
sweeps should keep any activation proof-pack or other unrelated quality repair
on its owning issue, even when it is discovered while proving the maintenance
run.

Issue #1815 / PR #1821 updated only the Test262 project's compatible NUnit line:
`NUnit` `4.4.0 -> 4.6.1` and `NUnit3TestAdapter` `6.1.0 -> 6.2.0`. The narrow
Test262 discovery signal restored packages and then failed in
`dotnet test262 generate` because the local user dotnet inventory lacked the
exact `Microsoft.NETCore.App 8.0.0` runtime required by
`test262harness.console`; the same blocker existed before and after the edit.
The accepted proof boundary was to record that unchanged environment blocker,
keep the dependency diff to the two PackageReference updates, and rely on the
canonical internal quality gate for source-level verification instead of
folding a Test262 harness/runtime migration into the routine dependency sweep.

Issue #1880 / PR #1899 refreshed the NodeHostDemo dependency drift note for the
2026-05-25 recurrence after `npm --prefix examples/NodeHostDemo outdated --json`
reported `express` with `wanted` `4.22.2` and `latest` `5.2.1`. The committed
`package.json` already selected `^4.22.2` and `package-lock.json` already locked
`express` to `4.22.2`, so the actionable lesson was to document the signal as
local install-state evidence while continuing to defer Express 5 and Polka
`1.0.0-next.28` to dedicated compatibility passes.

Issue #2445 / PR #2457 performed the deferred NodeHostDemo Express 5
compatibility pass. The slice changed only the direct Express dependency from
`^4.22.2` to `^5.2.1`, refreshed `package-lock.json`, left `polka` unchanged,
and proved the major update with `npm audit`, lockfile consistency, and smoke
checks for `/api/status` plus `/api/hello/agent?from=smoke`. Future recurring
dependency runs should no longer treat Express 5 as merely deferred for this
demo; they should keep the package files and README baseline note aligned and
apply the same behavior-specific smoke boundary to later Express updates.
Related ADR: `docs/adrs/0242-keep-nodehostdemo-express-baseline-smoke-gated.md`.

Issue #1972 / PR #1978 updated `Test262Harness` from `1.0.3` to `1.0.6` in both
the generated Test262 test project and `ProfileRunner`. The package feeds
Test262 fixture discovery/generation as well as profiling scenarios, so future
maintenance sweeps should keep those two project references aligned and record
real restore/build proof for both affected surfaces: Test262 project restore and
ProfileRunner Release build were the accepted narrow compatibility signals for
this patch update.

Issue #2291 / PR #2299 found that the same `Test262Harness` `1.0.6` line from
PR #1978 was still repeated as a project-local literal in both owner projects.
The safe dependency-maintenance slice did not change the resolved package
version; it moved the version to `Directory.Build.props` as
`Test262HarnessVersion` and kept both owner projects on that property. Future
Test262Harness work should preserve that shared property unless a dedicated
issue documents an intentional split. The accepted proof was a targeted
metadata scan plus `git diff --check`, and the review bounce was closed with an
evidence-only re-entry that added the required `Sibling check` and `Scope note`.
Related ADR: `docs/adrs/0219-keep-test262harness-version-centralized.md`.

Issue #2099 / PR #2107 found `Jint` `4.9.2` hard-coded in both the
ProfileRunner comparison tool and the BenchmarkDotNet comparison project. The
safe maintenance slice did not change the resolved package version; it moved
the version to `Directory.Build.props` as `JintVersion` and kept both owner
projects on `$(JintVersion)`. Future Jint comparison dependency work should
preserve that shared property unless a dedicated issue documents an intentional
split. The accepted narrow proof was `git diff --check` plus Release builds for
ProfileRunner and the benchmark project. Related ADR:
`docs/adrs/0174-keep-jint-comparison-version-centralized.md`.
