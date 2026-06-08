# ADR 0346: Keep script IR fallback classified with production decline details

## Status

Accepted

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

## Decision

Keep the remaining top-level script IR fallback centralized behind
`RunScriptViaClassifiedIrFallback`, and pass through the production
`UnifiedBytecodeProductionEligibility.EvaluateScript(...)` result when a script
declines.

- The accepted path still runs all-or-nothing through
  `TryRunScriptViaProductionUnifiedBytecode` and
  `UnifiedBytecodeVirtualMachine.Execute`.
- The non-accepted path may still delegate to `ExecutionPlanRunner.RunScript`,
  but only through the classified helper.
- The helper log must include the stable decline code and detail from the
  production eligibility result: `code={DeclineCode}` and
  `detail={DeclineReason}`.
- While ordinary non-dynamic script declines still exist, keep them behind the
  same classified helper. Do not recast the helper as terminal-dynamic-only
  until the proof manifest has executable route-hit and no-route evidence that
  those ordinary declines are gone.
- The helper log must include `terminalDynamicResidue={TerminalDynamicResidue}`
  so terminal dynamic residue remains explicit without hiding ordinary E5c
  runner-retirement work.
- A non-script execution kind that reuses the script runner must synthesize an
  explicit unsupported-plan decline instead of appearing as an ordinary
  production script decline.
- Future script-route widening should delete or narrow the classified fallback
  only after route-hit and no-route proof demonstrates that the relevant script
  family is VM-owned. It should not replace this boundary with an unclassified
  direct runner call or a VM fallback.

## Consequences

- Route logs can distinguish "script declined for a known production gate" from
  "script never tried production bytecode" while the runner remains alive.
- Non-residue ratchets can assert the exact decline family for script safety
  cases instead of merely asserting absence of a fast-path route.
- Source gates should keep `ExecutionPlanRunner.RunScript` isolated to the
  helper until E5 removes the script runner edge entirely.
- The docs should continue calling this classification-only E5 progress, not a
  retirement claim; `RunScriptViaClassifiedIrFallback` still delegates to the
  tier-2 IR runner.
- The proof manifest should keep E5c open while the script fallback is needed
  for non-admitted ordinary scripts, even when terminal dynamic residue is
  separately excluded from ordinary E5 retirement.

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

## Related

- `docs/rules/unified-bytecode-prototypes.md`
- `docs/bytecode-progress.md`
- `docs/plans/bytecode-burndown-checklist.md`
- ADR 0201: `docs/adrs/0201-keep-unified-bytecode-production-routing-decline-first.md`
- ADR 0204: `docs/adrs/0204-keep-unified-bytecode-sync-production-routing-slot-bridged.md`
- ADR 0246: `docs/adrs/0246-keep-unified-bytecode-expansion-contract-source-of-truth-and-drift-guarded.md`
- `src/Asynkron.JsEngine/Ast/Legacy/StatementNodeExtensions.cs`
- `tests/Asynkron.JsEngine.Tests/UnifiedBytecodeProductionInvocationTests.cs`
