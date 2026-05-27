# ADR 0217: Keep derived constructor this-init lookup local-first

## Status

Accepted

## Context

Issue #2279 / PR #2289 followed the failed `classdef` retry set from ADR 0214.
The retained slice stayed inside the constructor / `super(...)` dispatch owner
surface named by the current CPU profile:

```text
ExecuteProgramConstructNoSpread
  ReflectHelper.Construct
    SyncFunctionInvoker.InvokeWithContextSlow
      ExecutionPlanRunner.RunSync
        ExecuteProgramSuperConstruct
```

The previous retry set showed that broader typed no-spread construct shortcuts,
simple parameter-binding shortcuts, and `SetHomeObject` / simple-activation
gate changes did not produce a repeatable selected-profile win. This run kept
those boundaries intact and targeted only the derived-constructor bookkeeping
needed when `super(...)` initializes `this`.

Derived class constructors already create a function environment that owns the
uninitialized `this` state before `super(...)` runs. The expression-program
`SuperConstruct` handler still often had to rediscover that owner through
lexical binding search or `ResolveConstructorThisEnvironment()`. That made the
hot path pay for environment-chain resolution in the common case where the
current function environment is already the correct owner.

## Decision

For derived class constructors, define `Symbol.LexicalThisEnvironment` directly
on the function environment and point it at that same environment.

When `ExecuteProgramSuperConstruct` needs the environment whose
`ThisInitialized` binding must be updated, resolve it local-first:

1. check the current environment for a local `LexicalThisEnvironment` object;
2. fall back to the existing lexical-chain lookup for inherited arrow or nested
   lexical contexts;
3. when resolving through `this`, use the environment directly if it already
   owns a local `ThisInitialized` binding; and
4. keep `ResolveConstructorThisEnvironment()` and the final
   `ThisInitialized` chain search as fallbacks.

Treat that order as semantic, not just as a performance preference. The final
generic `ThisInitialized` chain search is a fallback only; it must not move
ahead of the lexical-this or constructor-this owner resolution.

Do not turn this into a broader constructor shortcut. `ReflectHelper.Construct`
remains the construction boundary, spread and generic construct handling stay on
their existing paths, ADR 0193 simple-activation eligibility is unchanged, and
`SetHomeObject` invalidation remains untouched.

## Consequences

- Derived-constructor `super(...)` can update `this` initialization through a
  local owner path on the common case instead of repeatedly resolving the same
  owner through the environment chain.
- Nested lexical contexts still preserve semantics because the existing
  lexical-chain and constructor-this fallbacks remain in place.
- Future `classdef` constructor/super work should treat the derived-constructor
  function environment as the owner of `ThisInitialized` state before widening
  activation or construction boundaries.
- Issue #2282 / PR #2296 proved the fallback order is externally observable in
  direct-eval arrow constructor cases. Reordering the generic `ThisInitialized`
  search ahead of constructor-this resolution can select a stale binding and
  break `super()` initialization semantics; that runtime trial was reverted and
  retained only as failed-attempt evidence.
- The retained evidence for PR #2289 was:
  - baseline `classdef` Asynkron row: `1369 ms`;
  - final focused rows after one noisy outlier: `881 ms` and `900 ms`;
  - focused class/super plus lowering proof pack: 166 tests passed; and
  - runner AST seam scan: no `EvaluateExpression(` or
    `ProfileEvaluateExpression(` hits in `TypedAstEvaluator.ExecutionPlanRunner*`.

## Related

- `src/Asynkron.JsEngine/Ast/TypedAstEvaluator.SyncFunctionInvoker.cs`
- `src/Asynkron.JsEngine/Ast/TypedAstEvaluator.ExecutionPlanRunner.Helpers.cs`
- `docs/adrs/0193-keep-class-method-simple-ir-activation-super-guarded.md`
- `docs/adrs/0214-keep-classdef-homeobject-and-construct-retries-profile-proven.md`
- `docs/performance/failed-classdef-homeobject-and-construct-trials.md`
- `.claude/rules/performance-profiling-guardrails.md`
