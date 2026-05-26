# ADR 0199: Keep activation TDZ slot indices plan-owned and generator state gated

## Status

Accepted

## Context

Issue `autrun-disu408v4ns8-3128f4f119` / PR #2198 continued the
`activation-arguments-lite` optimizer chain from ADR 0173 and ADR 0183. The
selected strict ordinary-function workload still reads `arguments`, so
observable arguments materialization had to remain intact.

The fresh baseline showed the workload was still a large current loss:

```text
activation-arguments-lite  asynkron_ms=1377  jint_ms=274  Jint 5.03x faster
```

The focused CPU profile rooted at `InvokeWithContextSlow` showed activation
setup costs that previous slices had not removed:

```text
CreateExecutionEnvironment
  CastHelpers.Box
  JsEnvironment.ResetSlotLayoutForPlan
    JsEnvironment.MarkSlotsLexicalUninitialized
      ImmutableHashSet<T>.IEnumerable<T>.GetEnumerator
ExecuteInstructionLoop
  HandlePushEnvironment
    Buffer.BulkMoveWithWriteBarrier
```

The remaining root TDZ setup already had a stable plan-owned slot layout. The
runner was still carrying root lexical names as a symbol set into
`ResetSlotLayoutForPlan`, then enumerating that set and scanning slots during
each activation. The same ordinary-function path also defined generator-only
internal bindings (`YieldResumeContext` and the runner back-reference) even
when the function was not executing as a generator.

## Decision

Keep activation TDZ slot marking plan-owned when the root activation layout is
known.

`ActivationSlotShape` may carry precomputed root lexical slot indices derived
from the plan's root slot map and root lexical bindings. Runtime activation
setup should prefer those indices and mark TDZ slots directly by slot index.
The symbol-set path remains only as a compatibility fallback for layouts that do
not have precomputed indices.

Do not move this proof into `ExecutionPlanRunner` by source scanning,
benchmark-name checks, or runner-local AST predicates. The compiler/lowering
side owns the mapping from lexical symbols to root activation slot indices, and
the environment owns applying those flags to its current slot array.

Keep generator-only activation state gated by the actual runner kind. Ordinary
sync functions must not define `YieldResumeContext` or generator instance
bindings, and their function-environment capacity must not reserve those slots.
Generator and async/generator paths still own the resumable state bindings they
need.

## Consequences

- Root activation TDZ setup avoids per-call immutable-set enumeration and
  slot-name scanning when the plan already knows the lexical slot indices.
- `ActivationSlotShape` remains the durable metadata carrier for activation
  layout facts shared by full activation and simple IR activation paths.
- Ordinary function activation stops paying for generator-only internal
  bindings, while generator and async/generator semantics remain explicit.
- Future activation setup work must pair selected-profile evidence with the
  activation proof pack and generator-focused coverage before changing this
  boundary.
- Follow-up work on the remaining `HandlePushEnvironment` / `BulkMove` hotspot
  should prove that owner directly instead of widening arguments or generator
  activation policy.

## Related

- `docs/performance/activation-arguments-generator-state-gating.md`
- `docs/adrs/0173-keep-observable-arguments-setup-profile-owned-and-plan-proven.md`
- `docs/adrs/0183-keep-activation-lexical-name-templates-hoist-owned.md`
- `docs/adrs/0099-keep-function-activation-slot-shape-plan-owned.md`
- `.claude/rules/function-activation-proof-pack.md`
- `.claude/rules/performance-profiling-guardrails.md`
