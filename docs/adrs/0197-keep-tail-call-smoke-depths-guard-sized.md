# ADR 0197: Keep tail-call smoke depths guard-sized

## Status

Accepted

## Context

Issue #2199 / PR #2200 repaired a red `main` health run where two internal
`TailCallTests` used a recursion depth of `100000` only to prove that
same-function tail restarts did not grow call depth.

The failing evidence was timeout-shaped, not semantic: the raw repro showed
repeated `getF` tail-call debug logger output, and the narrow
`StrictSameFunctionTailCall_IndirectCalleeExpressionDoesNotGrowCallDepth` test
was still running after 30 seconds. The tests were already guarded by xUnit
timeouts, and the engine's call-depth guard is `1000`, so a much smaller value
could still prove that ordinary recursion would cross the guard.

The accepted delivery reduced the two depth probes from `100000` to `1500`.
The focused proof command then passed 3/3 tests in 7 seconds, and the
orchestrator `make quality` gate passed 4253 tests with 2 skipped.

## Decision

Internal tail-call smoke tests that only need to prove stack-depth stability
should use a guard-sized depth: high enough to exceed the engine's current
`MaxCallDepth` guard, but low enough to stay inside the normal internal-suite
time and log-output budget.

For the current guard of `1000`, use a value in the same range as `1500` unless
a test documents why it needs a larger count. Do not lower a depth probe below
the guard just to make a test pass, because that no longer proves tail-call
restart behavior.

Reserve Test262-scale counts such as `100000` for cases where the exact
upstream workload or a performance profile is the behavior under test. If a
large count is necessary in the internal suite, prove the exact focused command
under the repository timeout and avoid debug logger output that can flood the
main health gate.

## Consequences

- `TailCallTests` remain a fast, durable proof surface for proper-tail-call
  stack behavior.
- Main health runs are less likely to fail from log flooding or excessive
  smoke-test iteration counts when the semantic condition is simply "greater
  than `MaxCallDepth`."
- Future proper-tail-call fixes should preserve semantic coverage by comparing
  the chosen depth to the runtime guard, not by copying an arbitrary large
  Test262 iteration count into internal tests.
- If `MaxCallDepth` changes, guard-sized tail-call smoke depths should be
  revisited with the same rule: exceed the guard with a bounded margin.

## Related

- `.claude/rules/proper-tail-calls.md`
- `docs/adrs/0126-keep-proper-tail-calls-runtime-context-owned.md`
- `docs/adrs/0162-keep-tail-restarts-activation-capture-safe-after-arguments.md`
- `docs/adrs/0001-preserve-quality-build-then-no-build-test-contract.md`
