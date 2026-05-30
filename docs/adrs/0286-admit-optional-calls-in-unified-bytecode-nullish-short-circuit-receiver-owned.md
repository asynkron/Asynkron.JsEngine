# ADR 0286: Admit optional calls in unified bytecode, nullish short-circuit receiver-owned

## Status

Accepted

## Context

Issue #2689 widened production unified-bytecode routing to admit **optional member calls** —
`box?.read(args)` (receiver-optional) and `box.read?.()` / `box[key]?.()` (callee-optional)
— as the next step in the "Ranked Next Unsupported Buckets" call-invocation widening from the
[expansion contract](../unified-bytecode-expansion-contract.md).

Before this slice, the boundary-candidate pre-check in
`TryFindExpressionDecline` declined any expression containing
`JumpIfNullish` or `JumpIfShortCircuited` when `isCallTargetPreparationCandidate`
was false, and the call-target-preparation arm declined optional call-target ops
(`IsOptional`, `ShortCircuitOnNullishTarget`) unconditionally.

The previously admitted call boundary covered:

- No-spread and spread identifier calls (`f(...)`, `f(...args)`)
- Direct named member calls — `box.read(args)`, receiver chain activation-resolved
- Shallow computed member calls — `box[key](args)`
- Synchronous spread calls (`f(...args)`, `obj.method(...args)`, multi-spread; gh2676)

Optional calls are distinct from the admitted shapes in two ways:

1. **Nullish short-circuit**: if the receiver (`box?.read()`) or the callee
   (`box.read?.()`, `box[key]?.()`) is `null` or `undefined`, the call is skipped
   and the expression yields `undefined`. This short-circuit must happen *before*
   argument evaluation in the callee-optional cases, and *before* method lookup
   in the receiver-optional case.
2. **Pattern structure**: the expression program encodes optional calls with one
   of three trailing structures:
   - Receiver-optional named: `[Receiver…, JumpIfNullish, LoadNamedCallTarget, args…, Call]`
   - Callee-optional named: `[Receiver…, LoadNamedCallTarget, JumpIfNullish, args…, Call, Jump, SwapTopTwo, Pop]`
   - Callee-optional computed: `[Receiver, Key, LoadComputedCallTarget, JumpIfNullish, args…, Call, Jump, SwapTopTwo, Pop]`

The observable behaviors that must be preserved exactly:

- **Nullish short-circuit ordering**: the nullish check happens before any argument
  expressions are evaluated and before the call boundary is crossed.
- **Receiver binding**: when the call proceeds, the final resolved receiver is the
  call `this` value — identical to the non-optional member-call contract (ADRs
  0262/0263/0264/0275).
- **Side-effect ordering**: receiver expressions are evaluated left-to-right before
  the nullish check; arguments are evaluated only when the call proceeds.

## Decision

Admit the **three proven optional-call sub-shapes** through two new opcodes:
`PrepareNamedOptionalCallTarget` and `PrepareComputedOptionalCallTarget`.

### Opcode encoding

Both opcodes pack two values into a single `int` operand:

- **Low 16 bits**: `callTargetConstantIndex` — index into
  `UnifiedBytecodeProgram.CallTargetConstants`.
- **High 16 bits**: `jumpTarget` — the absolute instruction PC to jump to when
  the nullish check short-circuits (the instruction immediately after
  `CallInvocationBoundary`).

### `UnifiedBytecodeCallTarget` extension

`IsOptionalReceiverCheck : bool` (default `false`) distinguishes the two named cases
under `PrepareNamedOptionalCallTarget`:

- `true` → receiver-optional (`box?.read()`) — peek receiver, check nullish, load method.
- `false` → callee-optional (`box.read?.()`) — load method, check nullish.

`PrepareComputedOptionalCallTarget` always uses the callee-optional protocol
(`box[key]?.()`) and does not need the flag.

### VM execution protocol

`PrepareNamedOptionalCallTarget` (receiver-optional, `IsOptionalReceiverCheck = true`):
1. Peek the receiver at `stack[sp - 1]`.
2. If nullish: replace the top of stack with `Undefined`; jump to `jumpTarget`.
3. Otherwise: push `GetNamedPropertyValue(receiver, name)` and fall through to `CallInvocationBoundary`.

`PrepareNamedOptionalCallTarget` (callee-optional, `IsOptionalReceiverCheck = false`):
1. Peek the receiver at `stack[sp - 1]`.
2. Fetch the named property into a local `callee`.
3. If `callee` is nullish: replace the top of stack with `Undefined`; jump to `jumpTarget`.
4. Otherwise: push `callee` and fall through to `CallInvocationBoundary`.

`PrepareComputedOptionalCallTarget`:
1. Pop the key from the top of stack.
2. Peek the receiver.
3. Fetch the computed property into a local `callee` via `GetComputedCallTargetValue`.
4. If `callee` is nullish: replace the top of stack with `Undefined`; jump to `jumpTarget`.
5. Otherwise: push `callee` and fall through to `CallInvocationBoundary`.

### Eligibility widening

