# ADR 0159: Keep no-args literal return fast path plan-proven

## Status

Accepted

## Context

Issue `autrun-dise0lhwhiaw-0099fde2a6` / PR #2027 continued the bounded
optimizer run against the `activation-noargs-lite` profile. The selected
baseline showed a clear no-argument activation gap:

```text
activation-noargs-lite          634      130  Jint 4.88x faster
```

The focused CPU profile pointed at sync IR call setup rather than expression
evaluation:

```text
TypedAstEvaluator.SyncFunctionInvoker.SyncIrCallTrampoline.PushFrame
Buffer.BulkMoveWithWriteBarrier
```

The accepted delivery cached simple literal-return shape on `ExecutionPlan`.
`SyncFunctionInvoker` now uses that plan metadata to return the literal before
renting a full invocation context or pushing sync IR trampoline frames, while the
existing trampoline still has a guarded literal-return fallback for call sites
that reach it.

Focused proof kept the activation semantics pack green, and repeated selected
profile samples moved Asynkron from the 634 ms baseline to 276, 262, and 265 ms.

## Decision

Keep the no-argument literal-return shortcut plan-proven and activation-guarded.

The only acceptable direct-return shape is:

1. the function's lowered `ExecutionPlan` has a simple return expression whose
   expression program is exactly one literal operation;
2. the existing simple IR activation eligibility check still passes for the
   call;
3. the function is a sync ordinary function, not a constructor/class,
   generator, async function, or async generator; and
4. the invocation cannot observe skipped activation setup through `arguments`,
   captured activation environments, dynamic scope, private names, `super`,
   home-object state, instance fields, or other activation-sensitive metadata.

Do not replace the plan check with a source-text predicate such as "body is
small", "no parameters", or "looks like return 1". The executable proof surface
is the lowered plan and expression bytecode because that is the payload the
runtime would otherwise execute.

Do not generalize this into a broad return fast path for parameter reads,
binary expressions, object literals, closures, class constructors, or dynamic
activation cases. Those shapes can be optimized only after their own plan-owned
proof and focused activation coverage.

## Consequences

- No-argument literal-return functions can skip context rental, environment
  setup, and sync trampoline frame work only when those operations are
  unobservable for the proven plan shape.
- `ExecutionPlan` remains the owner for reusable return-shape metadata; runtime
  callers consume it instead of rebuilding shape checks from AST or source text.
- Future activation optimizations must keep the proof pack broad enough to
  cover positive direct-return behavior and negative dynamic or
  activation-observable fallback cases.
- Performance claims for this boundary still need selected-profile baseline,
  CPU owner evidence, repeated focused timing, and canonical internal quality.

## Related

- `docs/performance/activation-noargs-literal-return-fast-path.md`
- `.claude/rules/function-activation-proof-pack.md`
- `.claude/rules/performance-profiling-guardrails.md`
- `docs/adrs/0099-keep-function-activation-slot-shape-plan-owned.md`
- `docs/adrs/0140-keep-sync-ir-trampoline-eligibility-executor-exact.md`
