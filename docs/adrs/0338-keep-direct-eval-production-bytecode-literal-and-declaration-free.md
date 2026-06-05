# ADR 0338: Keep direct eval production bytecode literal and declaration-free

## Status

Accepted

## Context

Faktorial issue
`planitem-planmanual1780639098493226000-full-unified-bytecode-execution-burndown-b-4e57b65e02`
and delivery PR #3230 widened A2 captured/dynamic activation routing for the
ordinary sync production unified-bytecode VM.

The useful admitted slice is narrow: a function may execute a syntactic direct
`eval(...)` call, then continue with ordinary dynamic identifier reads, stores,
or calls on the production VM, when the eval source is known not to inject
bindings and the function has no captured activation, live `with` closure
state, or arguments-object dependency.

During review, the route briefly treated an identifier-loaded eval source as an
eligible direct-eval argument. That was unsound for the production route.
Identifier-loaded eval text is runtime data; its top-level declarations are not
known before the VM starts executing the function. A string such as
`"var value = 42;"` can create caller-visible bindings, and a VM frame that was
compiled before that text was parsed cannot prove slot layout or later dynamic
name behavior from the static expression program alone.

## Decision

Production unified bytecode admits the direct-eval call boundary only when the
source argument is a single non-spread literal whose string payload is classified
as declaration-free by the current static guard.

- Keep syntactic directness as boundary-local metadata on
  `CallInvocationBoundary`; do not infer it from receiver or dynamic lookup
  state.
- Keep the same-engine eval fast path and caller-slot resynchronization for the
  admitted non-injecting literal route.
- Decline identifier-loaded eval source, declaration-bearing eval literals,
  spread eval, and multi-argument eval to the existing IR/eval route.
- Treat eval-injected runtime bindings as dynamic residue until a future model
  owns declaration discovery, activation shape mutation, and post-eval dynamic
  name behavior before VM execution.

## Consequences

- A2 can route the useful non-injecting direct-eval plus ordinary dynamic-name
  shape without pretending all direct eval is bytecode-owned.
- Runtime eval text remains a pre-VM boundary even when the surrounding function
  otherwise has an admitted dynamic-name path.
- Future direct-eval widening must prove declaration discovery before VM entry,
  not just the call-target shape or the eval host fast path.
- Focused tests should cover both sides: literal direct eval must hit the
  production fast path, while identifier-loaded or declaration-bearing eval must
  compute correctly without the production route-hit marker.

## Evidence

- Delivery PR #3230 merged as squash commit
  `8f269560f24cc362fe459a583dc3f7ea5e700ab1`.
- Review repair commit before squash:
  `74db46ddaf4093263bf16dd5a175562ca3ee49e5`.
- Implementation changed
  `src/Asynkron.JsEngine/Execution/UnifiedBytecode/UnifiedBytecodeProductionEligibility.cs`,
  `tests/Asynkron.JsEngine.Tests/UnifiedBytecodeProductionEligibilityTests.cs`,
  `tests/Asynkron.JsEngine.Tests/UnifiedBytecodeProductionInvocationTests.cs`,
  `docs/plans/bytecode-burndown-checklist.md`,
  `docs/unified-bytecode-expansion-contract.md`, and
  `docs/rules/expression-bytecode-call-targets.md`.
- Focused review-repair proof passed:
  `rtk dotnet test tests/Asynkron.JsEngine.Tests --filter "FullyQualifiedName~UnifiedBytecodeProductionEligibilityTests&FullyQualifiedName~DirectEval"`
  and
  `rtk dotnet test tests/Asynkron.JsEngine.Tests --filter "FullyQualifiedName~UnifiedBytecodeProductionInvocationTests&FullyQualifiedName~DirectEval"`.
- Diff hygiene passed: `rtk git diff --check HEAD^..HEAD`.

## Related

- `docs/rules/expression-bytecode-call-targets.md`
- `docs/rules/ecmascript-direct-eval-declaration-instantiation.md`
- `docs/unified-bytecode-expansion-contract.md`
- `docs/plans/bytecode-burndown-checklist.md`
