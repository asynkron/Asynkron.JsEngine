# ADR 0161: Keep Test262 profile packs direct-runner verifiable

## Status

Accepted

## Context

Issue #2051 asked for a reproducible Test262 profile pack for a current
slow/crash cluster so future triage would not depend on ad-hoc commands or
stale Test262 gap documents. PR #2058 added the focused manifest entry
`test262-regexp-property-punctuation` for the RegExp Unicode property-escape
punctuation rows reported in the issue context:

- `General_Category_-_Close_Punctuation.js` strict `true`
- `General_Category_-_Connector_Punctuation.js` strict `false`
- `General_Category_-_Connector_Punctuation.js` strict `true`

The delivery deliberately changed only `tools/profile-manifest.json`. It did
not broaden the existing `test262-regexp-property-escapes` profile, change
runner code, or fix the underlying runtime cluster.

The profile was accepted with fresh direct runner evidence:

```bash
rtk dotnet run --project tools/ProfileRunner/ProfileRunner.csproj -c Release -- test262-regexp-property-punctuation
```

The build-stage run completed the three cases with one warmup and one measured
iteration. Review reran the same direct command successfully. The usual wrapper
path,

```bash
rtk ./tools/profile test262-regexp-property-punctuation
```

failed before exercising the profile because the shell wrapper hit
`profiler_args[@]: unbound variable`. That failure was classified as wrapper
friction, not as evidence against the manifest profile.

## Decision

Keep focused Test262 profile packs direct-runner verifiable.

When a Test262 profile-pack slice only needs manifest coverage, the accepted
evidence path is a current Release `ProfileRunner` invocation against the named
profile. The manifest entry must preserve the exact representative fixture paths
and strict flags from the current issue context, and it must stay focused enough
to reproduce one cluster rather than becoming a broad unsorted dump.

If `tools/profile` fails before the underlying runner executes, use direct
`ProfileRunner` evidence to validate the profile and record the wrapper failure
as tooling friction. Do not expand a manifest-only Test262 profile slice into a
wrapper repair or runtime fix unless the issue explicitly asks for that work.

Future wrapper repairs should follow `.claude/rules/tooling-shell-wrappers.md`
and prove empty optional Bash array handling under `set -u`.

## Consequences

- Test262 triage profiles remain reproducible through the runner that owns
  manifest parsing, even when a higher-level profiling wrapper is broken.
- Profile-pack deliveries can stay narrow: manifest entry plus fresh direct
  runner output, with runtime repairs split into separate issues when needed.
- Wrapper failures stay visible and traceable without blocking acceptance of a
  valid manifest profile.
- Future agents should not treat historical Test262 reports, broad generated
  method groups, or wrapper preflight failures as substitutes for current
  focused runner evidence.

## Related

- Issue #2051 / PR #2058
- `.claude/rules/tooling-shell-wrappers.md`
- `.claude/rules/test262-triage-proof.md`
- `tools/profile-manifest.json`
- `tools/ProfileRunner/ProfileRunner.csproj`
