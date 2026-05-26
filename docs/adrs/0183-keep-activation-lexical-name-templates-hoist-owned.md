# ADR 0183: Keep activation lexical-name templates hoist-owned

## Status

Accepted

## Context

Issue #2123 / PR #2132 continued the `activation-arguments-lite` work from ADR
0173. The selected strict-mode workload actively reads `arguments`, so the
`JsArgumentsObject` and binding remain observable. After the descriptor and
no-op hoist setup reductions, the residual focused CPU profile still showed
activation setup time in lexical-name construction:

```text
CreateExecutionEnvironment
  HashSet<Symbol>.ConstructFrom
  CreateArgumentsObject
    JsArgumentsObject.ctor
      JsObject.DefinePropertyInternalDirect
        Dictionary.Resize
  HoistVarDeclarations
```

`HoistPlan` already owned immutable cached templates for the same metadata:
`LexicalTemplate`, `SimpleCatchParameterTemplate`, and `BodyLexicalTemplate`.
The execution runner was still cloning lexical and simple-catch sets on every
activation before it knew whether the hoist path would need mutable working
sets.

## Decision

Keep activation lexical-name setup template-owned and hoist-owned.

`HoistPlan.BodyLexicalTemplate` is the source of truth for the body lexical set
used by `CreateExecutionEnvironment`; it represents the lexical names after the
simple-catch exclusion. The runner must not rebuild that body set by cloning all
lexical names and applying `ExceptWith` on every activation.

Construct mutable `HashSet<Symbol>` instances only for consumers that need
mutable or retained sets:

1. the body lexical set passed into the environment for later body-lexical
   checks; and
2. the lexical, simple-catch, and active-catch working sets passed into
   `HoistVarDeclarations`, and only when `HoistableDeclarationsPlan` proves
   there is hoist work.

Do not pool these activation lexical sets unless a future change proves and
owns every return point. `ExecutionPlanRunner` also serves generator, async, and
async-generator activation paths, and environment-retained sets can outlive a
single synchronous call through suspension.

The accepted proof remains the activation semantics proof pack plus focused
owner-surface profile evidence. The final `activation-arguments-lite` CPU call
tree no longer showed `HashSet<Symbol>.ConstructFrom` in the hot activation
subtree under `CreateExecutionEnvironment`.

## Consequences

- Strict observable-arguments activation can avoid per-call lexical-template
  reconstruction without weakening observable `arguments` behavior from ADR
  0100, ADR 0124, and ADR 0173.
- Cached hoist metadata stays on `HoistPlan`, while the runner owns only the
  mutable copies required by environment retention or hoist mutation.
- Future work that changes activation lexical-name setup must prove
  `BodyLexicalTemplate` equivalence for simple-catch exclusion, preserve Annex B
  blocked-name behavior, and pair semantic proof with the selected activation
  profile.

## Related

- `docs/adrs/0173-keep-observable-arguments-setup-profile-owned-and-plan-proven.md`
- `docs/performance/activation-arguments-descriptor-and-hoist-fast-path.md`
- `.claude/rules/function-activation-proof-pack.md`
