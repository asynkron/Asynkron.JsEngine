# ADR 0112: Keep RegExp instance cache bounded and keyed by runtime shape

## Status

Accepted

## Context

Issue `autrun-dirl74ca7a0g-8d6fc2682c` / PR #0 optimized repeated Test262
matcher execution after the profiling pass showed avoidable .NET `Regex`
construction churn. The accepted delivery added a shared `Regex` instance cache
inside `JsRegExp` and reused compiled native-source text for host functions.

`JsRegExp` still has to normalize ECMAScript RegExp syntax into the .NET bridge
shape before matching. That means a reusable .NET `Regex` is safe only after the
engine has finished the same runtime normalization that an uncached instance
would use, including large-quantifier capping and option selection. Reusing by
source pattern alone would be too broad because flags and bridge options affect
observable matching behavior.

The same delivery also replaced array iteration callback argument-array literals
with a three-value struct carrier. That is an instance of the already accepted
argument-carrier rule in ADR 0101 rather than a separate architecture decision.

## Decision

Keep shared .NET `Regex` instance reuse behind a bounded runtime-shape cache:

1. key the cache by the capped normalized pattern and `RegexOptions`, not by the
   raw ECMAScript source pattern alone;
2. run the same normalization and quantifier-capping path before cache lookup
   that an uncached `JsRegExp` instance would run;
3. keep each `JsRegExp` object's `_compiledRegex` field as the first fast path
   so per-instance reuse does not pay concurrent-dictionary lookup costs;
4. bound the shared cache and clear it when it exceeds the configured limit
   instead of letting Test262-style generated pattern families grow without
   limit; and
5. keep construction-time syntax validation, capture metadata construction,
   `lastIndex`, and RegExp statics on the existing runtime paths. The cache
   reuses only the .NET `Regex` object for an equivalent runtime shape.

Do not turn this cache into broad RegExp construction laziness. Laziness has
different observable timing constraints and remains governed by narrower ADRs
such as ADR 0003 and ADR 0079.

## Consequences

- Repeated Test262 matcher patterns can avoid rebuilding equivalent .NET
  `Regex` objects without changing JavaScript-visible RegExp semantics.
- Future RegExp performance work must treat the cache key as semantic surface:
  any new normalization, option, timeout, or bridge behavior that changes .NET
  matching must be represented in the key or must decline shared-cache reuse.
- Cache bounds are part of the design, not incidental cleanup. Generated
  property-escape and Test262 fixture families can otherwise trade CPU wins for
  unbounded process memory.
- Allocation reductions from callback argument carriers should continue to
  follow ADR 0101: preserve concrete struct carriers through hot paths and avoid
  `IReadOnlyList<JsValue>`-typed boxing before the callback consumes them.

## Related

- `docs/adrs/0003-defer-annex-b-single-escape-regexp-construction.md`
- `docs/adrs/0079-keep-eval-regexp-literal-fast-path-grammar-shaped.md`
- `docs/adrs/0101-keep-function-call-argument-carriers-typed-through-hot-paths.md`
- `.claude/rules/ecmascript-regexp-runtime-bridges.md`
- `.claude/rules/performance-profiling-guardrails.md`
