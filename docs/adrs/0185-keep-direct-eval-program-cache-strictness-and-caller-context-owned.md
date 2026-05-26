# ADR 0185: Keep direct eval program cache strictness and caller context owned

## Status

Accepted

## Context

Issue `autrun-disol3tkc92g-850bc475d9` / PR #2142 selected
`activation-evalscope-lite` from the optimizer benchmark table:

```text
activation-evalscope-lite  asynkron_ms=3541  jint_ms=559  Jint 6.33x faster
```

The focused CPU profile showed repeated direct eval calls spending most of the
selected path under parse and plan construction:

```text
EvalHostFunction.Invoke
  JsEngine.ParseProgram
  ScriptPlanCache.Build
```

The winning delivery cached parsed eval `ProgramNode` instances in
`EvalHostFunction` with a bounded 64-entry LRU keyed by eval source text and
forced strictness. Reusing the immutable parsed program also reuses warmed AST
caches, including the script execution plan cache, while each eval call still
executes against the current eval environment.

This boundary is semantic, not just a cache placement detail. Direct eval can
observe the caller's current activation bindings, strictness changes parse and
early-error behavior, and private names must still be validated against the
current caller context after parsing. A broader source-only or global eval cache
would risk mixing those observable states.

Issue #2149 / PR #2156 followed the cache slice with a narrower call-path
optimization for `activation-evalscope-lite`: same-engine, one-argument,
non-spread direct eval from expression-program execution now enters
`EvalHostFunction` through an explicit fast entrypoint instead of first using
the generic callable/context handoff. Review caught the important boundary:
class-field-initializer state, caller context, and caller environment are
invocation-local eval inputs, so the shared eval core must receive them as
parameters rather than reading mutable state from the engine-global eval host.

## Decision

Keep repeated direct-eval program caching inside the eval host boundary and
keyed by all parse-shaping state used by the retained program. For the current
implementation, that state is the eval source text and `forceStrict`.

The cache may retain parsed `ProgramNode` instances and their warmed AST caches.
It must not cache eval environments, declaration-instantiation results,
execution results, caller bindings, private-name validation, `super` /
`new.target` eligibility, or any other invocation-local state.

The cache stays per engine through `EvalHostFunction`, bounded, and eviction
based. Parse errors are not cached. If a future slice changes parser options or
adds another eval parse-shaping flag, that flag must become part of the cache
key before the parsed program can be reused across calls.

Direct eval execution must continue to run declaration instantiation and program
execution against the current eval environment. Private-name validation and
caller-context validation remain after program lookup because they depend on
the current caller, not only on source text.

Eval call-path fast entries may bypass generic callable setup only for
expression-bytecode shapes that have already proven syntactic direct eval,
same-engine ownership, no spread, and the supported argument shape. Those fast
entries must pass caller context, caller environment, directness, and
class-field-initializer state explicitly into the shared eval core. They must
not cache or read those invocation-local values through `EvalHostFunction`
fields such as `CallingContext`, `CallingJsEnvironment`, `IsDirectCall`, or
`InClassFieldInitializer`.

Future eval-scope performance work must prove the current owner with an
`activation-evalscope` CPU profile before broadening cache scope, and must keep
focused activation/eval/private-name proof coverage beside repeated selected
profile timings.

## Consequences

- Repeated stable eval source can avoid reparsing and rebuilding execution
  plans without freezing the caller activation observed by direct eval.
- Strict and sloppy eval source stay separated by cache key instead of relying
  on later runtime checks to repair a wrongly parsed program.
- Cache size remains an engine-local operational bound, not a process-global
  source-text memory policy.
- Same-engine direct-eval fast paths can remove generic host-call setup cost
  without making caller activation state an implicit property of the shared
  eval host object.
- Future attempts to share eval parse state across realms, engines, or parser
  option sets need a separate ADR-level proof of the semantic key and eviction
  policy.

## Related

- `docs/performance/activation-evalscope-eval-program-cache.md`
- `docs/adrs/0015-keep-direct-eval-caller-lexical-context.md`
- `docs/adrs/0128-keep-private-name-parse-validation-entrypoint-owned.md`
- `docs/adrs/0132-keep-direct-eval-var-arguments-collision-checks-narrow.md`
- `.claude/rules/expression-bytecode-call-targets.md`
- `.claude/rules/performance-profiling-guardrails.md`
- `.claude/rules/ecmascript-direct-eval-declaration-instantiation.md`
