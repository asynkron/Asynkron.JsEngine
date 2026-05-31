# ADR 0302: Admit named member logical assignment in unified bytecode

## Status

Accepted

## Context

ADR 0300 admitted logical compound assignment only for slot identifiers
(`x &&= y`, `x ||= y`, `x ??= y`) and kept member logical assignments
(`this.x &&= y`, `box.value ||= y`) declined. That was a conservative boundary:
the slot form needs only a condition copy, a branch, and a slot store, while the
member form must preserve the receiver for a later property write and still
produce the assignment expression result.

PR #2826 widened the production unified-bytecode boundary for the direct named
member shape. The expression bytecode form is exact and activation-resolved:

```
base
DuplicateTop
GetNamedProperty
JumpIfFalse | JumpIfTrue | JumpIfNotNullish
Pop
rhs
SetNamedProperty
DuplicateTop
SwapTopTwo
Pop
```

The direct named member shape was already close to the compound property-write
model from ADR 0238: `GetNamedPropertyForCompoundSet` can preserve the receiver
needed by `SetNamedProperty`. What was missing was a VM-owned stack shuffle for
the short-circuit cleanup path after assignment result duplication.

## Decision

Admit direct named member logical assignments to production unified bytecode
when all of these are true:

- The base is activation-resolved (`this`, parameter/local slot, or other
  already admitted activation value).
- The target is a non-optional, non-private named property.
- The read and write property names match.
- The jump target is the exact cleanup start for the lowered logical assignment
  shape.
- The RHS is a simple production-owned operand.

Lower the accepted shape with existing property and short-circuit ownership plus
one new stack opcode:

```
[base load]
GetNamedPropertyForCompoundSet(name)
JumpIfShortCircuitX(cleanup)
Pop
[rhs load]
SetNamedProperty(name)
Jump(end)
cleanup:
SwapTopTwo
Pop
end:
```

`GetNamedPropertyForCompoundSet` keeps the receiver live for the eventual
`SetNamedProperty`. The short-circuit jump keeps peek semantics: when the
logical assignment short-circuits, the current property value remains available
as the expression result. On the proceeding path, `SetNamedProperty` leaves the
assigned value on the stack as the result. The cleanup path uses `SwapTopTwo`
then `Pop` to discard the preserved receiver while keeping the short-circuit
result value.

`SwapTopTwo` is VM-owned in both the ordinary and resumable unified-bytecode
interpreters, listed in the expansion contract opcode inventory, and explicitly
allowed by production eligibility. It is not a license to add a generic
expression-stack interpreter: the selector and compiler still match only this
exact direct named member logical-assignment shape.

## Consequences

- Direct named member logical assignments (`box.value &&= y`, `box.value ||= y`,
  `box.value ??= y`, and the corresponding `this.value` forms) now route through
  the production unified-bytecode fast path.
- ADR 0300's retained-decline statement for member logical assignment is
  superseded only for direct named member targets. Slot logical assignment keeps
  the ADR 0300 statement-level stack contract.
- Computed member logical assignment, optional chains, deeper member chains,
  private fields, `super`, destructuring, dynamic lookup, and complex RHS/key
  payloads remain pre-VM declines until a slice owns their selector, compiler,
  VM, and route-proof behavior.
- Any future stack-shuffle opcode added for expression-program lowering must be
  added to the VM, production eligibility allowlist, expansion-contract opcode
  inventory, and focused proof pack in the same delivery slice. PR #2826's
  build-back repair was required because `SwapTopTwo` support initially left the
  contract inventory stale.

## Evidence

- Delivery PR #2826 merged as commit `911cfde3`.
- Changed production surfaces:
  - `UnifiedBytecodeCompiler.TryAppendFirstBoundaryNamedLogicalPropertySet`
  - `UnifiedBytecodeProductionEligibility.TryIsFirstBoundaryNamedLogicalPropertyWriteCandidate`
  - `UnifiedBytecodeOpCode.SwapTopTwo`
  - `UnifiedBytecodeVirtualMachine` ordinary and resumable `SwapTopTwo` handlers
- Focused build-back verification passed:
  `rtk dotnet test tests/Asynkron.JsEngine.Tests/Asynkron.JsEngine.Tests.csproj -c Release --filter "FullyQualifiedName~UnsupportedPropertyReadAdjacentFamilies_DeclineUnifiedBytecodeAndFallBack|FullyQualifiedName~UnifiedBytecodeExpansionContract_ListsRequiredHeadingsAndCurrentEnums"`
  with 8 tests passing.

## Issue / PR

Issue `planitem-planmanual1780198120145433000-widen-unified-bytecode-production-conditio-f31b87b5d8`
/ PR #2826.

## Related

- `docs/adrs/0238-keep-unified-bytecode-compound-property-writes-get-for-set-owned.md`
- `docs/adrs/0293-admit-logical-and-nullish-expressions-in-unified-bytecode-with-peek-jump-semantics.md`
- `docs/adrs/0300-admit-logical-compound-assignment-on-slots-in-unified-bytecode.md`
- `docs/unified-bytecode-expansion-contract.md`
- `docs/rules/unified-bytecode-prototypes.md`
