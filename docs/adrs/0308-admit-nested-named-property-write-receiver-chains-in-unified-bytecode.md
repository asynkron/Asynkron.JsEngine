# ADR 0308: Admit nested named property write receiver chains in unified bytecode

## Status

Accepted

## Context

PR #2897 widened the production unified-bytecode property-write boundary from
direct receiver writes such as `box.value = y` and `box.value++` to simple
nested named receiver chains such as:

```js
box.child.value = y
++box.child.count
```

The key distinction is that the nested receiver read is now VM-owned. The
compiler can lower the receiver chain through existing `GetNamedProperty`
opcodes, then finish with the existing `SetNamedProperty` or
`UpdateNamedProperty` opcode. No `ExpressionProgram`, `ExecutionPlanRunner`, or
AST fallback is needed.

The delivery deliberately kept nearby families declined:

- private names and super properties;
- optional chains;
- nested compound and logical assignment chains such as `box.child.value += y`
  or `box.child.value &&= y`;
- computed-expression key chains; and
- dynamic lookup or call-dependent receiver chains.

The implementation also exposed a stack-accounting detail: expression-program
`MaxStackDepth` can understate the compiled unified-bytecode stack needed when a
nested receiver must stay live for the final write/update. The compiler now
applies an explicit stack-depth floor for nested named property receiver chains.

## Decision

Admit simple nested named receiver assignments and updates to production unified
bytecode when all of these are true:

1. the root receiver is activation-resolved;
2. every intermediate receiver step is a non-optional, non-private named
   property read;
3. the final operation is a non-private `SetNamedProperty` or
   `UpdateNamedProperty`;
4. assignment RHS payloads remain simple production-owned operands or accepted
   simple template-literal spans; and
5. the route compiles entirely to owned unified-bytecode opcodes.

Retain the existing decline boundaries for nested compound/logical assignments,
computed-expression keys, optional chains, `super`, private names, calls, and
dynamic lookup until a later slice owns selector, compiler, VM, and route-proof
semantics for those shapes.

When lowering admitted nested receiver writes/updates, preserve the receiver on
the VM stack with existing `GetNamedProperty`, `SetNamedProperty`, and
`UpdateNamedProperty` semantics. If the expression-program stack metadata is too
low for the compiled unified-bytecode receiver preservation, raise the compiled
program stack-depth calculation rather than adding a generic expression-stack
fallback.

## Consequences

- Common nested named property assignments and updates can now use the
  production unified-bytecode fast path.
- The property-write lane remains an owned-opcode route, not a broader license
  for mixed expression-program execution inside the unified VM.
- Future property-write widening must update the eligibility recognizer,
  compiler lowering, expansion contract wording, owned-opcode proof pack, and
  adjacent negative declines in the same delivery slice.
- Stack-depth proof is part of the route contract for receiver-preserving
  lowering. Future nested receiver shapes should include a compiled
  `MaxStackDepth` assertion when preserving intermediate receivers changes stack
  pressure relative to `ExpressionProgram.MaxStackDepth`.

## Evidence

- Delivery PR #2897 merged as commit `e6952fb9`.
- The original delivery commit was `0c1f982e` on branch
  `agent-go/task-planitem-planmanual1780240661926543000-burn-down-unified-byte-b4990445aa`.
- Changed production surfaces:
  - `UnifiedBytecodeProductionEligibility.TryIsFirstBoundaryNestedNamedPropertyWriteCandidate`
  - `UnifiedBytecodeProductionEligibility.TryIsFirstBoundaryNestedNamedPropertyUpdateCandidate`
  - `UnifiedBytecodeCompiler` nested named receiver lowering and compiled stack-depth floor
  - `docs/unified-bytecode-expansion-contract.md`
- Focused verification from the delivery:
  - property-write runtime tests passed;
  - nested named property eligibility tests passed;
  - accepted property write/update owned-opcode subset tests passed;
  - logical-assignment regression tests passed; and
  - `rtk git diff --check` passed.

## Issue / PR

Issue
`planitem-planmanual1780240661926543000-burn-down-unified-bytecode-production-decl-76ae54eb13`
/ PR #2897.

## Related

- `docs/adrs/0238-keep-unified-bytecode-compound-property-writes-get-for-set-owned.md`
- `docs/adrs/0302-admit-named-member-logical-assignment-in-unified-bytecode.md`
- `docs/unified-bytecode-expansion-contract.md`
- `docs/rules/unified-bytecode-prototypes.md`
