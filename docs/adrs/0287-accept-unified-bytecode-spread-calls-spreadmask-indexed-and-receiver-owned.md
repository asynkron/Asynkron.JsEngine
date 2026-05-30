# ADR 0287: Accept unified bytecode spread calls, spread-mask indexed and receiver-owned

## Status

Accepted

## Context

Issue #2676 widened production unified-bytecode routing to admit **synchronous
spread calls** — `f(...args)`, `f(...a, ...b)`, `obj.method(...args)`, and
mixed `f(a, ...b, c)` — as the highest-impact unsupported call bucket per the
[expansion contract](../unified-bytecode-expansion-contract.md) "Ranked Next
Unsupported Buckets" #1.

Before this slice, the `Call` eligibility arm declined any call whose
`SpreadMaskConstantIndex >= 0`, and the boundary-candidate pre-check declined
spread-masked calls before the per-op analysis. The existing accepted call
boundary was: no-spread activation-resolved identifier calls, direct named
member calls with optional-free activation-resolved receiver chains, and shallow
computed-member calls with simple literal/slot args.

Spread calls are distinct from the already-admitted no-spread shapes in two
ways:

1. **Argument flattening**: positional and spread arguments are pushed as
   individual value-producing loads onto the expression stack; a spread position
   marks slots that must be iterated at runtime rather than passed directly. The
   sequence of loads is left-to-right and fully evaluated before the call
   boundary.
2. **Spread-mask encoding**: the existing `ExpressionProgram.SpreadMaskConstants`
   surface (`ImmutableArray<ImmutableArray<int>>`) already records which argument
   positions carry spread elements. The `SpreadMaskConstantIndex` field on the
   expression-op `Call` node is the index into that table; `>= 0` means at least
   one spread argument is present. This surface predated this ADR.

The observable behaviors that must be preserved exactly:

- **Evaluation order**: all argument expressions (positional and spread) are
  evaluated left-to-right before the callee is invoked; no argument is evaluated
  after spread iteration begins.
- **Receiver binding**: for member calls the final resolved receiver remains the
  call `this` value (identical to the no-spread member-call contract in ADRs
  0262/0263/0264/0275).
- **Spread iteration protocol**: each spread position is iterated using the
  engine's existing `TypedAstEvaluator.EnumerateSpread` helper, which preserves
  `Symbol.iterator` semantics, side effects, and iterator-close semantics
  identical to the AST/ExecutionPlanRunner path. The VM does not inline or
  partially-inline the spread; it delegates to the shared helper.
- **Interleaved spreads**: `f(a, ...b, c, ...d)` pushes all argument loads in
  source order and the spread mask records which positions are spread; the VM
  applies the mask left-to-right to produce the final flattened argument list.

## Decision

Admit the **proven spread-call sub-shape** through the existing
`CallInvocationBoundary` opcode with a spread-mask extension.

- The `CallInvocationBoundary` operand is extended: the **low 16 bits** hold the
  pushed argument value count; the **high bits** hold a spread-mask reference
  (`spreadMaskIndex + 1`, where `0` means no spread). The operand encoding is
  owned by `UnifiedBytecodeProgram` and packed by `UnifiedBytecodeCompiler`.
- The compiler threads `SpreadMaskConstantIndex` from the expression program
  through the compiled program descriptor so the VM can recover the spread-mask
  array at the call boundary.
- The VM's `CallInvocationBoundary` handler detects a non-zero high-word as a
  spread-mask reference, flattens spread positions via
  `TypedAstEvaluator.EnumerateSpread` left-to-right, then invokes through the
  existing callable helpers with receiver-as-`this`.
- No new opcode or IR instruction is introduced; spread is encoded into the
  existing call-boundary operand surface.
- The eligibility predicate now admits spread calls only when (a) the spread mask
  is present and (b) all argument ops lower to supported expression bytecode.
  The boundary-candidate check was updated in parallel.

