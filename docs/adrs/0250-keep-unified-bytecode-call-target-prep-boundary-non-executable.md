# ADR 0250: Keep unified bytecode call-target prep boundary non-executable

## Status

Accepted

Superseded in part on 2026-05-28 for the first executable no-spread
activation-resolved identifier-call slice, the direct named member-call slice
from issue #2530 / PR #2534, the direct computed member-call slice from issue
#2531 / PR #2535, and the direct receiver-aware named/computed member-call
slice. The original decline-first decision still applies to direct eval, spread
calls, construct/super calls, optional calls, arguments-object dependencies,
dynamic lookup, private/super member targets, complex computed keys, and other
unproven call-adjacent families.

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
   production eligibility must decline at the invocation boundary for any call
   family not explicitly admitted by a later executable call slice.
3. The VM must not satisfy these opcodes by delegating to `ExpressionProgram`,
   `ExecutionPlanRunner`, AST evaluation, or host-call fallback.
4. For the unsuperseded part of this boundary, direct eval, spread calls,
   construct/super calls, optional calls, arguments object dependencies,
   dynamic lookup, private/super member targets, complex computed keys, and
   unproven receiver/key shapes stay outside the production route until selector,
   compiler, VM, and public route proof move together.
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

## 2026-05-28 executable identifier-call update

Issue #2495 narrows the former non-executable boundary by allowing only
activation-slot identifier calls with no spread and simple literal/slot
arguments to execute in `UnifiedBytecodeVirtualMachine`.
`PrepareIdentifierCallTarget` now loads the receiver/callee pair from
bytecode-owned call-target metadata and `CallInvocationBoundary` invokes the
callable through existing invocation helpers.

The executable slice also preserves the caller environment boundary for
environment-aware and debug-aware callables. Production invocation creates the
simple activation `JsEnvironment` only when a compiled program contains
`CallInvocationBoundary`, passes it into the VM, and the VM forwards the active
environment plus `EvaluationContext` to the shared callable helpers. When the
accepted bytecode enters a block lexical scope before the call, the VM tracks
slot environment ownership so debug-aware callees observe the active lexical
scope chain.

This update did not make member, computed, eval, spread, construct/super,
optional, arguments-dependent, or dynamic lookup calls production-eligible.
The later receiver-aware member-call slice admits direct named/computed member
calls only; eval, spread, construct/super, optional, arguments-dependent,
dynamic lookup, and broader call-adjacent families must still decline before VM
execution instead of falling back inside the VM.

The friction point that made this explicit was the PR #2501 review: the first
implementation passed ordinary identifier-call tests but failed a
parameter-passed `__debug` probe with missing environment/context. The repair
kept the no-mixed-execution rule intact while adding regression proof for
parameter-passed and block-scoped debug-aware calls.

## 2026-05-28 executable member-call update

Issue #2530 / PR #2534 narrows the former member-call decline by allowing direct
named member calls with activation-resolved receiver chains and simple
literal/slot arguments to execute in `UnifiedBytecodeVirtualMachine`.
The later receiver-aware member-call slice extends that boundary to direct
computed member calls with activation-resolved receiver chains and simple
literal/slot arguments.

`PrepareNamedCallTarget` and `PrepareComputedCallTarget` now load the callee
from the receiver while leaving that receiver on the stack as the call `this`
value for `CallInvocationBoundary`.

This update does not make direct eval, spread calls, construct/super calls,
optional calls, private/super member targets, arguments-dependent calls,
dynamic lookup, or unproven receiver shapes production-eligible. Those families
must still decline before VM execution instead of falling back inside the VM.

## 2026-05-28 executable computed member-call update

Issue #2531 / PR #2535 narrows the former computed member-call decline by
allowing direct computed member calls with activation-resolved receiver chains,
simple literal/slot computed keys, and simple literal/slot arguments to execute
in `UnifiedBytecodeVirtualMachine`. `PrepareComputedCallTarget` now consumes the
computed key, loads the callee from the receiver through the context-aware
property lookup path, and leaves that receiver on the stack as the call `this`
value for `CallInvocationBoundary`.

This update does not make direct eval, spread calls, construct/super calls,
optional calls, private/super member targets, arguments-dependent calls,
dynamic lookup, complex computed keys, or unproven receiver/key shapes
production-eligible. Those families must still decline before VM execution
instead of falling back inside the VM.

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
- ADR 0262: `docs/adrs/0262-keep-unified-bytecode-named-member-call-receiver-owned.md`
- ADR 0263: `docs/adrs/0263-keep-unified-bytecode-computed-member-call-key-and-receiver-owned.md`
