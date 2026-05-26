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

Issue `autrun-dir3tzc35h6w-a5094b82c5` / PR #1728 later removed the leftover
legacy dynamic-call `object?[]` argument-array helpers from `JsValueCache`. The
first delivery deleted the unused `RentArgumentArray`, `ReturnArgumentArray`,
and `CreateArgs` helper surface, then review caught that the now-unreferenced
`ObjectPool<object?[]>` fields still existed. The follow-up removed those pools
too, leaving only the `JsValue[]` argument-array pools for the dynamic-call fast
path.

Issue `autrun-dirl74ca7a0g-8d6fc2682c` / PR #0 extended the same typed-carrier
lesson to array iteration callbacks. `map`, `filter`, `forEach`, and related
iteration helpers previously created three-element argument-array literals for
`(value, index, array)`. The accepted slice introduced `ThreeValueArgs` and a
generic callback helper so those three callback arguments stay concrete until
the callable consumes them.

Issue `autrun-dis3ezcjxsm0-238752b986` / PR #1949 refined that array-iteration
callback path from the `classdef` profile. The callback hot path still paid to
materialize the `(value, index, array)` carrier even for simple arrow callbacks
such as `value => value * 3` that cannot observe the index or array arguments.
The accepted slice added a conservative callback-shape predicate and used
`SingleValueArgs` only for non-async, non-generator arrow callbacks with zero or
one simple identifier parameter and no parameter expressions. Observable
callbacks, including ordinary functions that can inspect `arguments`, rest
parameters, and callbacks with index or array parameters, continue through the
full three-argument path.

Issue `autrun-dis4ox6n39q8-5cc99c3db3` / PR #1955 applied the same typed-call
boundary lesson to recursive one-argument JavaScript calls. The `fib` profile
showed repeated `SyncFunctionInvoker` self-calls still entering the generic
single-argument callable helper before returning to the typed invoker. The
accepted slice routed only `SyncFunctionInvoker` calls with an available
`EvaluationContext` directly to `InvokeWithContext1` from both the
expression-program call executor and the shared single-argument helper, while
leaving host functions, eval, debug-aware host functions, spread calls, class
constructor rejection, and other arities on their existing paths.

Issue `autrun-dis5yv0qlsr4-6ad7f476ff` / PR #1961 extended the same carrier
rule to the full reducer callback shape. The `arrayops` profile still showed
array iteration callbacks as the selected owner after earlier one- and
three-argument callback work. `Array.prototype.reduce`/`reduceRight` must pass
all four observable callback arguments `(accumulator, value, index, array)`, so
the accepted slice introduced `FourValueArgs` and routed typed JavaScript
callbacks through `InvokeWithContext<FourValueArgs>` instead of allocating a
temporary `JsValue[]` per callback. The focused proof pinned
`arguments.length`, index, and array-observability for reduce.

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
5. after a call path has migrated from `object?[]` to `JsValue[]`, delete the
   obsolete helper methods, local return hooks, and private pool fields in the
   same cleanup slice; and
6. keep the generic fallback for uncommon or unsafe callable shapes rather than
   widening the simple activation predicate; and
7. when reducing an array-iteration callback arity, prove that the callback
   shape cannot observe omitted arguments before replacing the spec callback
   carrier. Ordinary functions, rest parameters, parameter expressions,
   callbacks with explicit index/array parameters, async functions, and
   generators must keep the observable full-argument path unless a separate
   proof establishes a narrower safe shape; and
8. when bypassing shared callable dispatch for a single-argument typed
   JavaScript call, keep the shortcut keyed to the concrete
   `SyncFunctionInvoker` identity plus an available call context. Do not widen a
   recursive `fib` win into a generic one-argument shortcut for host functions,
   direct eval, debug-aware host functions, spread calls, constructor rejection,
   or unproven callable shapes; and
9. for reducer callbacks that must expose four spec arguments, use a concrete
   four-value carrier through the typed invocation path rather than allocating a
   temporary argument array, and keep the generic fallback for non-typed
   callables.

`TwoValueArgs`, `ThreeValueArgs`, `FourValueArgs`, and future arity-specific
struct carriers remain valid only when the concrete struct type is preserved
through the generic path. They should not be treated as allocation-free after
crossing an interface-typed hot-path boundary.

## Consequences

- Activation optimization must consider both environment setup and argument
  carrier adaptation. A fast activation can still allocate if call helpers box
  struct argument lists on entry.
- Future arity-specific invocation work should preserve concrete argument-list
  types through `CallableInvokeHelpers`, `SyncFunctionInvoker`, parameter
  binding, and simple activation setup.
- Recursive typed JavaScript call sites may skip the generic helper layer only
  after profiling shows that layer is the selected hot owner. The direct
  `InvokeWithContext1` path is an arity-specific typed-function optimization,
  not a replacement for generic callable semantics.
- Array iteration may use narrower argument carriers only after the invoker
  owns an explicit callback-shape predicate. Do not infer safety from callback
  length alone if `arguments`, rest, parameter expressions, async/generator
  execution, or extra formal parameters can observe the omitted values.
- Reducer callbacks still need the full four-argument carrier when the callback
  shape can observe `arguments`, index, or the receiver array. The optimization
  is typed-carrier preservation, not argument omission.
- Dynamic-call cleanup should scan both the public/private helper methods and
  the backing pool fields. A no-caller search for `ReturnArgumentArray` is not
  enough if `ObjectPool<object?[]>` fields remain allocated and named as a
  plausible future entry point.
- Allocation claims should check the relevant profile output for helper carrier
  rows such as `TwoValueArgs`, `ThreeValueArgs`, or `EmptyValueArgs`, not just
  the total allocation number.
- This complements ADR 0099 and ADR 0100: ADR 0099 owns activation slot-shape
  metadata, ADR 0100 owns observable `arguments` binding creation, and this ADR
  owns allocation-stable argument carrier flow.

## Related

- `docs/adrs/0099-keep-function-activation-slot-shape-plan-owned.md`
- `docs/adrs/0100-keep-observable-arguments-binding-eval-aware-through-arrows.md`
- `.claude/rules/function-activation-proof-pack.md`
- `.claude/rules/performance-profiling-guardrails.md`
