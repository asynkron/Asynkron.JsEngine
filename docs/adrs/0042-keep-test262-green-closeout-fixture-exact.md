# ADR 0042: Keep Test262 green closeout fixture-exact

## Status

Accepted

## Context

Issue #826 was created from the 2026-05-17 Test262 testrunner summary for
`Statements_class_elements`. The summary listed 14 failed rows for private
field, method, getter, and setter access from ordinary inner functions inside
class elements.

By the build-stage pass on 2026-05-20, current main already passed every
listed private inner-function fixture when each reported file pattern was run
directly. The issue-suggested method-group filter
`Name=Statements_class_elements` and an FQN method-group variant both crossed
the local 60 second inactivity guard, because they selected the broader
generated method group rather than only the 14 reported rows.

The runtime private-name semantics were already governed by ADR 0010 and
`.claude/rules/ecmascript-private-names.md`, so no source or harness repair was
warranted for this issue.

## Decision

Treat old Test262 batch summaries as stale until reproved on the current
worktree. When the issue lists exact Test262 fixture rows and the generated
method-group proof is noisy, too broad, or hits the inactivity guard, close out
from exact fixture-row proof instead of treating the group hang as a current
implementation failure.

Use method-group filters as the first convenient proof when they are bounded.
If they over-select or time out, run the exact listed fixture files or file
patterns from the issue body and report the method-group behavior as proof
friction, not as a failing runtime signal.

Do not add runtime or harness changes after exact listed rows are green unless
there is a separate current repro or a focused regression-test gap worth
pinning.

## Consequences

- Build agents may finish a Test262 batch-summary issue with no code changes
  when all exact listed rows pass on current main.
- A method-group hang is not enough evidence to change implementation or
  harness behavior if exact issue rows are green.
- Future Test262 triage should preserve the distinction between generated
  method-group selection behavior and the specific fixtures that caused the
  issue.
- This ADR is caused by issue #826 and complements
  `.claude/rules/test262-triage-proof.md`.
- Issue #869 / PR #1223 reused this closeout path for
  `TypedArrayConstructors_ctors_lengthArg`. The focused proof was 24/24 green
  on current `origin/main`, so the delivery added only 12 focused `Uint8Array`
  length-arg regressions in `tests/Asynkron.JsEngine.Tests/TypedArrayTests.cs`
  pinning the `ToIndex` table (NaN/-0/undefined/fractional → 0,
  -1/Infinity/-Infinity/2^53 → `RangeError`, Symbol/BigInt → `TypeError`).
  The companion PR #1224 covered the analogous buffer-arg `ToIndex` shape.
  Typed-array constructor coercion slices stay test-only when the focused
  Test262 group is already green; future agents should extend the existing
  `TypedArrayTests.cs` clusters instead of opening a new file.
