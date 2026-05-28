# ADR 0258: Keep unified bytecode completed lanes integrated at production boundary

## Status

Accepted

## Context

Faktorial issue
`planitem-planmanual1779943568009120000-batch-1-shared-bytecode-surface-and-parall-fc03ae9db9`
and PR #2503 closed the Batch 4 production-routing integration slice for the
parallel unified-bytecode widening plan.

The preceding lanes had already admitted or bounded separate families:
literal construction, activation-value loads, primitive operators, call-target
preparation as a non-executable boundary, iterator/destructuring model-first
declines, completion behavior, loop-control targets, property reads/writes, and
block lexical scopes. Each lane had focused positive and negative proof, but
the batch still needed a single accepted sync function proving those completed
lanes compose as one production `UnifiedBytecodeProgram`.

There were two risks to close. First, per-lane tests could pass while an
ordinary mixed function accidentally hit a non-executable opcode, drifted into a
mixed `ExpressionProgram` / `ExecutionPlanRunner` fallback, or depended on a
reserved adjacent family. Second, the broader unified route could shadow older
specialized sync-call fast paths that remain faster for simple binary return
shapes.

## Decision

Keep the completed-lane production boundary integrated and route-priority
explicit.

- After parallel unified-bytecode lanes complete, prove at least one ordinary
  sync function that combines only already-owned production families and is
  accepted by `UnifiedBytecodeProductionEligibility` as a single program.
- The integrated selector proof must assert `None` decline code, require the
  expected owned opcodes, and assert absence of non-executable call-target
  preparation or invocation-boundary opcodes.
- The matching public invocation proof must execute the same integrated
  function through `SyncFunctionInvoker`, produce the expected JavaScript
  result, and log `unified-bytecode-production-fast-path` for that function.
- Existing specialized simple-return binary and binary-chain fast paths remain
  ahead of the broader unified bytecode route. Tests should assert that those
  functions do not log the unified route when the specialized route owns them.
- Do not make an integrated proof pass by adding VM fallback, calling
  `ExpressionProgram`, `ExecutionPlanRunner`, or AST evaluators, or broadening
  unowned adjacent families. If the proof needs an unowned shape, keep that
  shape out of the integrated accepted function or open a separate ownership
  slice.
- Keep the sync invocation bridge allocation shape stable: eligibility stays
  cached per plan, slot storage continues through the existing pooled bridge,
  and the `forloop --memory` profile remains part of the proof loop.

## Consequences

- A batch of independently proven lanes is not considered production-coherent
  until at least one integrated accepted function proves they compose inside
  the same VM-owned program.
- The unified bytecode route remains a deliberately bounded route, not a
  catch-all behind source syntax. Unsupported neighboring families still need
  explicit pre-VM declines or public no-route proof.
- Route ordering stays part of the production contract. Broader eligibility
  cannot silently steal work from older specialized fast paths.
- No expansion-contract update was required for PR #2503 because it added guard
  tests only and did not change opcode, decline-code, compiler, VM, selector, or
  proof-command inventory. Future runtime-surface changes still must update
  `docs/unified-bytecode-expansion-contract.md` in the same delivery slice.

## Evidence

- PR #2503 merged as commit
  `ca10e85c29c4b12eee8890de4d63586485917241`.
- Build-stage update recorded delivery commit
  `c0077ee6 Guard unified bytecode production boundary`.
- Focused integrated proof passed:
  `rtk dotnet test tests/Asynkron.JsEngine.Tests --filter "FullyQualifiedName~Evaluate_IntegratedCompletedLaneProgram_AcceptsAsOneExecutableProgram|FullyQualifiedName~IntegratedCompletedLaneProgram_UsesUnifiedBytecodeProductionFastPath|FullyQualifiedName~BinaryChainReturnFunction_KeepsExistingSpecializedFastPath"`
  with 3 tests.
- Focused production proof pack passed:
  `rtk dotnet test tests/Asynkron.JsEngine.Tests --filter "FullyQualifiedName~UnifiedBytecodeProductionEligibilityTests|FullyQualifiedName~UnifiedBytecodeProductionInvocationTests"`
  with 189 tests.
- AST-eval seam scan found no remaining runner seams:
  `rtk rg "EvaluateExpression\\(|ProfileEvaluateExpression\\(" src/Asynkron.JsEngine/Ast/TypedAstEvaluator.ExecutionPlanRunner*`.
- `rtk ./tools/profile forloop --memory` passed with final total allocated
  6.74 MB, and `rtk git diff --check` passed.
- Review-stage summary found no blocking issues and confirmed adjacent explicit
  declines were already covered.

## Related

- ADR 0201: `docs/adrs/0201-keep-unified-bytecode-production-routing-decline-first.md`
- ADR 0204: `docs/adrs/0204-keep-unified-bytecode-sync-production-routing-slot-bridged.md`
- ADR 0246: `docs/adrs/0246-keep-unified-bytecode-expansion-contract-source-of-truth-and-drift-guarded.md`
- ADR 0250: `docs/adrs/0250-keep-unified-bytecode-call-target-prep-boundary-non-executable.md`
- ADR 0251: `docs/adrs/0251-keep-unified-bytecode-iterator-and-destructuring-drivers-model-first.md`
- ADR 0252: `docs/adrs/0252-keep-unified-bytecode-completion-lane-vm-owned.md`
- ADR 0253: `docs/adrs/0253-keep-unified-bytecode-loop-control-targets-compiler-owned.md`
- ADR 0255: `docs/adrs/0255-keep-unified-bytecode-block-lexical-scopes-program-slot-owned.md`
- `docs/unified-bytecode-expansion-contract.md`
- `.claude/rules/unified-bytecode-prototypes.md`
- `tests/Asynkron.JsEngine.Tests/UnifiedBytecodeProductionEligibilityTests.cs`
- `tests/Asynkron.JsEngine.Tests/UnifiedBytecodeProductionInvocationTests.cs`