`TryFindExpressionDecline` now allows `JumpIfNullish`/`JumpIfShortCircuited` when
`isCallTargetPreparationCandidate` is `true` (the call-target-preparation arm has
already admitted the pattern); the decline fires only for other usages outside
the admitted boundary.

`TryIsFirstBoundaryCallTargetPreparationCandidate` adds three new sub-predicates:
- `TryIsFirstBoundaryReceiverOptionalNamedCallCandidate` (Case 1)
- `TryIsFirstBoundaryCalleeOptionalNamedCallCandidate` (Case 2)
- `TryIsFirstBoundaryCalleeOptionalComputedCallCandidate` (Case 3)

### Remaining declines

- **Optional property reads** (`box?.value`, `box?.[key]`) still decline with
  `OptionalChainDependency`; they are not a call pattern.
- **Construct and super** (`new F(...)`, `super(...)`) still decline at the
  existing guards.
- **Direct eval** still declines via the existing direct-eval guard.
- **Spread-onto-optional**: spreads in optional calls (e.g. `box?.read(...args)`)
  are not admitted by this slice; they remain outside the boundary as a
  compound of the optional and spread shapes.
- **Private member targets**: private names in optional call chains still decline
  with `PrivateFieldDependency`.

Do not satisfy optional calls by calling back into `ExpressionProgram`,
`ExecutionPlanRunner`, or AST evaluation. The no-mixed-execution rule applies
in full.

## Consequences

- `box?.read(args)`, `box.read?.()`, `box.read?.(args)`, and `box[key]?.()` execute
  through `UnifiedBytecodeVirtualMachine` without falling back to the AST/IR path.
- The `CallTargetConstants` table gains an `IsOptionalReceiverCheck` field to
  distinguish receiver-optional and callee-optional named calls. The compiler and
  VM are the sole owners of this distinction.
- Nullish short-circuit semantics are byte-identical to the AST/IR path because
  both check `IsNullOrUndefined` before invoking and produce `Undefined` on skip.
- The admit/decline boundary stays auditable: optional property reads,
  construct/super, direct eval, and private-member targets keep their existing
  decline codes and are not affected.
- Future widening (optional chains across property reads — `box?.a?.b.read()`) must
  own its full stack — eligibility, compiler, VM, proof pack, and contract update —
  before removing the corresponding decline.

## Evidence

- Implementation commit on `agent-go/task-gh2689`.
- Eligibility proof: three new `InlineData` entries in
  `UnifiedBytecodeProductionEligibilityTests` (`invokeOptionalReceiver`,
  `invokeOptionalCallee`, `invokeOptionalComputedCallee`) expect
  `DeclineCode.None`.
- Invocation proof: six new tests in
  `UnifiedBytecodeProductionInvocationTests`:
  - `ReceiverOptionalNamedMemberCall_ShortCircuitsToUndefinedWhenReceiverNullish`
  - `ReceiverOptionalNamedMemberCall_InvokesMethodAndPreservesThisWhenReceiverNonNull`
  - `CalleeOptionalNamedMemberCall_ShortCircuitsToUndefinedWhenMethodNullish`
  - `CalleeOptionalNamedMemberCall_InvokesMethodAndPreservesThisWhenCalleeNonNull`
  - `CalleeOptionalComputedMemberCall_ShortCircuitsToUndefinedWhenMethodNullish`
  - `CalleeOptionalComputedMemberCall_InvokesMethodAndPreservesThisWhenCalleeNonNull`

## Related

- [docs/unified-bytecode-expansion-contract.md](../unified-bytecode-expansion-contract.md) — Production Call Invocation Boundary section updated for gh2689
- Issue #2689
- ADR 0262: `docs/adrs/0262-keep-unified-bytecode-named-member-call-receiver-owned.md`
- ADR 0263: `docs/adrs/0263-keep-unified-bytecode-computed-member-call-key-and-receiver-owned.md`
- ADR 0264: `docs/adrs/0264-keep-unified-bytecode-member-call-final-receiver-owned.md`
- ADR 0275: `docs/adrs/0275-keep-unified-bytecode-named-chains-owned-and-computed-receiver-boundary-shallow.md`
- ADR 0285: `docs/adrs/0285-accept-unified-bytecode-spread-calls-spreadmask-indexed-and-receiver-owned.md`
- `src/Asynkron.JsEngine/Execution/UnifiedBytecode/UnifiedBytecodeProductionEligibility.cs`
- `src/Asynkron.JsEngine/Execution/UnifiedBytecode/UnifiedBytecodeCompiler.cs`
- `src/Asynkron.JsEngine/Execution/UnifiedBytecode/UnifiedBytecodeVirtualMachine.cs`
- `src/Asynkron.JsEngine/Execution/UnifiedBytecode/UnifiedBytecodeProgram.cs`
- `tests/Asynkron.JsEngine.Tests/UnifiedBytecodeProductionEligibilityTests.cs`
- `tests/Asynkron.JsEngine.Tests/UnifiedBytecodeProductionInvocationTests.cs`
