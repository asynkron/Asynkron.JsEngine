# ADR 0177: Keep runner argument storage benchmark-proven

## Status

Accepted

## Context

Issue `autrun-disltaszb6mo-e6c0f409f5` / PR #2122 selected `classdef`
because the fresh comparison table still showed a large constructor-heavy loss:

```text
profile   asynkron_ms  jint_ms  delta
classdef         1137      336  Jint 3.38x faster
```

The required CPU profile found constructor and `super(...)` invocation cost
under `ExecuteProgramConstructNoSpread`. The sampled subtree included repeated
`CastHelpers.Box` below `SyncFunctionInvoker.InvokeWithContextSlow` while small
struct argument lists flowed into the IR runner:

```text
ExecuteProgramConstructNoSpread
  ReflectHelper.Construct
    SyncFunctionInvoker.InvokeWithContext
      SyncFunctionInvoker.InvokeWithContextSlow
        CastHelpers.Box
```

The attempted optimization replaced the runner's stored
`IReadOnlyList<JsValue>` argument field with a small value container that copied
up to four arguments into fields and allocated an overflow array for larger
lists. The code built, and the follow-up CPU profile removed the sampled boxing
subtree. Repeated focused timings still regressed:

```text
classdef  1939 ms
classdef  2143 ms
```

The runtime change was reverted. After revert, `classdef` measured 1542 ms and
1583 ms in the noisy environment, so no repeated >=10% Asynkron-side
improvement survived. The retained delivery is the failed-attempt performance
note:
`docs/performance/failed-classdef-runner-argument-container.md`.

Existing ADRs still stand: ADR 0101 keeps function-call argument carriers typed
through hot paths, and ADR 0171 keeps no-spread construct argument carriers
typed through the construct boundary. This issue exposed a narrower boundary:
removing an interface boxing subtree after the invocation boundary does not
prove that runner-level argument storage should become a copied value
container.

## Decision

Keep `ExecutionPlanRunner` argument storage unchanged unless a future slice
proves a runner-storage replacement with repeated selected-profile timing.

Typed arity carriers remain the right shape before and through call or
construct helper boundaries. Once arguments are stored on a runner for parameter
binding, `arguments` object creation, and activation setup, changing the stored
shape is its own optimization. It must not be justified solely by a cleaner CPU
call tree or the disappearance of `CastHelpers.Box`.

Future runner argument-storage work must prove all of the following before
retaining code:

1. a current profile still names runner argument storage or binding as the
   selected owner after earlier construct/call-boundary carrier work;
2. repeated focused benchmark timings clear the issue's improvement threshold
   in the same direction, not just one sampled profile subtree;
3. the new storage shape does not add copies, generic indirection, or overflow
   handling that outweighs the removed boxing;
4. observable activation behavior stays pinned for parameter binding,
   `arguments.length`, default/rest/destructured parameters, direct eval,
   derived `super(...)`, constructor `new.target`, and ordinary fallback
   arities; and
5. no retained performance note describes the change as successful unless the
   runtime code is kept and the timing threshold is met.

If these proofs fail, revert the runtime code and keep the failed attempt as
evidence instead of preserving a runner-storage abstraction that only improves
one sampled call tree.

## Consequences

- Future `classdef` and activation slices can still use concrete arity carriers
  before the invocation boundary, but runner storage changes need their own
  benchmark proof.
- A profile subtree disappearing is not a performance win when repeated
  selected-profile timings regress.
- Failed runner-storage experiments are useful as negative evidence when they
  name the exact attempted shape and final timings, but they should not be
  retried unchanged without fresh profile ownership.
- This keeps ADR 0101 and ADR 0171 as positive typed-carrier decisions while
  documenting that the post-boundary runner container attempt did not earn the
  same treatment.

## Related

- `docs/performance/failed-classdef-runner-argument-container.md`
- `docs/adrs/0101-keep-function-call-argument-carriers-typed-through-hot-paths.md`
- `docs/adrs/0171-keep-no-spread-construct-argument-carriers-and-super-spread-order.md`
- `.claude/rules/performance-profiling-guardrails.md`
