# ADR 0045: Keep normal finally completion preservation origin-aware

## Status

Accepted

## Context

Issue #828 / PR #1127 fixed the Test262 `Statements_try` failures for
`language/statements/try/completion-values.js`. The failing surface was the IR
runner's try/catch/finally completion bookkeeping, not Test262 harness policy.

The first repair made `finally` execute in its own completion window so an
abrupt `break` or `continue` raised inside `finally` could replace the pending
try/catch completion value. Review then found the paired edge: a `break` or
`continue` that originated in the try body and only passed through a normal
`finally` block must preserve the try body's completion value. A plain
`finally { 42; }` is normal completion and its value is discarded for this
purpose.

The delivery added `PendingCompletion.OriginatedInFinally` so `EndFinally` can
distinguish those two otherwise similar pending abrupt completions. It also
added focused internal regression coverage for try-body `break` and `continue`
through a normal `finally { 42; }`, and proved the fix with the focused
`Name=Statements_try` Test262 method group.

## Decision

Keep IR try/finally completion state origin-aware.

When a `finally` block completes normally, restore the saved try/catch
completion value for pending abrupt completions that originated before the
finally block. Do not let the final expression value from a normal `finally`
overwrite the saved try/catch value.

When a `finally` block itself raises an abrupt completion, finalize and use the
finally block's completion value instead. The origin marker is the contract that
prevents these cases from collapsing into one branch.

Do not repair future try/finally completion issues by adding a runner-time AST
fallback or by treating all pending `break`/`continue` completions the same.
The IR runner must preserve the ECMAScript split between normal finally
completion and abrupt finally completion directly in its completion state.

## Consequences

- Future changes to `PendingCompletion`, `EndFinally`, or try/catch/finally IR
  handlers must preserve the origin distinction for pending abrupt completions.
- Regression coverage for this class needs both sides: abrupt completion raised
  inside `finally` wins, while try-body `break`/`continue` through a normal
  `finally` preserves the try/catch completion value.
- The focused proof for this class is the internal
  `CatchCompletionValueReplicationTest` coverage plus the Test262
  `Name=Statements_try` method group. Keep the proof narrow unless a separate
  investigation asks for a wider Test262 run.
- This ADR is caused by issue #828 / PR #1127 and complements
  `.claude/rules/ir-control-flow-cleanup.md`.
