# ADR 0167: Keep sync IR trampoline frame capacity shallow-first

## Status

Accepted

## Context

Issue `autrun-disgkh65fjdk-eae1e197e9` / PR #2072 selected
`activation-params-lite` from the optimizer benchmark table because it was the
largest current Asynkron-side loss in that run:

```text
profile                    asynkron_ms  jint_ms  delta
activation-params-lite            1416      297  Jint 4.77x faster
```

The focused CPU profile showed the selected hot path under simple strict
parameter calls paying sync IR trampoline frame setup cost:

```text
TypedAstEvaluator.SyncFunctionInvoker.SyncIrCallTrampoline.TryInvoke
Buffer.BulkMoveWithWriteBarrier
TypedAstEvaluator.SyncFunctionInvoker.SyncIrCallTrampoline.PushFrame
```

`activation-params-lite` is shallow and non-recursive. Each entry needs one
active trampoline frame, but `SyncIrCallTrampoline` eagerly allocated backing
storage for 64 frames before the first push. That made the common shallow path
pay zeroing and copy cost for capacity it did not use.

The accepted delivery reduced `InitialFrameCapacity` from `64` to `4`, leaving
the existing growth path intact for deeper trampoline stacks. Repeated
selected-profile timings improved from noisy baseline runs of `1432`, `770`,
and `896` ms to final runs of `604`, `571`, `568`, and `633` ms. The final
average was about 42% faster, and even the worst final run beat the fastest
baseline by about 18%.

Issue #2075 / PR #2091 returned to the same owner surface after the activation
allocation profiles still showed pressure around eligible simple IR calls. The
accepted slice moved the top-level trampoline frame array to
`ArrayPool<SyncIrFrame>` storage while keeping the shallow initial capacity and
growth-driven shape.

The delivery also exposed two lifecycle traps. A `[ThreadStatic]` scratch cache
was rejected during review because repo policy forbids thread-static shared
state and because nested or reentrant trampoline use can corrupt shared scratch
leases. The follow-up review then caught that growth cleanup and final cleanup
have different ownership: after live frames are copied into a new rented array,
the copied frames still own their nested `Slots`, `ExpressionStack`, and flag
arrays. Fully clearing those nested arrays through the old frame storage would
clear data now owned by the live frames. Final pool return has the inverse risk:
the backing array must not retain `ExecutionPlan` or activation-shape
references after the invocation ends.

## Decision

Keep sync IR trampoline frame storage shallow-first, growth-driven, and
explicitly pool-owned.

The startup frame array should cover the common shallow invocation case without
prepaying for deep recursion. Deeper eligible trampoline stacks must continue
to use `EnsureFrameCapacity` growth rather than an oversized eager initial
allocation.

Future changes may raise or specialize the initial frame capacity only when a
current selected CPU profile proves that repeated growth, not shallow setup, is
the selected owner. That proof must remain separate from trampoline eligibility:
capacity tuning must not change which calls are eligible, active frame count,
argument/receiver binding, expression stack state, fallback behavior, or the
proper-tail-call contract.

When the top-level frame array is pooled, storage ownership is part of the same
decision:

1. rent the top-level `SyncIrFrame[]` per trampoline invocation and return it
   from the invocation `finally` path;
2. on frame-array growth, copy the live frame structs into the new rented
   storage, reset only the old frame structs for the active depth, and return
   the old top-level array without clearing nested scratch arrays that are now
   owned by the copied live frames;
3. on final invocation cleanup, clear each used frame's nested slot, expression
   stack, and flag arrays, then clear non-scratch references such as `Invoker`,
   `Plan`, `ActivationSlots`, and `ThisValue` before returning the backing
   array; and
4. do not introduce `[ThreadStatic]`, `AsyncLocal<T>`, or other shared scratch
   frame state as a shortcut around pool rent/return cost.

## Consequences

- Shallow simple IR activation calls avoid paying for unused 64-frame backing
  storage on every trampoline entry.
- Recursive or deeply chained eligible trampoline workloads still grow through
  the existing capacity path.
- Pooled frame storage can remove repeated top-level frame-array allocation
  without sharing frame state across calls or retaining activation metadata
  after pool return.
- Growth cleanup and final cleanup are intentionally separate operations. Using
  final cleanup on the old storage after a growth copy can wipe nested scratch
  arrays that live frames still need; skipping final reference cleanup can keep
  plans and activation shapes alive through the shared pool.
- Trampoline eligibility remains owned by ADR 0140 and proper tail calls remain
  owned by ADR 0126; this ADR only owns the frame backing-capacity choice.
- Future sync IR trampoline performance work should report whether the profile
  is dominated by shallow setup, failed eligibility, or real deep-stack growth
  before changing either capacity or eligibility.
- Proof should include the selected CPU profile, repeated selected-profile
  timings, the activation semantics proof pack when activation setup is touched,
  and a memory or seam-stability check when the slice touches runtime hot paths.

## Related

- `docs/performance/activation-params-trampoline-frame-capacity.md`
- `docs/adrs/0140-keep-sync-ir-trampoline-eligibility-executor-exact.md`
- `docs/adrs/0126-keep-proper-tail-calls-runtime-context-owned.md`
- `.claude/rules/performance-profiling-guardrails.md`
- `.claude/rules/function-activation-proof-pack.md`
