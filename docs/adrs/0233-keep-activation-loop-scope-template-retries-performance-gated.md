# ADR 0233: Keep activation loop-scope template retries performance-gated

## Status

Accepted

## Context

Issue `autrun-ditj75rrj9ts-9a27bab67f` / PR #2398 continued the recurring
optimizer chain for `activation-arguments-lite` after ADR 0173, ADR 0183, ADR
0199, and ADR 0211 had already removed larger observable-arguments,
lexical-template, root-TDZ, generator-state, and numeric arguments-index costs.

The selected workload was still a current Asynkron-vs-Jint loss, but the
remaining gap was much smaller than earlier slices:

```text
activation-arguments-lite  asynkron_ms=770  jint_ms=267  Jint 2.88x faster
```

Three focused baseline rows averaged 825.7 ms for Asynkron. The required CPU
profiles rooted at `InvokeWithContextSlow` kept naming the same remaining
owner:

```text
HandlePushEnvironment
  Buffer.BulkMoveWithWriteBarrier
```

The attempted slice added an ordered slot-template initializer for
`PushEnvironment` instructions whose `SlotNames` covered every logical slot.
The goal was to avoid clearing the loop-scope slot array and then stamping
lexical slot names. A second variant let pooled environments preserve the
backing slot array when a complete template was available.

Both variants built and the focused activation proof pack stayed green, but
repeated focused timing did not cross the recurring optimizer gate. The best
trial average was 775.3 ms, a 6.1% improvement against the 825.7 ms baseline,
below the required 10%+ threshold. The runtime edits were reverted, and the
delivery retained only `docs/performance/failed-activation-arguments-loop-scope-template.md`.

## Decision

Keep activation loop-scope slot-template retries performance-gated.

Do not retain an ordered slot-template initializer, pooled slot-array
preservation, or another `HandlePushEnvironment` clear-then-stamp micro-slice
for `activation-arguments-lite` unless fresh repeated selected-profile rows
clear the current issue's acceptance threshold beyond timing noise. A stable
`HandlePushEnvironment -> Buffer.BulkMoveWithWriteBarrier` call tree proves the
owner is real, but PR #2398 showed that avoiding the clear/stamp sequence alone
is too small after the earlier activation wins.

Future retained work on this residual owner should change the semantic boundary
rather than retry the same template shape. The likely successful boundary is
one of:

1. prove a non-capturing loop environment is unobservable for the selected
   ordinary strict path and elide or reuse that environment object; or
2. prove the loop binding can live in an existing flat-slot path without a
   per-iteration environment object.

Either boundary must stay proof-driven. Closure capture, direct eval, `with`,
generator/async suspension and resume, TDZ, per-iteration lexical binding
identity, iterator cleanup, and observable `arguments` materialization must
remain on the semantic path unless the owning lowering/runner contract models
them explicitly.

Do not infer eligibility from benchmark names, source text, or the absence of
closures in one profile script. The proof must be carried by lowering/plan
metadata and paired with `ActivationSemanticsProofPackTests`, focused selected
timing, and a follow-up CPU owner check.

## Consequences

- The failed performance note remains useful evidence, but it is not a
  retained optimization and must not be cited as a win.
- Future `activation-arguments-lite` work should read the failed note before
  retrying adjacent loop-scope setup micro-slices.
- `HandlePushEnvironment` remains a valid residual owner, but the next useful
  slice should target environment object lifetime or flat-slot loop-binding
  semantics, not another slot-array initialization variant.
- The 10% recurring optimizer gate remains a policy boundary: clean builds and
  green focused tests are not enough to keep a performance-only runtime change.

## Related

- `docs/performance/failed-activation-arguments-loop-scope-template.md`
- `docs/adrs/0173-keep-observable-arguments-setup-profile-owned-and-plan-proven.md`
- `docs/adrs/0183-keep-activation-lexical-name-templates-hoist-owned.md`
- `docs/adrs/0199-keep-activation-tdz-slot-indices-plan-owned-and-generator-state-gated.md`
- `docs/adrs/0211-keep-arguments-object-index-reads-storage-owned-and-descriptor-aware.md`
- `.claude/rules/function-activation-proof-pack.md`
- `.claude/rules/performance-profiling-guardrails.md`
