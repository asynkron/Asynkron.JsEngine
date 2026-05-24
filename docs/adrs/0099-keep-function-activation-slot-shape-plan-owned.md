# ADR 0099: Keep function activation slot shape plan-owned

## Status

Accepted

## Context

Issue
`planitem-planmanual1779530433702731000-reduce-function-call-activation-overhead-p-3adf2bdf0d`
/ PR #1638 reduced function-call activation overhead by moving planned-function
activation metadata out of call-time reconstruction.

Before the repair, `ExecutionPlanRunner` derived the activation slot capacity
from `ExecutionPlan.SafeRootSlotMap` during environment setup. That required
walking the root slot map to find the maximum slot index, then combining it with
root-slot and internal-slot counts for every planned function activation.

The accepted delivery introduced `ExecutionPlan.ActivationSlots` /
`ActivationSlotShape`, built once by `ExecutionPlanBuilder` from the root scope
id, root slot count, layout id, immutable slot map, and ordered slot names. The
runner now consumes that metadata directly when calling
`ResetSlotLayoutForPlan`, with the ordered names used only as a defensive
capacity fallback when the recorded count is zero.

Review also required nested stamped plans to preserve the activation metadata
when `StampNestedExecutionPlan` copies plan instructions for enclosing-scope
slot stamping.

Issue
`planitem-planmanual1779530433702731000-reduce-function-call-activation-overhead-s-8f7318b9d0`
/ PR #1648 then exposed a follow-on allocation trap in closure-backed
parameter binding. The runtime correctly initialized a function activation with
the planned logical slot count and enough backing capacity for known activation
appends, but slot-backed closure parameter paths still called
`DefineParameterFast` after writing the preallocated parameter slots. That
second definition appended duplicate parameter bindings on each invocation,
inflating `JsSlot[]` growth and `GrowSlots` pressure even though the slot shape
was already known.

Issue `autrun-dir4p2zvmkps-4dd788bea7` / PR #1740 showed the same slot-growth
pressure from the other side of activation setup. The `classdef` profile spent
constructor-invocation time in
`BindFunctionParameters -> DefineSlot -> GrowSlots -> Array.Copy` because the
IR runner appended predictable activation bindings into environments with no
reserved backing capacity. The fix pre-sized the function and parameter
environments for known activation appends while keeping logical slot count and
binding order unchanged.

## Decision

Keep planned-function activation slot shape owned by `ExecutionPlan` and
computed by the builder.

Runtime activation setup should consume `ActivationSlots` as the authority for:

1. the root scope id used for activation slot layout;
2. the slot capacity needed by the function body environment;
3. the layout id used to validate or reset pooled environments;
4. the immutable symbol-to-slot map; and
5. any ordered slot-name view needed for diagnostics or defensive fallbacks.

Do not rederive activation capacity from `SafeRootSlotMap` or scan slot-map
values in `ExecutionPlanRunner` during function calls. If slot-shape semantics
change, update the builder-owned `ActivationSlotShape` construction and ensure
nested plan stamping carries the updated metadata.

When a function activation has initialized slot storage from that planned shape,
parameter binding must write directly to those slots. Do not call
`DefineParameterFast` as an additional closure-binding step for a parameter slot
that already exists in the activation layout. `DefineParameterFast` remains the
fallback for dictionary/no-slot paths and for real appended activation bindings,
not a mirror for every slot-backed parameter write.

Activation environments should also reserve backing capacity for predictable
runtime appends before those bindings are defined. Capacity pre-sizing is allowed
for known activation-owned bindings such as `this`, `new.target`, runner
bookkeeping, `super`, `arguments`, function-name bindings, and simple
parameters, but it must not change the logical slot count, binding order, or
observable environment semantics.

## Consequences

- Function activation stays on a metadata-consumption path instead of doing
  call-time slot-map shape analysis.
- The builder remains the single owner for converting scope-slot analysis into
  activation layout metadata.
- Runner code can stay focused on environment setup and should not gain
  fallback heuristics that recompute root-slot shape from maps.
- Slot-backed parameter binding remains allocation-stable for closure functions:
  preallocated parameter slots are updated in place instead of duplicated as
  appended bindings on every invocation.
- Known activation-owned appends can avoid per-invocation `GrowSlots` and
  `Array.Copy` pressure without moving semantic ownership away from the runner.
- Capacity helpers remain a performance reservation detail. They must be kept
  in sync with activation binding order and proved with activation/class and
  slot-environment tests when changed.
- Future activation optimization work should preserve this ownership boundary
  and prove behavior with the activation proof pack from
  `.claude/rules/function-activation-proof-pack.md`.
- Profiling claims still need activation-specific evidence from
  `.claude/rules/performance-profiling-guardrails.md`; this ADR only records
  the ownership boundary for slot-shape metadata.

## Related

- `.claude/rules/function-activation-proof-pack.md`
- `.claude/rules/performance-profiling-guardrails.md`
- `docs/performance/classdef-ir-environment-pre-sizing.md`
