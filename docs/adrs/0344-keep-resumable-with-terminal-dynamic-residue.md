# ADR 0344: Keep resumable with terminal dynamic residue

## Status

Accepted

## Context

Faktorial issue
`planitem-planmanual1780639098493226000-full-unified-bytecode-execution-burndown-b-a97892fc0c`
and delivery PR #3264 closed the D3 unified-bytecode burndown row by
classifying `with` in resumable bodies and awaited with-object evaluation as
terminal dynamic residue.

The repo already admits sync non-awaited `with` statements through production
unified bytecode. ADR 0269 keeps that route activation-hoist and receiver-owned,
and ADR 0341 keeps active `with` depth separate from zero-depth dynamic-name
reachability. The remaining question in this delivery was not whether sync
`with` lowering can run through the VM; it was whether generator, async, or
async-generator bodies should route through `ExecuteResumable` while a dynamic
scope object can be entered before or across suspension.

The accepted delivery keeps that resumable shape declined. It added an explicit
reachable-instruction eligibility check for `EnterWithInstruction` and
`LeaveWithInstruction`, including `EnterWithInstruction.AwaitedProgram`, and
records the result as D3 dynamic residue instead of leaving it as an ambiguous
Phase B resumable gap.

## Decision

Keep `with` statements out of resumable production unified bytecode until the
VM owns dynamic-scope suspension semantics explicitly.

- `EvaluateResumable` must decline any reachable `EnterWithInstruction` or
  `LeaveWithInstruction` before routing the plan to `ExecuteResumable`.
- The decline includes awaited with-object evaluation, because the awaited
  object and the resulting dynamic scope boundary would both have to be
  represented by resumable state.
- Unreachable `with` instructions should not poison otherwise eligible
  resumable plans. The eligibility check follows the existing reachable
  instruction set and skips unreachable markers.
- Sync non-awaited `with` remains admitted through the production VM. Do not
  reuse the resumable D3 quarantine to roll back the sync route or weaken ADR
  0269 receiver and activation-hoist requirements.
- Future work that wants to admit resumable `with` must first model the active
  dynamic scope object, enter/leave balance, awaited-object settlement, and
  resumed lookup behavior as VM-owned state. It should not add an AST fallback
  or a runner callback inside the production route.

## Consequences

- B40 and B43 are closed as classified residue rather than as missing mechanical
  resumable opcode coverage.
- D3 is now a terminal dynamic-residue row in the burndown checklist, and the
  remaining work should focus on explicit VM state ownership before any
  resumable `with` admission is attempted.
- Reviewers can distinguish "sync `with` should route" from "resumable `with`
  should decline" without reopening with-depth or dynamic-name scan ownership.
- Eligibility tests must cover three sides of the boundary: generator/async
  `with` declines, awaited with-object declines, and unreachable `with` markers
  do not decline.

## Evidence

- Delivery PR #3264 merged as commit `106a7bc60`.
- The delivery changed:
  - `src/Asynkron.JsEngine/Execution/UnifiedBytecode/UnifiedBytecodeProductionEligibility.cs`
  - `tests/Asynkron.JsEngine.Tests/UnifiedBytecodeProductionEligibilityTests.cs`
  - `docs/plans/bytecode-burndown-checklist.md`
- Focused tests added:
  - `EvaluateResumable_WithDynamicIdentifierLoad_DeclinesD3Residue`
  - `EvaluateResumable_AwaitedWithObject_DeclinesD3Residue`
  - `EvaluateResumable_UnreachableWithBody_DoesNotDeclineD3Residue`
- The checklist now records:
  - B40 `with(obj){}` in generator/async body as D3 terminal dynamic residue;
  - B43 awaited with-object as D3 terminal dynamic residue;
  - D3 `with` quarantine for resumable bodies plus awaited with-object as
    complete.
- ADR allocation note: the local `rtk faktorial-api adr-next` wrapper was not
  available in this runtime (`No such file or directory`), so the learn pass
  used the runtime allocator endpoint `POST /api/adrs/next`, which returned
  `{"adr_id":344}`.

## Related

- ADR 0269: `docs/adrs/0269-keep-with-backed-unified-bytecode-dynamic-names-activation-hoist-and-receiver-owned.md`
- ADR 0341: `docs/adrs/0341-keep-with-depth-and-zero-depth-dynamic-name-scans-separate.md`
- `docs/rules/expression-bytecode-ast-seams.md`
- `docs/plans/bytecode-burndown-checklist.md`
