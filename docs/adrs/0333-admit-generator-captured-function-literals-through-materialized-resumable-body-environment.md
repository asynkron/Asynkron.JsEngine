# ADR 0333: Admit generator captured function literals through a materialized resumable body environment

## Status

Accepted

## Context

Faktorial issue
`planitem-planmanual1780639098493226000-full-unified-bytecode-execution-burndown-b-0768364f94`
and PR #3234 widened resumable unified bytecode for the B23 nested function
literal family.

Before this delivery, non-capturing nested function literals already routed
through `LoadFunctionLiteral` and `EnsureHasName` in `ExecuteResumable`, but a
literal that captured a generator body binding stayed declined. That was not a
plain opcode gap. Resumable generator locals live in flat slots on
`UnifiedBytecodeResumeState`, while ordinary nested closures read captured
bindings through a `JsEnvironment` chain. A naive route either failed to expose
the captured binding or risked stale values after later slot writes.

The same delivery kept the adjacent B36 declaration boundary narrow. Capturing
function declarations, recursive or sibling helper graphs, block/Annex B
declarations, and class declarations still need declaration-instantiation
ownership beyond the environment materialization added here.

## Decision

Admit only generator nested function literals that capture root body locals by
materializing a generator body environment for the resumable route.

- Detect whether the lowered plan needs a materialized resumable body
  environment through `UnifiedBytecodeProductionEligibility`.
- Allow that route only for sync generators in this slice.
- Create a body `JsEnvironment` during generator invocation, mirror the current
  unified flat-slot values into that environment before execution, and keep
  environment-backed slots synchronized when the resumable VM writes,
  initializes, updates, or applies lowered declaration binding targets.
- Mark the resume state with `HasMaterializedBodyEnvironment` so the VM only
  pays the slot-environment synchronization cost for admitted captured-literal
  plans.
- Keep lexical-this, `new.target`, `super`, and private-name dependent nested
  literals declined until those closure contexts are materialized directly.
- Keep async and async-generator captured-local literals declined until their
  promise/async-generator settlement paths own the same body-environment
  lifetime across await and resume boundaries.
- Keep capturing function declarations and nested declaration-instantiation
  graphs declined; B36 remains separate from B23.

## Consequences

- Future B23 widening must prove slot-to-environment synchronization, not just
  opcode admission or route hits.
- Root-local captured generator literals can observe mutations across yield
  boundaries through the materialized environment, matching the IR runner's
  closure behavior.
- Any future async or async-generator version must account for the different
  invocation and settlement lifetimes before reusing this pattern.
- Super environment materialization and body environment materialization remain
  distinct ownership modes; accepted programs must not combine them until a
  later slice proves their interaction.
- B36 declaration work cannot be inferred from this environment bridge. Function
  declaration instantiation, helper dependency graphs, and block/Annex B
  semantics still need separate proof.

## Evidence

- PR #3234 merged as squash commit
  `59572bea3de5beba22d41e4c70488e515f01b46b`.
- Implementation changed
  `src/Asynkron.JsEngine/Ast/TypedAstEvaluator.SyncGeneratorInvoker.cs`,
  `src/Asynkron.JsEngine/Ast/TypedAstEvaluator.UnifiedBytecodeResumableActivation.cs`,
  `src/Asynkron.JsEngine/Execution/UnifiedBytecode/UnifiedBytecodeProductionEligibility.cs`,
  `src/Asynkron.JsEngine/Execution/UnifiedBytecode/UnifiedBytecodeProgram.cs`,
  and
  `src/Asynkron.JsEngine/Execution/UnifiedBytecode/UnifiedBytecodeVirtualMachine.cs`.
- Focused proof lives in
  `tests/Asynkron.JsEngine.Tests/UnifiedBytecodeResumableNestedFunctionTests.cs`,
  including generator captured function-literal routing, observed post-yield
  mutations, method/accessor literal variants, async captured-literal declines,
  and lexical/private-context declines.
- Delivery docs updated
  `docs/unified-bytecode-expansion-contract.md`,
  `docs/plans/bytecode-burndown-checklist.md`, and
  `docs/bytecode-progress.md` to keep B23 partial and B36 separate.

## Related

- ADR 0323:
  `docs/adrs/0323-keep-resumable-unified-bytecode-context-sensitive-cleanup-declined.md`
- ADR 0324:
  `docs/adrs/0324-keep-resumable-generator-context-cleanup-declines-explicit.md`
- ADR 0328:
  `docs/adrs/0328-admit-resumable-class-literal-public-instance-fields-with-activation-safe-initializers.md`
- `docs/rules/unified-bytecode-prototypes.md`
