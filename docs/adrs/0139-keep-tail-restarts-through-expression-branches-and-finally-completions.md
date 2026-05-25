# ADR 0139: Keep tail restarts through expression branches and finally completions

## Status

Accepted

## Context

Issue #1865 / PR #1898 fixed the residual proper-tail-call rows that still
crashed after the broader proper-tail-call runtime trampoline work. The failing
rows were not a request to reopen the whole TCO implementation. They narrowed
to branch-local expression-program calls such as conditional expressions and to
returns that interacted with scheduled `finally` completion.

The old expression-bytecode tail-position check only treated the final
operation in an `ExpressionProgram` as tail position. That missed calls in a
conditional branch when the branch returned by jumping to program end. Those
calls were semantically still the returned expression's final value, but the
program counter had to cross one or more unconditional jumps before exit.

The old `finally` handling also treated a return carrying a same-function tail
restart like an ordinary return completion. A restart requested before entering
`finally` could be lost, while a `return` inside `finally` could leave stale
restart state behind and incorrectly resume the older completion. The fix had
to preserve ECMAScript completion replacement order before applying stack
reuse.

The broader exact issue filter also contained unrelated residuals:
non-eval `eval`-identifier rows still need wider dynamic/global/with activation
slot or trampoline support, and
`built-ins/Proxy/revocable/tco-fn-realm.js` was a proxy realm-constructor
mismatch covered by the revoked-proxy realm work. This ADR only records the
conditional and `finally` restart lesson.

## Decision

Keep same-function tail restarts as an explicit runtime completion property
that can flow through expression-bytecode branch exits and scheduled `finally`
frames.

For expression-bytecode calls, a call is in tail position when it is the final
operation or when every remaining operation is an unconditional jump that lands
at the program end. Do not require conditional-expression branches to be
rewritten into another statement shape just to expose tail position.

For `try` / `finally`, a tail restart requested by the returned expression is
part of the pending return completion. The runner may carry that pending
restart through `finally` and apply it only after `EndFinally` has restored the
spec completion order. If `finally` produces a new completion that does not
carry that restart, clear the stale restart request because the `finally`
completion overrides the earlier return.

Track restart freshness with runner-owned state instead of inferring it from
the returned value. The important observable boundary is whether evaluating the
current return expression requested a new restart, not whether a previous
restart flag happens to still be set.

## Consequences

- Conditional-expression tail calls can reuse the same function frame when the
  selected branch only jumps to expression-program end.
- Tail-call stack reuse remains subordinate to `finally` ordering: cleanup runs
  first, `finally` may override the pending return, and only the surviving
  return completion may restart.
- Future proper-tail-call fixes should prove both call-depth stability and
  completion replacement. A call-depth-only test can miss stale restart state
  after `finally` overrides a return.
- Broader proper-tail-call residuals should stay split by owner. Dynamic/global
  `eval` binding rows and proxy realm-constructor rows are not evidence that
  branch-tail or `finally` restart handling should be widened.
- Focused proof should start with `TailCallTests` plus the exact Test262 rows
  for `language/expressions/conditional/tco-cond.js`,
  `language/statements/try/tco-finally.js`, and
  `language/statements/try/tco-catch-finally.js`.

## Related

- `.claude/rules/proper-tail-calls.md`
- `.claude/rules/ir-control-flow-cleanup.md`
- `.claude/rules/expression-bytecode-call-targets.md`
- `docs/adrs/0126-keep-proper-tail-calls-runtime-context-owned.md`
