# Dependency Maintenance

For recurring dependency-maintenance issues, keep the delivery slice compatible
unless the issue explicitly asks for a major migration.

## Compatible Update Rule

- Prefer patch/minor updates that stay inside the existing major version and
  keep the changed manifest/lockfile pair synchronized.
- For .NET package references that appear in multiple project files, align the
  compatible version across every owning project in the same scoped slice
  unless there is a documented reason for one project to stay pinned.
- Do not fold major-version migrations into a routine dependency sweep just
  because `latest` reports one. Major upgrades need their own issue, migration
  notes, and behavior-specific proof.
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
- Record the baseline and final dependency signals in the issue context:
  `outdated`, `audit`, installed versions, and the project build or demo proof
  that owns the dependency.
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