Keep all adjacent declines in place:

- **Optional calls** (`f?.()`, `obj.m?.()`, optional-chain call targets) still
  decline with `OptionalChainDependency` at the call-target prep arm.
- **Construct and super** (`new F(...)`, `super(...)`, `LoadNamedSuperCallTarget`,
  `LoadComputedSuperCallTarget`) still decline at `:692-699`.
- **Direct eval** still declines via the existing direct-eval guard.
- **Spread onto optional or construct targets** still declines as a compound of
  the above guards.
- Out-of-scope optional property reads, super writes, and super access decline
  independently and are not affected by this change.

Do not satisfy spread calls by calling back into `ExpressionProgram`,
`ExecutionPlanRunner`, or AST evaluation. The no-mixed-execution rule applies
in full.

## Consequences

- `f(...args)`, `f(...a, ...b)`, `obj.method(...args)`, and mixed
  `f(a, ...b, c)` execute through `UnifiedBytecodeVirtualMachine` without
  falling back to the AST/IR path.
- The `CallInvocationBoundary` operand is now a two-field packed integer. The
  compiler and VM are the sole owners of this encoding; no other surface
  encodes or decodes it.
- Spread iteration side-effects are identical to the AST/IR path because both
  delegate to the same `TypedAstEvaluator.EnumerateSpread` helper.
- The admit/decline boundary stays auditable: optional calls, construct/super,
  and direct eval keep their existing decline codes and are not affected.
- Future widening (optional calls — Batch 2, construct/super — Batch 3) must
  own its full stack — eligibility, compiler, VM, proof pack, and contract
  update — before removing the corresponding decline.

## Evidence

- Implementation commit: `1ddce3fe` on `agent-go/task-gh2676`.
- Focused proof passed:
  `dotnet test tests/Asynkron.JsEngine.Tests --filter "FullyQualifiedName~UnifiedBytecodeProductionEligibilityTests"`
  and `~UnifiedBytecodeProductionInvocationTests` (335 tests passed).
- New `UnifiedBytecodeProductionSpreadCallTests.cs` (7/7) covers:
  evaluation-order preservation, receiver-as-`this`, spread iterator
  side-effects, multi-spread interleaving, and negative-fallback proofs for
  optional/construct/super/eval.
- Engine build clean: `dotnet build -c Debug` → 0 errors, 0 warnings.

## Related

- [docs/unified-bytecode-expansion-contract.md](../unified-bytecode-expansion-contract.md) — Production Call Invocation Boundary section updated for gh2676
- Issue #2676
- ADR 0250: `docs/adrs/0250-keep-unified-bytecode-call-target-prep-boundary-non-executable.md`
- ADR 0261: `docs/adrs/0261-keep-unified-bytecode-call-invocation-boundary-plan-sliced-and-deferred.md`
- ADR 0262: `docs/adrs/0262-keep-unified-bytecode-named-member-call-receiver-owned.md`
- ADR 0263: `docs/adrs/0263-keep-unified-bytecode-computed-member-call-key-and-receiver-owned.md`
- ADR 0264: `docs/adrs/0264-keep-unified-bytecode-member-call-final-receiver-owned.md`
- ADR 0275: `docs/adrs/0275-keep-unified-bytecode-named-chains-owned-and-computed-receiver-boundary-shallow.md`
- `src/Asynkron.JsEngine/Execution/UnifiedBytecode/UnifiedBytecodeProductionEligibility.cs`
- `src/Asynkron.JsEngine/Execution/UnifiedBytecode/UnifiedBytecodeCompiler.cs`
- `src/Asynkron.JsEngine/Execution/UnifiedBytecode/UnifiedBytecodeVirtualMachine.cs`
- `src/Asynkron.JsEngine/Execution/UnifiedBytecode/UnifiedBytecodeProgram.cs`
- `tests/Asynkron.JsEngine.Tests/UnifiedBytecodeProductionSpreadCallTests.cs`
