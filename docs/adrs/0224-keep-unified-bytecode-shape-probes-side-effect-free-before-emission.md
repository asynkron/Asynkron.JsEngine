# ADR 0224: Keep unified bytecode shape probes side-effect-free before emission

## Status

Accepted

## Context

Issue #2314 and PR #2320 widened production unified-bytecode property reads to
accept exactly `box.child.value` as a two-hop direct named read. ADR 0222 owns
that accepted boundary.

During build verification, the first implementation exposed a stack-corruption
regression in the named-chain compiler helper. The helper recognized part of
the candidate, emitted some unified instructions, then returned `false` for an
out-of-boundary neighbor. The caller then continued into the generic expression
compiler with stale partially emitted instructions still in the builders.

This is not specific to named property reads. Any helper that both probes
`ExpressionProgram` shape and appends unified instructions can create the same
failure mode if a non-match leaves `unified`, literal constants, or string
constants mutated.

## Decision

Unified-bytecode expression shape helpers must be all-or-nothing at the builder
boundary:

- Validate the full accepted operation sequence before appending to shared
  builders, or append to local scratch builders and commit only after the shape
  has been accepted.
- Returning `false` with an empty reason must leave the shared instruction and
  constant builders unchanged so the next helper or generic compiler path sees
  a clean stack contract.
- Returning `false` with a non-empty reason may decline the shape, but it must
  still avoid partial shared-builder mutation before the decline.
- Adjacent unsupported examples should be tested near accepted examples so
  partial-emission stack drift is caught by route/prototype proof packs.

## Consequences

- Unsupported neighboring shapes remain clean declines instead of partially
  compiled programs.
- Compiler fallback order can stay layered without defensive builder snapshot
  logic at every call site.
- Future unified-bytecode widening work has a reusable guardrail for helpers
  that combine shape recognition with emission.
- Helpers that must call lower-level append routines before full acceptance
  should stage `unified`, literal, and string builders in scratch copies and
  replace the shared builders only after every operand and branch target has
  been accepted.

## Evidence

- The gh2314 build log recorded focused test failures, then identified "a
  stack-corruption regression from partial emission in the new named-chain
  compiler helper" before applying a prevalidation fix.
- `src/Asynkron.JsEngine/Execution/UnifiedBytecode/UnifiedBytecodeCompiler.cs`
  now validates every named-chain property operation before appending
  `LoadSlot` and `GetNamedProperty` instructions.
- The focused proof pack passed after the fix:
  `rtk dotnet test tests/Asynkron.JsEngine.Tests --filter "FullyQualifiedName~UnifiedBytecodePrototypeTests|FullyQualifiedName~UnifiedBytecodeProductionEligibilityTests|FullyQualifiedName~UnifiedBytecodeProductionInvocationTests" -- xUnit.MaxParallelThreads=1 -timeout 20000`.
- Issue `planitem-planmanual1780198120145433000-widen-unified-bytecode-production-conditio-0aa2351edc`
  / PR #2812 found the same partial-emission risk in
  `TryAppendFirstBoundaryNamedLogicalPropertySet`: the accepted direct named
  logical-assignment route needed to try an activation-base append before
  proving the simple RHS. The build-back fix staged the unified instruction,
  literal, and string builders and committed them atomically only after the
  whole shape was accepted.

## Related

- Issue #2314
- PR #2320
- Issue `planitem-planmanual1780198120145433000-widen-unified-bytecode-production-conditio-0aa2351edc`
- PR #2812
- ADR 0181: `docs/adrs/0181-keep-unified-bytecode-prototype-ir-owned-and-all-or-nothing.md`
- ADR 0218: `docs/adrs/0218-keep-unified-bytecode-property-read-production-boundary-selector-owned.md`
- ADR 0221: `docs/adrs/0221-keep-unified-bytecode-property-reads-vm-owned-and-observable.md`
- ADR 0222: `docs/adrs/0222-keep-unified-bytecode-two-hop-named-property-read-boundary-owned.md`
- `docs/rules/unified-bytecode-prototypes.md`
- `src/Asynkron.JsEngine/Execution/UnifiedBytecode/UnifiedBytecodeCompiler.cs`
