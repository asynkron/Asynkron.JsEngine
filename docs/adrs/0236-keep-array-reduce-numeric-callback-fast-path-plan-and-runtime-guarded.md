# ADR 0236: Keep Array reduce numeric callback fast path plan- and runtime-guarded

## Status

Accepted

## Context

Issue `autrun-ditkh3m7fjx4-a2cf88a53d` / PR #2414 continued the bounded
`arrayops` reducer-callback performance track after earlier reducer work had
made callback carriers typed and had added a guarded two-argument reducer path.

The selected baseline still showed an Asynkron-side loss:

```text
arrayops  asynkron_ms=1113  jint_ms=598  Jint 1.86x faster
```

Three pre-change CPU profile captures kept the owner under reducer callback
dispatch:

```text
ArrayPrototype.Reduce
  StandardLibrary.ReduceLike
    TypedAstEvaluator.SyncFunctionInvoker.InvokeWithContext
      TypedAstEvaluator.SyncFunctionInvoker.InvokeWithContextSlow
```

The hot reducer shape was the dense numeric callback `(a, b) => a + b`. The
invoker already had lowered-plan metadata for simple parameter-binary returns,
so the retained change added a reducer-only numeric callback fast path instead
of broadening generic callable dispatch or inferring arithmetic from source
text.

The unsafe alternatives were to treat all two-argument reducer arrows as
numeric, to key the shortcut from callback length, or to let `Array.reduce`
rebuild callback-body knowledge from syntax. JavaScript reducer callbacks can
observe string concatenation and coercion, callback-local `arguments`,
rest/default/destructured parameters, async/generator behavior, index and array
arguments, lexical/private/super state, and ordinary array element
observability through holes, prototypes, proxies, and accessors.

## Decision

Keep the `Array.prototype.reduce` / `reduceRight` numeric reducer callback
shortcut narrow, plan-owned, and runtime-guarded.

The array helper may ask only a typed `SyncFunctionInvoker` for the shortcut,
and only from the existing reducer loop after the current accumulator and
present element value have already been selected by the ordinary `ReduceLike`
semantics. The shortcut must not own element lookup, hole handling, inherited
prototype values, proxy checks, accessor reads, initial-accumulator selection,
or empty-array errors.

The invoker may return before ordinary callback invocation only when all of the
following are true:

1. the existing two-argument reducer callback predicate says the omitted
   `index`, `array`, and callback-local `arguments` values are unobservable;
2. the lowered `ExecutionPlan` carries a simple parameter-binary return shape
   whose activation slot shape matches the root plan;
3. both runtime operands selected from `(accumulator, value)` are already
   tagged JavaScript numbers; and
4. ordinary activation-observable shapes, including class/constructor,
   async/generator, default-derived-constructor, lexical-this, home-object,
   private-name, super, and instance-field state, stay on the existing callback
   invocation path.

Non-number operands must fall back to the ordinary invocation path so `+`
continues to perform string concatenation or object coercion and so unsupported
operators retain shared JavaScript operator semantics. Future widening for
coercive or mixed-type reducer callbacks needs its own plan-owned metadata,
runtime guard, and semantic proof; it must not be inferred from benchmark names,
source text, callback arity, or array density.

## Consequences

- The selected `arrayops` reducer slice avoids full sync-function invocation
  for proven numeric parameter-binary reducer callbacks. The retained repeated
  focused average moved from `1113 ms` to `790 ms`, a 29.0% Asynkron-side
  improvement.
- Reducers with string initial values, ordinary functions, observable callback
  arguments, rest/default/destructured parameters, async/generator callbacks,
  non-number operands, and non-simple return shapes keep the existing
  semantics.
- `reduceRight` shares the same callback shortcut only after ordinary
  right-to-left accumulator/value selection has occurred.
- Future `arrayops` work should pair selected-profile evidence with focused
  reducer regressions for numeric positive behavior, string/coercive fallback,
  full callback argument observability, prototype/accessor/proxy element
  behavior, and `reduceRight` order.

## Related

- `docs/performance/arrayops-reduce-numeric-binary-fast-path.md`
- `docs/adrs/0101-keep-function-call-argument-carriers-typed-through-hot-paths.md`
- `docs/adrs/0110-keep-simple-numeric-expression-program-fast-path-runtime-guarded.md`
- `docs/adrs/0178-keep-activation-params-binary-chain-fast-path-plan-and-runtime-guarded.md`
- `.claude/rules/performance-profiling-guardrails.md`
