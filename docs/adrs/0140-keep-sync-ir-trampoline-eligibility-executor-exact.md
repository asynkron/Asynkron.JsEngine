# ADR 0140: Keep sync IR trampoline eligibility executor-exact

## Status

Accepted

## Context

Issue `autrun-dirtf01zpmv4-17122917c9` / PR #1909 selected `fib` from the
optimizer benchmark table because it was the largest current Asynkron-vs-Jint
loss in that run:

```text
fib  asynkron_ms=7394  jint_ms=866  Jint 8.54x faster
```

The focused CPU profile,
`rtk ./tools/profile fib --cpu --calltree-depth 40 --calltree-width 40`,
showed ordinary recursive invocation as the dominant owner, but also showed
repeated failed sync IR trampoline setup under non-tail recursive calls:

```text
TypedAstEvaluator.SyncFunctionInvoker.SyncIrCallTrampoline.TryInvoke
TypedAstEvaluator.SyncFunctionInvoker.SyncIrCallTrampoline.PushFrame
```

`SyncIrCallTrampoline` can execute same-function calls only when the call is
the final operation of a return expression program. Its eligibility check was
broader than that executor contract: return expressions such as
`fib(n - 1) + fib(n - 2)` contain recursive calls, but those calls are operands
of a later binary operation, not the returned final call. The trampoline
therefore initialized frame storage, discovered that it could not complete the
shape, and fell back to ordinary invocation on every recursive call.

## Decision

Keep sync IR trampoline eligibility exactly aligned with the executor shape it
can complete.

For the current trampoline, expression-program calls are eligible only when
they occur in a return-purpose expression and the call is the final operation
in that expression program. Branch-condition programs and non-final calls in
return expressions must use ordinary invocation.

This does not change the proper-tail-call contract from ADR 0126 or the
branch-tail restart contract from ADR 0139. Those decisions describe when a
call is semantically in tail position for stack reuse. This ADR describes the
separate performance guard: a speculative trampoline entry must not be broader
than the implementation can execute without bailing out after per-call setup.

Future trampoline widening should first extend the executor semantics, then
relax eligibility to match that new executable shape. Do not widen the
predicate just because a profile contains recursive calls.

## Consequences

- Tail-recursive return calls keep the existing sync IR trampoline path.
- Non-tail recursive expressions such as `fib(n - 1) + fib(n - 2)` avoid paying
  repeated failed trampoline setup and stay on the ordinary call path.
- Branch conditions remain outside sync IR trampoline eligibility unless a
  future executor can prove that shape without changing branch semantics.
- Future recursive-call performance work should report whether the selected
  profile's calls are tail calls, non-tail operand calls, branch-condition
  calls, or another call shape before changing trampoline eligibility.
- Proof should include the selected benchmark/profile, focused recursion
  semantics tests, and a post-change CPU profile that confirms failed
  trampoline setup disappeared from the selected hot path.

## Related

- `docs/performance/fib-trampoline-eligibility.md`
- `docs/adrs/0126-keep-proper-tail-calls-runtime-context-owned.md`
- `docs/adrs/0139-keep-tail-restarts-through-expression-branches-and-finally-completions.md`
- `.claude/rules/proper-tail-calls.md`
- `.claude/rules/performance-profiling-guardrails.md`
