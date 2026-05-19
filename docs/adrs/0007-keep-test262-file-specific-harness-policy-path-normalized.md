# ADR 0007: Keep Test262 file-specific harness policy path-normalized

## Status

Accepted

## Context

Issue #771 fixed the Test262 `DecodeURIComponent` crash for
`built-ins/decodeURIComponent/S15.1.3.2_A2.5_T1.js`. The underlying URI
decoder behavior was already covered by ADR 0006, but this delivery exposed a
separate harness concern: the four-byte `decodeURIComponent` fixture is large
enough to need a narrow extended execution timeout.

The first build-stage repair matched the bare Test262 path only. Review caught
that `Test262File.FileName` can include the optional upstream `test/` root
prefix, so the timeout override would miss the same fixture when the harness
reported `test/built-ins/decodeURIComponent/S15.1.3.2_A2.5_T1.js`.

## Decision

Keep file-specific Test262 harness policy narrow, explicit, and path-normalized.

When a Test262 helper applies behavior by fixture path, it must:

- normalize the optional leading `test/` root before comparing the path;
- keep the override scoped to the exact known fixture instead of broad method
  groups or directory prefixes;
- add regression coverage for both bare and `test/`-prefixed path shapes;
- keep ordinary fixtures covered by the default behavior.

For issue #771, the only fixture-specific policy is a 90 second execution
timeout for `built-ins/decodeURIComponent/S15.1.3.2_A2.5_T1.js`; ordinary
Test262 fixtures keep the default 30 second timeout.

## Consequences

- Future Test262 harness fixes should not assume one canonical path shape when
  matching `Test262File.FileName`.
- Per-fixture timeouts remain exceptional compatibility policy, not a broad
  way to hide runtime performance problems.
- Review-bounce repairs should add direct harness-helper regression coverage
  for the missed path shape before returning to review.
- This ADR is caused by issue #771 / PR #948 and complements
  `.claude/rules/test262-harness-policy.md`.
