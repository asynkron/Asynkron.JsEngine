# ADR 0346: Keep script IR fallback classified with production decline details

## Status

Accepted; amended by PR #3540.

## Context

Faktorial issue
`planitem-planitem-planmanual1780639098493226000-full-unified-bytecode-execution-b-1bdecc5080`
and delivery PR #3282 targeted E5 fallback-tier retirement evidence for
top-level script execution.

Before the delivery, accepted scripts already attempted production unified
bytecode through `EvaluateScript`, but a declined script fell directly into
`ExecutionPlanRunner.RunScript`. That made the remaining runner edge visible in
source but weakly classified in runtime evidence: reviewers could see that the
script runner still existed, but route logs did not carry the same stable
decline code and reason that the production eligibility gate produced.

The review repair found a second problem with the first classification pass:
the fallback helper must receive the already-computed `EvaluateScript`
eligibility result. Recomputing or replacing it with a generic fallback reason
would hide the precise non-residue decline that future burndown and ratchet
tests need, such as top-level lexical destructuring remaining outside the
accepted script route until its TDZ and lexical-environment semantics are
VM-owned.

Issue
`planitem-gh3377-rebaseline-the-finite-bytecode-retirement-inventory-retire-fallba-22f4b88ddd`
and delivery PR #3477 later tried to hard-tombstone ordinary script fallback
while leaving only terminal dynamic direct-eval residue classified. The quality
repair proved that was premature: current ordinary non-dynamic scripts can
still decline production routing for explicit safety reasons, so forcing those
declines to throw before `ExecutionPlanRunner.RunScript` would break valid
current execution rather than retire an unreachable seam.

Faktorial issue
`planitem-planitem-gh3495-shared-context-e5b-runner-entry-point-tombstones-after-e-54f613f240`
and delivery PR #3540 found the next narrower boundary. Ordinary script
declines still need a classified runner fallback for correctness, but they no
longer need to enter through the direct `ExecutionPlanRunner.RunScript` call
site that E5b tracks. Direct script runner calls can now be limited to terminal
dynamic direct-eval residue and eval execution-kind residue while ordinary
script declines use a named classified ordinary fallback entry.

## Decision

Keep top-level script production routing decline-first, but split the fallback
by semantic owner instead of one shared `RunScriptViaClassifiedIrFallback`
helper.

- The accepted path still runs all-or-nothing through
  `TryRunScriptViaProductionUnifiedBytecode` and
  `UnifiedBytecodeVirtualMachine.Execute`.
- Terminal dynamic direct-eval script residue may still delegate to
  `ExecutionPlanRunner.RunScript`, but only through
  `RunTerminalDynamicScriptResidueViaIrFallback`. Its log must include the
  stable production decline code, decline detail, and
  `terminalDynamicResidue={TerminalDynamicResidue}` marker.
- Eval execution kinds may reuse the script runner only through
  `RunEvalScriptViaIrFallback`, with an explicit unsupported-plan decline so
  they do not appear as ordinary production script declines.
- Ordinary script production declines must use
  `RunOrdinaryScriptViaClassifiedRunnerFallback` and
  `ExecutionPlanRunner.ExecuteClassifiedOrdinaryScriptFallback`. They should log
  `ordinary-script-classified-runner-fallback` with the production decline code
  and detail, but must not call `ExecutionPlanRunner.RunScript` directly or
  resurrect the deleted `RunOrdinaryDeclinedScript` wrapper.
- `RunScriptCore` may remain the shared implementation behind the public runner
  entries, but E5b source proofs should classify call sites and deleted wrappers
  rather than treating the shared implementation method as ordinary script
  residue.
- Future script-route widening should delete or narrow the classified fallback
  only after route-hit and no-route proof demonstrates that the relevant script
  family is VM-owned. It should not replace this boundary with an unclassified
  direct runner call or a VM fallback.

## Consequences

- Route logs can distinguish terminal dynamic direct-eval residue, eval
  execution-kind residue, and ordinary script production declines while the
  runner implementation remains alive.
- Non-residue ratchets can assert the exact decline family for script safety
  cases instead of merely asserting absence of a fast-path route.
- Source gates should keep direct `ExecutionPlanRunner.RunScript` calls isolated
  to the terminal dynamic and eval helpers, and should block ordinary script
  fallback from reusing that direct entry.
