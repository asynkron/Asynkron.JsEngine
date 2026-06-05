# ADR 0326: Admit resumable super member calls through captured method environment

## Status

Accepted

## Context

Issue
`planitem-planmanual1780639098493226000-full-unified-bytecode-execution-burndown-b-43182f6ef4`
/ PR #3189 widened the resumable unified-bytecode route for class generator,
async, and async-generator method bodies that call `super.m()` or
`super[k]()` after a `yield` / `await` suspension point.

The preceding super-property slice (ADR 0325 / PR #3188) established that
resumable super access must be owned by the resume state instead of rebuilt
ad hoc per VM step. Super member calls have the same activation problem plus a
call-reference problem: the VM must resolve the method from the super
prototype while preserving the derived instance as the call receiver.

The ordinary sync VM already had helpers for named and computed super call
targets, but the resumable route previously declined those opcodes. Reusing
the sync helpers directly is only sound when the resumable state carries the
right method environment across suspension.

## Decision

Admit named and computed super member calls in resumable unified bytecode only
when the captured method environment is available to the VM.

- Create a resumable invocation environment for method bodies with `this`
  initialized and a `SuperBinding` derived from the method home object.
- Thread that environment through `UnifiedBytecodeResumeState.CallingEnvironment`
  for resumable generator, async, and async-generator activations.
- Add `EnsureSuperReference`, `PrepareNamedSuperCallTarget`, and
  `PrepareComputedSuperCallTarget` to the resumable opcode allowlist only
  together with matching `ExecuteResumable` handlers.
- Keep the call stack contract identical to ordinary member calls:
  push the derived receiver as `this`, then push the resolved callee, and let
  `CallInvocationBoundary` perform the invocation.
- Keep `SuperConstructInvocationBoundary` declined. Derived constructors cannot
  themselves be generator or async function bodies, and lexical constructor-state
  inheritance remains outside this proven route until a legal resumable source
  shape demonstrates it.

## Consequences

- Resumable `super.m()` / `super[k]()` now routes through production unified
  bytecode after suspension while preserving the derived receiver.
- Future super-invocation widening must update activation setup, resumable
  eligibility, VM handlers, expansion-contract inventories, and end-to-end route
  tests in the same slice.
- Super construct remains a separate boundary; do not treat super member-call
  admission as evidence that constructor-state or `super()` semantics are owned
  by the resumable VM.

## Evidence

- Delivery PR #3189 merged as commit `d5f1ec06b`.
- The delivery added `CreateResumableInvocationEnvironment` and threaded the
  resulting environment through the sync generator, async function, and async
  generator resumable activations.
- The delivery added resumable VM handlers for
  `PrepareNamedSuperCallTarget` and `PrepareComputedSuperCallTarget`.
- The delivery added `UnifiedBytecodeResumableCallDispatchTests` coverage for
  named and computed super member calls after suspension, including route proof
  and receiver preservation.
- The expansion contract and bytecode burndown checklist were updated to mark
  B12 complete while keeping super construct declined.
- ADR allocation note: `rtk faktorial-api adr-next` was unavailable in this
  runtime (`No such file or directory`), so this learn pass used the Faktorial
  HTTP allocator endpoint `POST /api/adrs/next`, which returned
  `adr_id: 326`.

## Related

- `docs/adrs/0325-admit-resumable-super-property-access-through-owned-resume-state.md`
- `docs/rules/expression-bytecode-call-targets.md`
- `docs/unified-bytecode-expansion-contract.md`
- `src/Asynkron.JsEngine/Ast/JsEnvironmentExtensions.cs`
- `src/Asynkron.JsEngine/Ast/TypedAstEvaluator.SyncGeneratorInvoker.cs`
- `src/Asynkron.JsEngine/Ast/TypedAstEvaluator.AsyncFunctionInvoker.cs`
- `src/Asynkron.JsEngine/Ast/TypedAstEvaluator.AsyncGeneratorInvoker.cs`
- `src/Asynkron.JsEngine/Execution/UnifiedBytecode/UnifiedBytecodeProductionEligibility.cs`
- `src/Asynkron.JsEngine/Execution/UnifiedBytecode/UnifiedBytecodeVirtualMachine.cs`
- `tests/Asynkron.JsEngine.Tests/UnifiedBytecodeResumableCallDispatchTests.cs`
