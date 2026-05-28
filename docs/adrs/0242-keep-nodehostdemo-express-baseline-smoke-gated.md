# ADR 0242: Keep NodeHostDemo Express baseline smoke-gated

## Status

Accepted

## Context

NodeHostDemo includes a real Express package script to prove the C# host can run
an unchanged npm framework through the repo's Node-shaped CommonJS surface. That
demo is intentionally isolated under `examples/NodeHostDemo`, but it is still a
behavioral compatibility signal because Express exercises routing, middleware,
request/response host methods, package resolution, and host-backed modules.

Earlier recurring dependency-maintenance runs deliberately deferred Express 5:
Express 4 was current on the `latest-4` dist-tag, while `latest` reported
`5.2.1`. Those runs recorded the drift as evidence-only because moving a real
framework demo across a major version could change route matching or middleware
semantics and therefore needed a dedicated compatibility pass.

Issue #2445 selected that dedicated bounded slice. PR #2457 changed only the
direct NodeHostDemo Express dependency from `^4.22.2` to `^5.2.1`, refreshed
`package-lock.json`, and left `polka` unchanged at its stable `0.5.2` line. The
build-stage evidence recorded the before/after dependency signal:

```text
baseline: manifest ^4.22.2, lock 4.22.2, npm latest 5.2.1
final:    manifest ^5.2.1, lock 5.2.1, npm latest 5.2.1
```

The accepted proof also hit the live Express demo endpoints:

```text
GET /api/status
GET /api/hello/agent?from=smoke
```

## Decision

Treat Express 5.2.1 as the maintained NodeHostDemo Express package baseline.

Future dependency-maintenance runs should keep the NodeHostDemo Express
manifest, lockfile, and README dependency note aligned. Express changes should
not be reduced to a manifest-only or lockfile-only edit; the observable owner is
the real Express demo, so proof must include behavior-specific smoke coverage.
At minimum, after changing Express, run `/api/status` and one parameterized
route such as `/api/hello/agent?from=smoke`.

Continue using npm dist-tag metadata to distinguish compatible safe-line updates
from major or prerelease work. A future Express major may be selected by a
recurring dependency run only when the investigation scopes that package as the
single compatibility slice and records the same manifest/lock/demo evidence.

This ADR does not claim engine-wide Node.js compatibility. It records the
maintained baseline and proof boundary for the NodeHostDemo real Express package
script only.

## Consequences

- Recurring dependency sweeps should no longer refresh an Express 4 deferral
  note for NodeHostDemo; Express 5 is now the committed baseline.
- Express transitive lockfile churn is acceptable when it is caused by the
  selected direct dependency update and the root manifest/lock versions agree.
- Future Express package updates have a clear minimum proof pack: dependency
  metadata, lockfile consistency, and live demo smoke through the route and
  middleware surface.
- Polka remains separately owned by its stable npm tag; prerelease Polka 1.x
  work still needs an explicit compatibility issue.

## Related

- Issue #2445 / PR #2457
- `.claude/rules/dependency-maintenance.md`
- `examples/NodeHostDemo/package.json`
- `examples/NodeHostDemo/package-lock.json`
- `examples/NodeHostDemo/README.md`
