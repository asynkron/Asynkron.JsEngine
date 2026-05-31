# ADR 0299: Admit optional call-chain forms in unified bytecode via jump-based lowering

## Status

Accepted

## Context

Faktorial issue `gh2806` and PR #2806 extend the production unified-bytecode VM
to admit two optional call-chain forms that were previously declined with
`OptionalChainDependency`:

- `a?.b.c()` — optional-start chain, plain non-optional call
- `a?.b?.c()` — double-optional chain, receiver-optional call

ADR 0289 admitted simple receiver-optional (`box?.read()`) and callee-optional
(`box.read?.()`) call forms. ADR 0298 admitted multi-hop optional named property
read chains (`a?.b.c`, `a?.b?.c`) via a jump-based lowering that avoids a
sentinel-value propagation scheme. This ADR extends the call-target compiler path
to reuse the same jump-based lowering for the two new call-chain forms.

The form `a?.b()` (single-hop optional start into an immediate call) was already
admitted by ADR 0289 Case 1 as `box?.read()` — the receiver chain is just the
activation-resolved base.

## Decision

### Expression program IR

The two new forms produce distinct expression program patterns:

**`a?.b.c()`** (Case 4):
```
[base(0), GetNamedProperty(IsOptional:true, b)(1), JumpIfShortCircuited(2),
 LoadNamedCallTarget(c)(3), args..., Call]
```
`JumpIfShortCircuited` is emitted because `HasOptionalChaining(member.Target)` is
true for the base; the `.c` member itself is not optional.

**`a?.b?.c()`** (Case 5):
```
[base(0), GetNamedProperty(IsOptional:true, b)(1), JumpIfShortCircuited(2),
 JumpIfNullish(ReplaceWithUndefined:true)(3), LoadNamedCallTarget(c)(4), args..., Call]
```
The extra `JumpIfNullish` at [3] comes from `member.IsOptional=true` on the `.c`
member expression (the `?.c` hop).

### Eligibility gate

Two new private candidate recognizers are added to
`UnifiedBytecodeProductionEligibility`:

- `TryIsFirstBoundaryOptionalChainPlainCallCandidate` — matches the Case 4 pattern
  (`LoadNamedCallTarget` at index 3, preceded by `JumpIfShortCircuited`, preceded by
  `GetNamedProperty(IsOptional:true)`, preceded by activation-resolved base).
- `TryIsFirstBoundaryOptionalChainReceiverOptionalCallCandidate` — matches the Case 5
  pattern (`LoadNamedCallTarget` at index 4, preceded by `JumpIfNullish(RWU)`, preceded
  by `JumpIfShortCircuited`, preceded by `GetNamedProperty(IsOptional:true)`, preceded
  by activation-resolved base).

Both are wired into `TryIsFirstBoundaryCallTargetPreparationCandidate` alongside the
existing Cases 1–3.

The `GetNamedProperty(IsOptional:true)` op at index 1 was previously declined unless it
matched a property-read candidate. Now, when `isCallTargetPreparationCandidate` is true
(set by the new candidates), the `IsOptional` branch in `TryFindExpressionDecline`
admits the op via the existing `if (isCallTargetPreparationCandidate) break;` escape
added at the end of that branch.

### Lowering — reuse `JumpIfNullishReplaceUndefined` and `PrepareNamedCallTarget`/`PrepareNamedOptionalCallTarget`

**Case 4 (`a?.b.c()`)** lowers to:
```
LoadSlot(base)
JumpIfNullishReplaceUndefined(end)   ← short-circuits when a is null/undefined
GetNamedProperty(b)                  ← receiver for c()
PrepareNamedCallTarget(c)            ← loads callee, receiver stays on stack
args...
CallInvocationBoundary
end:
```

`JumpIfNullishReplaceUndefined` is backpatched to point past the
`CallInvocationBoundary` once all arguments have been emitted. No new VM opcode
is required.

**Case 5 (`a?.b?.c()`)** lowers to:
```
LoadSlot(base)
JumpIfNullishReplaceUndefined(end)   ← short-circuits when a is null/undefined
GetNamedProperty(b)                  ← gives a?.b on stack
PrepareNamedOptionalCallTarget(c, end)  ← short-circuits when a.b is null/undefined
args...
CallInvocationBoundary
end:
```

Both `JumpIfNullishReplaceUndefined` and `PrepareNamedOptionalCallTarget` target
the same `end` PC, which is backpatched to the instruction count after the call.
`PrepareNamedOptionalCallTarget` is emitted with `IsOptionalReceiverCheck: true`,
matching the existing Case 1 pattern.

### Invariants preserved

- Both forms require an activation-resolved base at op[0] (same as all other
  admitted call-chain forms).
- The `GetNamedProperty(IsOptional:true)` must be non-private and have
  `!ShortCircuitOnNullishTarget`.
- The `LoadNamedCallTarget` must be non-private.
- Call arguments must be simple (same admissibility rules as other call-target forms).
- No new VM opcodes are needed.
- The callee-optional trailing structure (`Call, Jump, SwapTopTwo, Pop`) does NOT
  appear in these patterns; the new candidates correctly exclude it.

## Consequences

- `a?.b.c(args)` and `a?.b?.c(args)` now route through the production VM,
  gaining the same throughput benefit as other admitted call-chain forms.
- The gate for `a.x?.b.c()` (non-activation-resolved base) correctly remains
  declined with `OptionalChainDependency` (AC-4).
- Previously-admitted optional call patterns (Cases 1–3) are unaffected.
- Lineage: ADR 0289 (Cases 1–3), ADR 0296 (simple optional reads),
  ADR 0298 (multi-hop optional property read chains), gh2806.
