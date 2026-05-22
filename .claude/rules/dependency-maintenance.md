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
- Record the baseline and final dependency signals in the issue context:
  `outdated`, `audit`, installed versions, and the project build or demo proof
  that owns the dependency.

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

Issue #1571 / PR #1576 applied the same bounded-slice rule to .NET test
infrastructure. The main test project moved `Microsoft.NET.Test.Sdk` from
`18.0.1` to `18.5.1`, while `coverlet.collector 6.0.4 -> 10.0.1` stayed
deferred because it was a separate major collector migration. Future .NET
dependency sweeps should keep compatible test SDK updates scoped to their
owning test project and leave unrelated major test tooling migrations for a
dedicated issue.