- The docs should continue calling this ordinary script fallback work E5c
  runner-retirement progress, not total E5 closure; the named ordinary fallback
  still executes the tier-2 IR runner through a classified entry.
- The proof manifest should keep E5c open while ordinary script fallback is
  needed for non-admitted scripts, even though direct `RunScript` call sites are
  no longer ordinary script fallback anchors.

## Evidence

- ADR allocation note: the local `rtk faktorial-api adr-next` wrapper was not
  available in this runtime (`No such file or directory`), so this learn pass
  used the host HTTP allocator endpoint `POST /api/adrs/next`, which returned
  `{"adr_id":346}`.
- Delivery PR #3282 merged as commit
  `feba4b97ff867689f26311e36f1c94befc9dedb3`.
- Review repair commit
  `f0588fdd10cd4f4d537c4a4113795472d0ba338e` preserved the script fallback
  decline code and reason through `RunScriptViaClassifiedIrFallback`.
- Delivery PR #3477 merged as commit
  `a96727577f4c3159447c21cdd3b5885698ab5722`.
- Build repair commit `bcf7c2062` restored the classified helper for current
  ordinary script declines and added the `terminalDynamicResidue` log field.
- The delivery changed:
  - `src/Asynkron.JsEngine/Ast/Legacy/StatementNodeExtensions.cs`
  - `tests/Asynkron.JsEngine.Tests/UnifiedBytecodeProductionInvocationTests.cs`
  - `docs/bytecode-progress.md`
  - `docs/plans/bytecode-burndown-checklist.md`
  - `docs/plans/bytecode-proof-manifest.json`
- Build-stage proof recorded:
  - `rtk dotnet test tests/Asynkron.JsEngine.Tests --filter "FullyQualifiedName~UnifiedBytecodeProductionInvocationTests"` passed 557 tests.
  - `rtk dotnet test tests/Asynkron.JsEngine.Tests --filter "FullyQualifiedName~BytecodeNonResidueDeclineRatchetTests"` passed 20 tests.
  - `rtk git diff --check` passed.
- PR #3477 build-stage proof recorded:
  - focused E5/runtime proof pack went from 0 passing tests to 9 passing tests
    after the repair.
  - `rtk git diff --check` passed.
  - the AST-eval seam scan had no hits.
- Delivery PR #3540 merged as commit
  `e4f06e36ee084fbfa76d0068a88d8d6a16f06e84`.
- PR #3540 changed:
  - `docs/plans/bytecode-burndown-checklist.md`
  - `docs/plans/bytecode-proof-manifest.json`
  - `src/Asynkron.JsEngine/Ast/Legacy/StatementNodeExtensions.cs`
  - `src/Asynkron.JsEngine/Ast/TypedAstEvaluator.ExecutionPlanRunner.Core.cs`
  - `tests/Asynkron.JsEngine.Tests/BytecodeProofManifestTests.cs`
  - `tests/Asynkron.JsEngine.Tests/UnifiedBytecodeProductionInvocationTests.cs`
- PR #3540 build-stage proof recorded:
  - `rtk dotnet test tests/Asynkron.JsEngine.Tests --filter FullyQualifiedName~BytecodeProofManifestTests` passed 234 tests.
  - `rtk git diff --check` passed.
  - the targeted stale-phrase search found no remaining
    `RunScriptViaClassifiedIrFallback` E5c wording.

## Related

- `docs/rules/unified-bytecode-prototypes.md`
- `docs/bytecode-progress.md`
- `docs/plans/bytecode-burndown-checklist.md`
- ADR 0201: `docs/adrs/0201-keep-unified-bytecode-production-routing-decline-first.md`
- ADR 0204: `docs/adrs/0204-keep-unified-bytecode-sync-production-routing-slot-bridged.md`
- ADR 0246: `docs/adrs/0246-keep-unified-bytecode-expansion-contract-source-of-truth-and-drift-guarded.md`
- ADR 0371: `docs/adrs/0371-keep-e5b-runner-entry-anchors-as-classified-allowlists.md`
- `src/Asynkron.JsEngine/Ast/Legacy/StatementNodeExtensions.cs`
- `tests/Asynkron.JsEngine.Tests/UnifiedBytecodeProductionInvocationTests.cs`
