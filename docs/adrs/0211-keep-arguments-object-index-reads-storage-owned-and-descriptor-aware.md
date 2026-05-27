# ADR 0211: Keep arguments object index reads storage-owned and descriptor-aware

## Status

Accepted

## Context

Issue `autrun-dit3byldulw0-d4ff922b20` / PR #2249 continued the
`activation-arguments-lite` optimizer chain from ADR 0173, ADR 0183, and ADR
0199. The selected strict ordinary-function workload still reads `arguments`,
so the concrete `JsArgumentsObject` remains observable and must not be skipped.

The fresh selected-profile evidence showed a remaining current loss:

```text
activation-arguments-lite  asynkron_ms=1269  jint_ms=279  Jint 4.55x faster
```

Repeated baseline samples were noisy but kept the same owner shape, averaging
1128.7 ms for Asynkron. The focused CPU profiles rooted at
`InvokeWithContextSlow` showed the called function entering the property-read
path for numeric computed arguments access:

```text
ExecuteInstructionLoop
  GetProgramComputedPropertyValue
    JsOps.TryGetPropertyValueJsValue
      JsArgumentsObject.TryGetProperty
```

The workload uses strict code that reads `arguments[i]` in a loop. The
arguments object itself is observable, but the common numeric-index read was
still paying property-key string conversion plus generic object property lookup
even when the key was already a numeric index and the object was the engine's
own arguments object.

## Decision

Keep numeric `arguments[i]` read shortcuts owned by `JsArgumentsObject`, not by
a generic computed-property bypass.

`JsOps.TryGetArrayLikeValueJsValue` may route numeric property keys to
`JsArgumentsObject.TryGetIndex` only after the target is the concrete
arguments-object runtime type and the key resolves as an array index. The
arguments object remains responsible for the observable read decision:

1. mapped sloppy parameters read from the activation binding;
2. ordinary unmapped data descriptors can return their stored `JsValue`
   directly;
3. accessor descriptors, deleted indices, prototype values, and other
   non-direct cases fall back to the backing object's ordinary `[[Get]]` path
   with the original receiver; and
4. out-of-range numeric reads report no fast-path hit so the normal property
   path can preserve non-index and prototype semantics.

Do not turn selected `activation-arguments` evidence into permission to skip
`JsArgumentsObject` materialization, weaken mapped-parameter aliasing, or bypass
accessor/prototype behavior. Future arguments-object index read work must keep
the fast path at the storage owner and prove the direct data path plus the
mapped, accessor, and prototype fallbacks.

The same delivery kept the body-lexical setup improvement aligned with ADR
0183 by storing immutable `HoistPlan.BodyLexicalTemplate` data on
`JsEnvironment` and materializing a mutable `HashSet<Symbol>` only when a later
merge path needs one.

## Consequences

- Strict observable-arguments workloads can avoid repeated string-key and
  generic descriptor lookup work for direct numeric reads without changing
  object materialization semantics.
- `JsArgumentsObject` remains the boundary that knows mapped-parameter state,
  tracked descriptors, backing object descriptors, and prototype fallback.
- Future generic array-like read optimizations must not treat arguments objects
  like dense arrays; arguments objects have mapped parameters and descriptor
  observability that dense arrays do not own.
- Retained performance claims still need selected-profile before/after timing,
  focused CPU evidence, and the activation semantics proof pack.

## Related

- `docs/performance/activation-arguments-index-read-fast-path.md`
- `docs/adrs/0173-keep-observable-arguments-setup-profile-owned-and-plan-proven.md`
- `docs/adrs/0183-keep-activation-lexical-name-templates-hoist-owned.md`
- `docs/adrs/0199-keep-activation-tdz-slot-indices-plan-owned-and-generator-state-gated.md`
- `docs/adrs/0124-keep-lazy-arguments-object-materialization-observable-and-profile-owned.md`
- `.claude/rules/function-activation-proof-pack.md`
- `.claude/rules/performance-profiling-guardrails.md`
