# Proper Tail Calls

When changing proper-tail-call support, sync IR trampolines, or same-function
tail restart behavior, keep runtime context ownership explicit.

## Rules

1. Do not repair proper-tail-call failures by routing return-position calls
   through legacy AST evaluation. The IR return handler and expression-bytecode
   call boundary own this behavior.
2. Treat trampoline eligibility as conservative. If a function, statement, or
   expression shape cannot preserve activation slots, argument binding, receiver
   binding, expression-stack side state, and cleanup ordering, use the ordinary
   call path.
3. Same-function tail restarts must capture evaluated arguments and the call
   receiver, then apply the restart only after scheduled cleanup has been
   honored. For strict functions, rebind `this` in the existing function
   environment and slot storage before resetting parameter slots.
4. In expression bytecode, tail position is not limited to the final op. A call
   in a conditional or other branch is tail-position eligible when all
   remaining ops are unconditional jumps to expression-program end.
5. Do not apply a tail restart until active `finally` cleanup has run. If a
   return expression requested a same-function restart before `finally`, carry
   that fact on the pending return completion and apply it after `EndFinally`
   only if the return survives. If `finally` replaces that completion without a
   restart, clear the stale restart request.
6. Reset or reinitialize expression interpreter side state when reusing a
   trampoline frame, including optional-chain short-circuit flags and any
   stack-slot metadata.
7. Preserve the callable object's operation-selected realm-sensitive errors. A
   revoked proxy called from tail position must throw from the realm selected
   by that proxy operation; do not replace it with a generic caller, callee, or
   proxy-creation realm. See `.claude/rules/ecmascript-proxy-realm-errors.md`
   for the apply/construct null-handler rule.
8. Prove this class with focused internal coverage before broad Test262 runs:
   call-depth stability, `try` / `catch` frame cleanup, `try` / `finally`
   ordering and completion override, conditional-expression branch calls,
   strict member-call receiver rebinding, and relevant realm-sensitive proxy
   coverage.
9. Keep sync IR trampoline eligibility aligned with executable trampoline
   shapes. For the current trampoline, expression-program calls are eligible
   only when they are the final operation in a return-purpose expression
   program; branch-condition calls and non-final operand calls must use
   ordinary invocation unless the executor is widened first.

## Why

Issue #1748 / PR #1796 fixed the proper-tail-call Test262 bucket after the
coverage was enabled. The delivery added a sync IR trampoline and a
same-function tail-restart path, then review found a strict member-call bug:
`first.f(1)` tail-calling `second.f(...)` restarted with new parameters but kept
the old strict `this` binding. The repair had to record the receiver and rebind
the existing function environment before jumping back to the plan entry point.

The same issue also exposed that tail-call work crosses expression-bytecode
side-state, `try` / `finally` completion ordering, and proxy realm identity.
Issue #1864 / PR #1890 later refined the proxy lesson: revoked proxy
`[[Call]]` and `[[Construct]]` null-handler errors use the current execution
realm when present. Future changes need targeted semantic proof for those
boundaries; a green broad suite or a call-depth-only test is not enough.

Issue #1865 / PR #1898 refined the same proper-tail-call boundary again:
conditional-expression branches can end through unconditional jumps rather than
as the final bytecode op, and return completions can carry a pending restart
through scheduled `finally`. The rule exists so future fixes preserve
completion replacement semantics instead of treating a stale restart flag as a
surviving return.

Issue `autrun-dirtf01zpmv4-17122917c9` / PR #1909 exposed the performance
failure mode on the same surface. The `fib` profile spent time repeatedly
entering `SyncIrCallTrampoline` for non-tail recursive operands such as
`fib(n - 1) + fib(n - 2)`, even though the trampoline executor can only complete
final return-expression calls. The fix kept tail-recursive eligibility intact
but rejected branch expressions and non-final return-expression calls before
frame setup. The durable lesson is that semantic tail-position rules and
executor eligibility are related but not interchangeable.

Related ADRs:
- `docs/adrs/0126-keep-proper-tail-calls-runtime-context-owned.md`
- `docs/adrs/0139-keep-tail-restarts-through-expression-branches-and-finally-completions.md`
- `docs/adrs/0140-keep-sync-ir-trampoline-eligibility-executor-exact.md`
