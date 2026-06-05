# ADR 0332: Admit resumable try/catch with owned frame state

## Status

Accepted

## Context

Faktorial issue
`planitem-planmanual1780639098493226000-full-unified-bytecode-execution-burndown-b-ba3b90f110`
and PR #3226 widened resumable unified bytecode so simple generator and async
bodies can execute `try/catch` across suspension in
`UnifiedBytecodeVirtualMachine.ExecuteResumable`.

Before this delivery, ordinary sync exception regions were VM-owned, but the
resumable route still kept `EnterCatch` and `PopEnvironment` out of the
resumable opcode and instruction allowlists. The remaining B32 gap was not just
an allowlist row: a catch block can suspend after a body throw or after
`iterator.throw(...)`, so the resume state must preserve the active try frame,
catch-used bit, thrown value, inactive catch-binding slots, and pending finally
completion across yield/await boundaries.

The slice kept the existing soundness boundary for suspending `finally` blocks.
A `finally` block that can yield or await during early close still stays on the
IR runner until the resumable VM owns suspending cleanup execution.

## Decision

Admit the narrow resumable `try/catch` shape only when exception-region state is
owned by the resume state and handled by the resumable VM.

- Store resumable try frames on `UnifiedBytecodeResumeState`, replacing the old
  parallel descriptor-index and resume-target arrays with frame objects that
  carry `CatchUsed`, `ThrownValue`, `FinallyScheduled`, and pending completion.
- Store inactive catch-binding slot state on the resume state so direct reads
  after leaving catch still throw through the VM-owned ReferenceError path while
  catch-local reads inside the handler see the thrown value.
- Add resumable handlers for `EnterCatch` and `PopEnvironment`, and admit
  `EnterCatchInstruction` only for absent or identifier catch bindings.
- Keep destructuring catch bindings declined. They need binding-target
  evaluation and environment/slot synchronization that this slice did not own.
- Keep suspending `finally` blocks declined. Try-body and catch-body suspension
  can route because the resumable frame carries the needed state across the
  boundary; suspending cleanup execution remains a separate ownership problem.

## Consequences

- Future resumable exception-region widening must update eligibility, compiler
  descriptors, resume-state fields, VM abrupt-completion handling, catch-binding
  lifetime, route/no-route proof, and expansion-contract inventory together.
- Do not treat removed resumable allowlist gaps as mechanical unless the state
  that crosses suspension is explicitly represented on
  `UnifiedBytecodeResumeState`.
- Destructuring catch bindings and suspending finally cleanup remain explicit
  pre-VM boundaries until a later slice owns their state and proof pack.
- The ordinary sync exception-region ADR remains valid, but resumable work must
  additionally prove state survives yield/await and external `.throw(...)`.

## Evidence

- PR #3226 merged as squash commit
  `7924bb5686f1e79352a459be2523b7cd746a4790`.
- Implementation changed
  `src/Asynkron.JsEngine/Execution/UnifiedBytecode/UnifiedBytecodeVirtualMachine.cs`,
  `UnifiedBytecodeProductionEligibility.cs`, and `UnifiedBytecodeProgram.cs`.
- Drift inventories removed `EnterCatch` / `PopEnvironment` from the resumable
  opcode and instruction gap lists in
  `tests/Asynkron.JsEngine.Tests/ExpressionProgramCoverageMapTests.cs`.
- Focused proof added
  `tests/Asynkron.JsEngine.Tests/UnifiedBytecodeResumableTryCatchTests.cs`,
  covering throw statements inside try, external generator `.throw(...)` into
  try, catch binding lifetime after catch, and optional catch binding routing.
- Delivery docs updated B32 in `docs/bytecode-progress.md`,
  `docs/plans/bytecode-burndown-checklist.md`, `docs/roadmap.md`, and
  `docs/unified-bytecode-expansion-contract.md`.

## Related

- ADR 0271:
  `docs/adrs/0271-keep-unified-bytecode-exception-regions-vm-owned-and-driver-cleanup-topology-guarded.md`
- ADR 0323:
  `docs/adrs/0323-keep-resumable-unified-bytecode-context-sensitive-cleanup-declined.md`
- ADR 0324:
  `docs/adrs/0324-keep-resumable-generator-context-cleanup-declines-explicit.md`
- `docs/rules/unified-bytecode-prototypes.md`
