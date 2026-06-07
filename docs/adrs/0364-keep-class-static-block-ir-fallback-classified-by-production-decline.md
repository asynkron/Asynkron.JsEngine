# ADR 0364: Keep class static-block IR fallback classified by production decline

## Status

Accepted.

## Context

Issue
`planitem-planitem-planmanual1780730299657353000-unified-bytecode-remaining-burndo-bbaf44ae93`
and delivery PR #3372 targeted the E5 class/static-block bridge in
`ClassDefinitionExtensions.ExecuteStaticBlock`.

Before this slice, eligible class static-block bodies could already attempt
production unified bytecode, but the remaining `ExecutionPlanRunner.RunScript`
edge needed a stronger proof contract. Static blocks are especially sensitive
because direct eval and declaration-producing runtime source can observe the
class static-initialization environment. Treating every static-block body as
ordinary route-widening would risk admitting runtime-source semantics before the
broader class-definition environment bridge owns them.

The selected delivery outcome was a ratchet rather than direct-eval admission:
runtime-source direct eval remains declined, and the static-block fallback is
source-gated as a classified fallback after a production unified-bytecode
eligibility decline.

## Decision

Keep `ClassDefinitionExtensions.ExecuteStaticBlock` ordered as:

- create the static-block lexical environment;
- attempt `TryExecuteStaticBlockViaUnifiedBytecode(...)`;
- if production eligibility declines, log the stable decline code and reason;
- then and only then delegate to `ExecutionPlanRunner.RunScript`.

The accepted path must stay all-or-nothing through
`UnifiedBytecodeVirtualMachine.Execute` and must not delegate to
`ExecutionPlanRunner`, `ExpressionProgram`, or AST evaluation inside the
accepted section.

Runtime-source direct eval and any otherwise non-production static-block plan
remain B24h/B36 residue until a future class-definition environment slice proves
the exact eval environment and declaration semantics. Future widening should
replace a specific decline with route-hit proof and nearby no-route proof; it
should not replace the classified fallback with a generic runner call or a VM
fallback.

## Consequences

- The remaining static-block runner edge is auditable E5 residue, not an
  unclassified hot-path escape.
- Logs preserve the production decline family through the
  `classified-static-block-ir-fallback` message with `code={DeclineCode}` and
  `detail={DeclineReason}`.
- B24h and B36 can keep runtime-source direct-eval neighbors open without
  blurring them with eligible static-block bodies that now route through
  production unified bytecode.
- Source gates should keep the accepted static-block path free of runner,
  expression-program, and AST delegation while the fallback section may still
  contain the classified `ExecutionPlanRunner.RunScript` call.

## Evidence

- ADR allocation note: local `rtk faktorial-api adr-next` was unavailable in
  this runtime (`No such file or directory`), so this learn pass used the
  runtime allocator endpoint `POST /api/adrs/next`, which returned
  `{"adr_id":364}`. The prefix `0364` was checked free before writing.
- Delivery PR #3372 merged as commit
  `a198ea60eaad3df53795c80483e9d159d6ecdcf9`.
- Build-stage commit `cc9423797` added the static-block fallback source gate,
  aligned the proof manifest, and updated the burndown checklist.
- Build-stage verification recorded:
  - `rtk dotnet test tests/Asynkron.JsEngine.Tests --filter
    "FullyQualifiedName~UnifiedBytecodeProductionInvocationTests&FullyQualifiedName~ClassStaticBlockIrFallback"`
    passed.
  - focused `BytecodeProofManifestTests` manifest checks passed.
  - `rtk git diff --check` passed.

## Related

- `docs/rules/unified-bytecode-prototypes.md`
- `docs/plans/bytecode-burndown-checklist.md`
- `docs/plans/bytecode-proof-manifest.json`
- `src/Asynkron.JsEngine/Ast/ClassDefinitionExtensions.cs`
- `tests/Asynkron.JsEngine.Tests/UnifiedBytecodeProductionInvocationTests.cs`
- ADR 0346:
  `docs/adrs/0346-keep-script-ir-fallback-classified-with-production-decline-details.md`
