# ADR 0347: Keep resumable runner construction classified by route boundary

## Status

Accepted

## Context

Faktorial issue
`planitem-planitem-planmanual1780639098493226000-full-unified-bytecode-execution-b-25237d6be1`
and delivery PR #3284 targeted the E5 inventory for async function,
generator, and async-generator `ExecutionPlanRunner` construction.

Before the delivery, resumable invocation code still contained direct runner
construction in two semantically different situations:

- declined async/generator/async-generator bodies that intentionally fall back
  to the existing IR runner after `EvaluateResumable` declines, and
- accepted resumable bodies that need a setup-only materialized environment
  bridge for `super` property semantics before `UnifiedBytecodeResumeState`
  runs the accepted body through the VM.

Those two paths both mentioned `ExecutionPlanRunner`, but they do not mean the
same thing. The first is remaining E5 fallback inventory; the second is setup
state for an accepted route and must not be counted as accepted-body execution
delegating back to the runner.

The quality-gate repair found the source-gate risk directly. After the
implementation moved direct constructions behind classified helpers, the test
still expected inline `ExecutionPlanRunner` markers in accepted setup sections.
The repair changed the source gate to recognize the named
`CreateResumableSuperEnvironmentBridgeRunner` setup bridge and moved async
helper definitions below the accepted-route section boundary so accepted-route
assertions inspect only route bodies.

## Decision

Classify resumable runner construction by route boundary instead of by type
name alone.

- Declined async function bodies construct the runner only through
  `CreateClassifiedAsyncDeclinedBodyRunner`.
- Declined generator bodies construct the runner only through
  `CreateClassifiedGeneratorDeclinedBodyRunner`.
- Declined async-generator bodies construct the runner only through
  `CreateClassifiedAsyncGeneratorDeclinedBodyRunner`.
- Accepted resumable bodies that require a `super` environment may construct a
  runner only through `CreateResumableSuperEnvironmentBridgeRunner`, and only
  to obtain the materialized execution environment before
  `UnifiedBytecodeResumeState` is created.
- Source gates must inspect accepted execution sections separately from helper
  definitions. Accepted sections may allow the narrowly named setup bridge, but
  must still reject fallback runner construction, expression-program execution,
  AST evaluation, and async-step delegation markers in the accepted route body.
- Future E5 retirement work should delete or narrow classified helpers as each
  owner surface becomes VM-owned. It should not collapse declined-body fallback
  and accepted setup bridges back into one generic runner factory.

## Consequences

- Source scans and docs can distinguish remaining fallback inventory from
  setup-only accepted-route materialization.
- Accepted resumable route tests can stay broad and exception-driven without
  being forced to allow unclassified runner calls.
- The E5 inventory in `docs/unified-bytecode-expansion-contract.md` can name
  the actual owner surface for async, generator, and async-generator fallbacks.
- A future route-widening slice that removes a declined-body fallback should
  remove its classified helper or turn the relevant source-gate allowance into
  a tombstone, rather than leaving a stale generic runner bridge.

## Evidence

- ADR allocation note: the local `rtk faktorial-api adr-next` wrapper was not
  available in this runtime (`No such file or directory`), so this learn pass
  used the host HTTP allocator endpoint `POST /api/adrs/next`, which returned
  `{"adr_id":347}`.
- Delivery PR #3284 merged as commit
  `fce53add6c6e3561282f4a0cc3e2498c7422baeb1`.
- Build-stage repair commit
  `356d4bd852e3add13dfaf7fd64d430f2d73944d4` updated the source-gate markers
  after helper classification moved direct runner construction behind named
  helpers.
- The delivery changed:
  - `docs/unified-bytecode-expansion-contract.md`
  - `src/Asynkron.JsEngine/Ast/TypedAstEvaluator.AsyncFunctionInvoker.cs`
  - `src/Asynkron.JsEngine/Ast/TypedAstEvaluator.AsyncGeneratorInvoker.cs`
  - `src/Asynkron.JsEngine/Ast/TypedAstEvaluator.GeneratorFunctionBase.cs`
  - `src/Asynkron.JsEngine/Ast/TypedAstEvaluator.SyncGeneratorInvoker.cs`
  - `tests/Asynkron.JsEngine.Tests/UnifiedBytecodeProductionInvocationTests.cs`
- Focused build-stage verification recorded:
  - `rtk dotnet test tests/Asynkron.JsEngine.Tests/Asynkron.JsEngine.Tests.csproj -c Debug --filter "FullyQualifiedName~UnifiedBytecodeProductionInvocationTests.SourceGate_ProductionUnifiedBytecodeScriptAndResumableAcceptedPaths_DoNotDelegateToAstOrExecutionPlanRunner"` passed.
  - `rtk git diff --check` passed.

## Related

- `docs/rules/unified-bytecode-prototypes.md`
- `docs/unified-bytecode-expansion-contract.md`
- `tests/Asynkron.JsEngine.Tests/UnifiedBytecodeProductionInvocationTests.cs`
- ADR 0201: `docs/adrs/0201-keep-unified-bytecode-production-routing-decline-first.md`
- ADR 0321: `docs/adrs/0321-admit-simple-async-generator-resumable-route.md`
- ADR 0325: `docs/adrs/0325-admit-resumable-super-property-access-through-owned-resume-state.md`
- ADR 0346: `docs/adrs/0346-keep-script-ir-fallback-classified-with-production-decline-details.md`
