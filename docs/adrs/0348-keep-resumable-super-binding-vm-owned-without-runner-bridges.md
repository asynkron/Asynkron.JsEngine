# ADR 0348: Keep resumable super binding VM-owned without runner bridges

## Status

Accepted

## Context

Faktorial issue
`planitem-planitem-planmanual1780639098493226000-full-unified-bytecode-execution-b-e947984722`
and delivery PR #3285 retired the setup-only
`CreateResumableSuperEnvironmentBridgeRunner` path that PR #3284 had
classified as an accepted-route bridge.

That earlier classification was useful while accepted resumable `super` bodies
still needed an `ExecutionPlanRunner` only to materialize an environment before
`UnifiedBytecodeResumeState` execution. The next delivery moved the actual
state the VM needs into `UnifiedBytecodeResumeState.ResumableSuperBinding`
instead: invokers snapshot the method receiver, home-object prototype, and
inherited binding state up front, then `EnsureSuperReference`,
super-property read/write/update, and super call-target opcodes consume that
snapshot directly.

The quality-gate repair found the durable doc/test risk. The broad no-mixed
execution source gate still required the deleted bridge marker, so the accepted
path could not pass until the gate required the new `ResumableSuperBinding`
setup markers and rejected runner-backed environment materialization.

## Decision

Keep resumable `super` state VM-owned.

- Accepted async, generator, and async-generator resumable setup may create a
  `SuperBinding` snapshot when the compiled program contains super opcodes.
- `UnifiedBytecodeResumeState.ResumableSuperBinding` is the execution-time owner
  for resumable super reference validation, property access, mutation, updates,
  and call-target preparation.
- Accepted resumable `super` bodies must not construct an
  `ExecutionPlanRunner` or call
  `GetOrCreateExecutionEnvironmentForInternalUse` just to supply `super`
  semantics.
- The deleted `CreateResumableSuperEnvironmentBridgeRunner` marker is now a
  tombstone, not a source-gate allowance. Future source gates should require
  the `TryCreateResumableSuperBinding` and `ResumableSuperBinding = ...` setup
  shape for accepted routes while continuing to reject unclassified runner
  construction.
- Declined async/generator/async-generator bodies may still use their
  classified declined-body runner helpers; that fallback inventory is separate
  from accepted resumable `super` execution.

## Consequences

- Accepted resumable `super` execution no longer depends on a runner-owned
  environment bridge.
- Source-gate repairs for this area should remove bridge allowances instead of
  carrying them forward as historical exceptions.
- ADR 0347 remains the historical classification of the transitional bridge;
  this ADR records the follow-on retirement of that accepted-route bridge.
- The E5 fallback inventory should describe accepted super bodies as using
  `ResumableSuperBinding`, while keeping declined-body runner helpers as the
  remaining fallback surfaces.

## Evidence

- ADR allocation note: the local `rtk faktorial-api adr-next` helper was not
  available in this runtime (`No such file or directory`), so this learn pass
  used the host HTTP allocator endpoint `POST /api/adrs/next`, which returned
  `{"adr_id":348}`.
- Delivery PR #3285 merged as commit
  `b5919c0fb98078f7ac6229d5feb5b5928c21fa81`.
- The delivery removed the setup bridge from:
  - `src/Asynkron.JsEngine/Ast/TypedAstEvaluator.AsyncFunctionInvoker.cs`
  - `src/Asynkron.JsEngine/Ast/TypedAstEvaluator.AsyncGeneratorInvoker.cs`
  - `src/Asynkron.JsEngine/Ast/TypedAstEvaluator.GeneratorFunctionBase.cs`
  - `src/Asynkron.JsEngine/Ast/TypedAstEvaluator.SyncGeneratorInvoker.cs`
- The delivery added `UnifiedBytecodeResumeState.ResumableSuperBinding` and
  changed resumable super opcode handlers in
  `src/Asynkron.JsEngine/Execution/UnifiedBytecode/UnifiedBytecodeVirtualMachine.cs`
  to read that binding directly.
- Focused build-stage verification recorded the repaired source gate passing
  after commit `822358a6d Fix resumable super source gate`.

## Related

- `docs/rules/unified-bytecode-prototypes.md`
- `docs/unified-bytecode-expansion-contract.md`
- `tests/Asynkron.JsEngine.Tests/UnifiedBytecodeProductionInvocationTests.cs`
- `tests/Asynkron.JsEngine.Tests/UnifiedBytecodeResumableSuperPropertyReadTests.cs`
- ADR 0325: `docs/adrs/0325-admit-resumable-super-property-access-through-owned-resume-state.md`
- ADR 0347: `docs/adrs/0347-keep-resumable-runner-construction-classified-by-route-boundary.md`
