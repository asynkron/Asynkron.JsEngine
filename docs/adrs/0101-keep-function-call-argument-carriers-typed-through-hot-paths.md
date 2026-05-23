# ADR 0101: Keep function-call argument carriers typed through hot paths

## Status

Accepted

## Context

Issue
`planitem-planmanual1779530433702731000-reduce-function-call-activation-overhead-s-f3dc144c31`
/ PR #1657 added a conservative simple sync-function activation fast path.

The initial fast path reduced activation setup work, but the memory profile still
showed helper argument-list allocations for common call arities. The follow-up
repair found that `TwoValueArgs` and the empty-argument helper only stayed
allocation-free while carried as concrete values. Passing those readonly struct
argument lists through `IReadOnlyList<JsValue>`-typed hot helpers boxed the
struct or forced a helper object, which moved the cost instead of removing it.

The accepted delivery changed typed callable invocation so arity-specific call
helpers preserve the concrete argument-list type into `SyncFunctionInvoker` and
simple activation parameter binding. Two-argument calls now use a direct typed
path before the generic fallback, and the empty runner placeholder uses
`Array.Empty<JsValue>()` instead of a boxed helper singleton.

## Decision

Keep function-call hot paths typed over their argument carrier until the
activation boundary has consumed the arguments.

For sync-function invocation and simple IR activation:

1. pass arity-specific struct argument lists through generic `TArgs` helpers
   constrained to `IReadOnlyList<JsValue>`;
2. avoid assigning struct argument lists to `IReadOnlyList<JsValue>` locals or
   parameters on the hot path because that boxes the struct;
3. add direct arity helpers before generic callable fallback when a common arity
   can avoid materializing an array or helper object;
4. use shared array instances such as `Array.Empty<JsValue>()` for required
   placeholder argument lists instead of boxed empty helper values; and
5. keep the generic fallback for uncommon or unsafe callable shapes rather than
   widening the simple activation predicate.

`TwoValueArgs` remains valid only when the concrete struct type is preserved
through the generic path. It should not be treated as allocation-free after it
has crossed an interface-typed hot-path boundary.

## Consequences

- Activation optimization must consider both environment setup and argument
  carrier adaptation. A fast activation can still allocate if call helpers box
  struct argument lists on entry.
- Future arity-specific invocation work should preserve concrete argument-list
  types through `CallableInvokeHelpers`, `SyncFunctionInvoker`, parameter
  binding, and simple activation setup.
- Allocation claims should check the relevant profile output for helper carrier
  rows such as `TwoValueArgs` or `EmptyValueArgs`, not just the total allocation
  number.
- This complements ADR 0099 and ADR 0100: ADR 0099 owns activation slot-shape
  metadata, ADR 0100 owns observable `arguments` binding creation, and this ADR
  owns allocation-stable argument carrier flow.

## Related

- `docs/adrs/0099-keep-function-activation-slot-shape-plan-owned.md`
- `docs/adrs/0100-keep-observable-arguments-binding-eval-aware-through-arrows.md`
- `.claude/rules/function-activation-proof-pack.md`
- `.claude/rules/performance-profiling-guardrails.md`
