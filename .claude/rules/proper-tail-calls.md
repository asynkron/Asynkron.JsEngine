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
4. Do not mark a return expression as tail-position while an active `finally`
   frame still has to run. Tail-call stack reuse must not skip pending cleanup
   or change which abrupt completion wins.
5. Reset or reinitialize expression interpreter side state when reusing a
   trampoline frame, including optional-chain short-circuit flags and any
   stack-slot metadata.
6. Preserve the callable object's realm-sensitive errors. A revoked proxy called
   from tail position must throw from the proxy realm, not from the caller realm.
7. Prove this class with focused internal coverage before broad Test262 runs:
   call-depth stability, `try` / `catch` frame cleanup, `try` / `finally`
   ordering, strict member-call receiver rebinding, and relevant realm-sensitive
   proxy coverage.

## Why

Issue #1748 / PR #1796 fixed the proper-tail-call Test262 bucket after the
coverage was enabled. The delivery added a sync IR trampoline and a
same-function tail-restart path, then review found a strict member-call bug:
`first.f(1)` tail-calling `second.f(...)` restarted with new parameters but kept
the old strict `this` binding. The repair had to record the receiver and rebind
the existing function environment before jumping back to the plan entry point.

The same issue also exposed that tail-call work crosses expression-bytecode
side-state, `try` / `finally` completion ordering, and proxy realm identity.
Future changes need targeted semantic proof for those boundaries; a green broad
suite or a call-depth-only test is not enough.

Related ADR: `docs/adrs/0126-keep-proper-tail-calls-runtime-context-owned.md`.
