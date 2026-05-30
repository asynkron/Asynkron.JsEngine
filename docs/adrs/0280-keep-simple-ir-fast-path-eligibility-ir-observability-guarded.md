# ADR 0280 — Keep Simple IR Fast-Path Eligibility Guarded by IR-Observability Flags, Not the Spec-Level Arguments-Creation Flag

## Status

Accepted

## Context

`CanUseSimpleIrActivationFastPath` in `TypedAstEvaluator.SyncFunctionInvoker`
controls whether a function call takes the simple IR activation path (fast,
no struct boxing) or the full `ExecutionPlanRunner` path (slow, boxes struct
argument carriers to `IReadOnlyList<JsValue>` on every call).

The original guard included `_argumentsObjectNeeded ||` as the first condition:

```csharp
// Before (blocked all closures)
return !(
    _argumentsObjectNeeded ||    // <-- spec-level flag, true for most non-arrow functions
    _usesArguments ||
    _needsArgumentsBinding ||
    ...
);
```

`_argumentsObjectNeeded` is a compiler-set spec-level flag: the ECMAScript spec
says a non-arrow function with `arguments` neither as a parameter name nor as a
body `var` declaration must have an arguments object. This flag is `true` for
virtually all ordinary non-arrow functions — including closures that never
reference `arguments` in any IR instruction.

As a result, every closure call fell through to `InvokeWithContextSlow`, which
passed a `SingleValueArgs` struct via an `IReadOnlyList<JsValue>`-typed
parameter. The struct-to-interface assignment boxed it on every call.
For `activation-closures-lite` (120,000 calls per iteration), `CastHelpers.Box`
consumed 85.7% of `InvokeWithContextSlow` time.

The IR observability flags `_usesArguments` and `_needsArgumentsBinding` were
already present in the same guard but unreachable for closures because
`_argumentsObjectNeeded` short-circuited first.

## Decision

Remove `_argumentsObjectNeeded` from `CanUseSimpleIrActivationFastPath`.

```csharp
// After (closures eligible)
return !(
    // _argumentsObjectNeeded intentionally omitted: safe when _usesArguments
    // and _needsArgumentsBinding are both false — the IR plan has no
    // instructions that access the arguments binding.
    _usesArguments ||
    _needsArgumentsBinding ||
    ...
);
```

**Rationale**: `_argumentsObjectNeeded` answers the spec question "must an
arguments object be allocated for this function?" The IR observability flags
answer the runtime question "can the current plan's IR instructions observe the
arguments binding?" These are different questions. When both IR flags are false,
the plan contains no `LoadArguments`, no `ArgumentsBinding`, no direct eval in
the body, and no nested-arrow arguments capture — the arguments object can be
skipped entirely on the fast path without observable semantic change.

The spec creation requirement (`_argumentsObjectNeeded`) is satisfied by
creating the object when the function is on the slow path (i.e., when at least
one IR observability flag is true). It is not a constraint on which functions
may use a path that skips arguments setup entirely.

## Consequences

- Closure functions and other non-arrow functions that don't actually use
  `arguments` are now eligible for the simple IR activation fast path.
- Benchmark improvements from single guard removal:
  - `activation-closures-lite`: Jint 5.59x → Asynkron 1.13x (6.77x speedup)
  - `activation-arguments-lite`: 2354ms → 603ms (3.90x speedup)
  - `activation-evalscope-lite`: 2686ms → 516ms (5.20x speedup)
- **Do not re-add `_argumentsObjectNeeded` to `CanUseSimpleIrActivationFastPath`**
  as a "conservative" guard. It would re-block all closures from the fast path
  with no safety benefit, since `_usesArguments` and `_needsArgumentsBinding`
  already own all observable cases.
- Rule 11 of `docs/rules/function-activation-proof-pack.md` — requiring both
  `argumentsObjectNeeded` and `NeedsArgumentsBinding` guards — applies to
  **lazy materialization on the slow invocation path**, not to fast-path
  eligibility. The distinction: lazy materialization defers creating
  `JsArgumentsObject` on a path where it may still be needed; fast-path
  eligibility decides whether to skip the invocation context entirely. They are
  guarded by different predicates for sound reasons.

## Related

- PR #2646: delivery
- Issue `autrun-diuvwweuwsrs-e67e789465`: profile showed `CastHelpers.Box` at 85.7%
- ADR 0124: lazy arguments object materialization (slow-path, different concern)
- `docs/rules/function-activation-proof-pack.md` rule 11, rule 25
- `docs/performance/closure-simple-activation-fast-path.md`: benchmark evidence
