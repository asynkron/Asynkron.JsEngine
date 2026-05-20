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
- Issue #873 / PR #1228 reused the same test-only closeout path for
  `TypedArrayConstructors_internals_DefineOwnProperty`. The focused Test262
  proof was already green on current `origin/main`, so the delivery added
  internal regressions for integer-indexed `Object.defineProperty` value
  conversion: number typed arrays use `ToNumber`, BigInt typed arrays use
  `ToBigInt`, conversion overflow wraps through the target element semantics,
  and a `valueOf` that detaches the receiver buffer still makes
  `Reflect.defineProperty` return `true` without a visible write. This also
  confirms that nearby typed-array closeout branches should be merged by
  preserving independent test blocks rather than replacing sibling regression
  clusters.
- Issue #874 / PR #1233 reused the same green-closeout decision for
  `TypedArrayConstructors_internals_Set`. The focused Test262 method group
  passed 52/52 on current main, so the delivery added only regression coverage
  in `tests/Asynkron.JsEngine.Tests.Test262/RegressionTests.cs` proving
  integer-indexed `[[Set]]` value coercion still throws before out-of-range or
  detached-buffer no-op behavior can hide it, across both Number and BigInt
  typed-array paths. This complements the typed-array coercion rule in
  `.claude/rules/ecmascript-numeric-coercions.md`.
- Issue #876 repeated the green-closeout path for
  `TypedArray_prototype_at`. The 2026-05-17 testrunner summary listed only the
  strict and sloppy
  `built-ins/TypedArray/prototype/at/returns-undefined-for-holes-in-sparse-arrays.js`
  rows, but the build-stage proof on current main passed both the exact fixture
  filter (2/2) and the full `Name=TypedArray_prototype_at` method group (28/28).
  The correct delivery was no source change: sparse-array hole filling through
  typed-array construction and `.at()` reads was already fixed by later mainline
  work, so the stale batch report should not trigger another nearby
  TypedArray patch.
- Issue #877 reused the no-source-change form of this decision for
  `TypedArray_prototype_fill`. The issue came from a 2026-05-17 Test262 batch
  summary for `fill-values-conversion-operations.js`, but the build-stage
  focused proof on 2026-05-20 passed the whole issue-supplied method group
  66/66 on current `origin/main`. No runtime, harness, or regression-test
  patch was warranted because the exact current proof was green.
- Issue #1035 reused the no-source-change form for `Expressions_greaterThan`.
  Investigation found the right owner surface in binary-expression relational
  coercion (`JsOps.PerformComparisonOperation` / `ToPrimitive`), but the
  build-stage focused proof for the reported `greater-than` fixture cluster was
  already green on the current worktree. Owner-surface analysis remains useful
  for understanding the stale report, but a green exact fixture proof is not a
  reason to patch relational comparison runtime or expression bytecode dispatch.
