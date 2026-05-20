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
