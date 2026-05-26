# ADR 0173: Keep observable arguments setup profile-owned and plan-proven

## Status

Accepted

## Context

Issue `autrun-disk07x5rsi8-08cbb528b9` / PR #2103 selected
`activation-arguments-lite` from the optimizer benchmark table because it was a
large current Asynkron-vs-Jint loss with a narrow activation owner:

```text
activation-arguments-lite  asynkron_ms=5762  jint_ms=652  Jint 8.84x faster
```

The focused CPU profile showed the selected strict-mode workload spending time
inside `ExecutionPlanRunner.CreateExecutionEnvironment`:

```text
CreateExecutionEnvironment
  HashSet<Symbol>.ConstructFrom
  CreateArgumentsObject
    JsArgumentsObject.ctor
      JsObject.DefinePropertyInternalDirect
        Dictionary.Resize
  HoistVarDeclarations
```

The workload actively reads `arguments`, so the concrete `JsArgumentsObject`
and its binding are observable. Earlier ADRs already allow skipping arguments
object materialization only when `argumentsObjectNeeded` and
`NeedsArgumentsBinding` prove the object is unobservable. This slice needed a
different boundary: retain observable arguments semantics while removing setup
work that the current profile proved was unnecessary for the selected shape.

## Decision

Keep observable arguments setup optimizations profile-owned and plan-proven.

When `arguments` is observable, do not turn an `activation-arguments` hotspot
into a lazy-materialization change. The runtime must still create the
`JsArgumentsObject` and bind it through the existing activation path documented
by ADR 0100 and ADR 0124.

Capacity-only descriptor pre-sizing is acceptable for `JsArgumentsObject` when
the argument count is already known. The reservation may cover numeric argument
properties plus standard metadata properties, but it must not change descriptor
attributes, insertion order, mapped-parameter behavior, strict `callee`
accessors, iterator exposure, or later descriptor promotion semantics.

Activation hoist work may be skipped only behind existing semantic facts:

1. strict-mode functions do not need sloppy Annex B legacy block-function
   blocked-name set construction; and
2. function-body var/function hoisting may be skipped only when the cached
   `HoistableDeclarationsPlan` proves there are no hoistable declarations.

Do not replace these with source-text checks, benchmark-name checks, or
runner-local AST predicates. Lexical TDZ setup, parameter-expression handling,
direct eval, sloppy Annex B behavior, and observable `arguments` binding
semantics remain on their existing owners.

## Consequences

- Strict `activation-arguments` paths can avoid descriptor resize churn and
  no-op Annex B/hoist scans without weakening observable `arguments` behavior.
- Descriptor capacity tuning belongs at the runtime type that owns the storage:
  `JsArgumentsObject` for its tracked descriptors and `JsObject` for backing
  descriptor/insertion-order storage.
- Future activation-arguments performance work needs a selected CPU profile,
  repeated focused timing, and the activation semantics proof pack before
  claiming a retained win.
- If the remaining owner is lexical-name or hoist metadata construction, the
  next slice should prove that owner directly instead of broadening arguments
  materialization policy.

## Related

- `docs/performance/activation-arguments-descriptor-and-hoist-fast-path.md`
- `docs/adrs/0100-keep-observable-arguments-binding-eval-aware-through-arrows.md`
- `docs/adrs/0124-keep-lazy-arguments-object-materialization-observable-and-profile-owned.md`
- `docs/adrs/0099-keep-function-activation-slot-shape-plan-owned.md`
- `.claude/rules/function-activation-proof-pack.md`
- `.claude/rules/performance-profiling-guardrails.md`
