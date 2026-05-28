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

Issue #1742 reused this policy for
`built-ins/Function/prototype/toString/built-in-function-object.js`. The row was
classified as a focused Test262 harness timeout/resource-pressure case around a
large built-in function object inventory, not a `Function.prototype.toString`
native source display correctness failure. The delivery therefore added only an
exact 90 second timeout override plus helper regressions for both bare and
`test/`-prefixed path shapes.

Issue #1745 reused this policy for
`intl402/Locale/invalid-tag-throws.js`. The row was classified as a focused
Test262 harness timeout/resource-pressure case around invalid-language-tag
validation, not an `Intl.Locale` BCP-47 semantic failure. The delivery therefore
added only an exact 90 second timeout override plus helper regressions for both
bare and `test/`-prefixed path shapes.

Issue #2562 / PR #2567 reused this policy for
`built-ins/decodeURI/S15.1.3.1_A2.5_T1.js`. The URI decoder remained the right
owner surface to inspect, but the current focused Release rows passed under the
already accepted exact extended timeout. The delivery therefore kept runtime and
harness behavior unchanged and added the missing helper regression coverage for
both bare and `test/`-prefixed path shapes.

## Consequences

- Future Test262 harness fixes should not assume one canonical path shape when
  matching `Test262File.FileName`.
- Per-fixture timeouts remain exceptional compatibility policy, not a broad
  way to hide runtime performance problems.
- Review-bounce repairs should add direct harness-helper regression coverage
  for the missed path shape before returning to review.
- A slow fixture that exercises a broad built-in inventory may receive a narrow
  exact-path timeout only after focused evidence rules out a semantic runtime
  failure; do not widen neighboring Test262 method groups as a shortcut.
- This ADR is caused by issue #771 / PR #948 and complements
  `.claude/rules/test262-harness-policy.md`.
- Issue #1742 / PR #1767 extends the same decision for the exact
  `built-ins/Function/prototype/toString/built-in-function-object.js` fixture.
- Issue #1745 / PR #1769 extends the same decision for the exact
  `intl402/Locale/invalid-tag-throws.js` fixture.
- Issue #2562 / PR #2567 extends the same decision for the exact
  `built-ins/decodeURI/S15.1.3.1_A2.5_T1.js` fixture and records that missing
  helper regression coverage should be closed without reopening URI runtime
  semantics when the focused rows already pass.
