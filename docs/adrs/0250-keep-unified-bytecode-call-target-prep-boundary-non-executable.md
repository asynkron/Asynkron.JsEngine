# ADR 0250: Keep unified bytecode call-target prep boundary non-executable

## Status

Accepted

## Context

Faktorial issue
`planitem-planmanual1779943568009120000-batch-1-shared-bytecode-surface-and-parall-161f73f52d`
and PR #2479 added a broad unified-bytecode call-target preparation lane. The
slice made direct identifier calls, named member calls, and computed member
calls visible to the unified compiler through `UnifiedBytecodeCallTarget`
records and `PrepareIdentifierCallTarget`, `PrepareNamedCallTarget`, and
`PrepareComputedCallTarget` opcodes.

The lane intentionally did not make unified bytecode execute calls. JavaScript
call invocation carries observable receiver binding, direct-eval semantics,
construct/super behavior, optional-chain behavior, and spread argument ordering.
Those semantics are already split carefully in expression bytecode, but they
were not proven for production unified bytecode in this slice.

PR #2479 therefore added an explicit `CallInvocationBoundary` opcode and
`UnifiedBytecodeProductionDeclineCode.CallInvocationBoundary`. Production
eligibility can compile far enough to see the call-target preparation surface,
then must decline before VM invocation.

The learn pass also found that `docs/unified-bytecode-expansion-contract.md`
missed the new opcode and decline-code inventory, causing
`ExpressionProgramCoverageMapTests.UnifiedBytecodeExpansionContract_ListsRequiredHeadingsAndCurrentEnums`
to fail on the missing `PrepareIdentifierCallTarget` entry.

## Decision

Keep unified-bytecode call-target preparation as a typed, non-executable
boundary until a later slice proves full call invocation.

1. Unified bytecode may own call-target preparation records for no-spread
   activation-resolved identifier calls and direct member calls.
2. The compiler may emit preparation opcodes plus `CallInvocationBoundary`, but
   production eligibility must decline at the invocation boundary rather than
   route the program through the VM.
3. The VM must not satisfy these opcodes by delegating to `ExpressionProgram`,
   `ExecutionPlanRunner`, AST evaluation, or host-call fallback.
4. Direct eval, spread calls, construct/super calls, optional calls, arguments
   object dependencies, dynamic lookup, private names, and unproven receiver
   shapes stay outside the production route until selector, compiler, VM, and
   public route proof move together.
5. Any future slice that makes calls executable must preserve receiver binding
   and direct-eval classification explicitly, then update the expansion
   contract, positive route proof, nearby decline/no-route proof, AST-eval seam
   scan, and memory/profile stability evidence in the same delivery slice.

## Consequences

- Future call invocation work has a bytecode-owned target representation to
  build on without pretending calls are already production-executable.
- Production routing remains decline-first and fallback-free: accepted unified
  programs still execute only owned VM semantics.
- Unsupported call-adjacent shapes remain visible as pre-VM declines instead of
  disappearing behind a generic call or expression-program callback.
- The expansion contract is again aligned with the live enum surface, so the
  drift guard can catch the next opcode or decline-code inventory miss.

## Evidence

- PR #2479 merged commit
  `082ae1e31619aec84490d8be8ab72b50797cfff7`.
- Build-stage focused proof passed
  `rtk dotnet test tests/Asynkron.JsEngine.Tests --filter "FullyQualifiedName~UnifiedBytecodePrototypeTests|FullyQualifiedName~UnifiedBytecodeProductionEligibilityTests|FullyQualifiedName~UnifiedBytecodeProductionInvocationTests" -- xUnit.MaxParallelThreads=1 -timeout 20000`
  with 187 tests, and `rtk git diff --check` passed.
- Conflict-resolution repair kept both `SlotNames` and `CallTargetConstants`;
  `rtk dotnet build` and the focused unified-bytecode tests passed before PR
  #2479 merged.
- The learn-stage baseline drift guard failed before this ADR with
  `Assert.Contains() Failure` for missing
  `PrepareIdentifierCallTarget` in
  `docs/unified-bytecode-expansion-contract.md`.
- After the contract inventory update, the same drift guard passed:
  `rtk dotnet test tests/Asynkron.JsEngine.Tests --filter "FullyQualifiedName~ExpressionProgramCoverageMapTests&FullyQualifiedName~UnifiedBytecodeExpansionContract_ListsRequiredHeadingsAndCurrentEnums" -- xUnit.MaxParallelThreads=1 -timeout 20000`
  with 1 test.

## Related

- Issue
  `planitem-planmanual1779943568009120000-batch-1-shared-bytecode-surface-and-parall-161f73f52d`
- PR #2479
- `docs/unified-bytecode-expansion-contract.md`
- `.claude/rules/unified-bytecode-prototypes.md`
- ADR 0012: `docs/adrs/0012-keep-expression-bytecode-call-target-semantics-split.md`
- ADR 0181: `docs/adrs/0181-keep-unified-bytecode-prototype-ir-owned-and-all-or-nothing.md`
- ADR 0201: `docs/adrs/0201-keep-unified-bytecode-production-routing-decline-first.md`
- ADR 0224: `docs/adrs/0224-keep-unified-bytecode-shape-probes-side-effect-free-before-emission.md`
- ADR 0246: `docs/adrs/0246-keep-unified-bytecode-expansion-contract-source-of-truth-and-drift-guarded.md`
