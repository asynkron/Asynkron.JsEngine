# ADR 0317: Admit exact optional computed delete shapes in unified bytecode

## Status

Accepted

## Context

Issue `gh2934` / PR #2938 widened production unified-bytecode routing for
optional-chain delete expressions. Ordinary named/computed property deletes were
already admitted by ADR 0309, and nested named receiver computed deletes were
admitted by ADR 0316. Optional-chain deletes still declined as
`OptionalChainDependency` because delete has a distinct short-circuit result:
when the optional receiver is nullish, the expression returns `true` and must not
evaluate the computed key.

The delivery deliberately avoided treating optional delete as a broad family
gate. It admitted only the two expression-program shapes whose receiver, key,
delete operation, and short-circuit result can be proven with existing unified
VM opcodes.

## Decision

Production unified bytecode admits exactly these optional computed delete forms:

- `delete box?.child[key]`
- `delete box.child?.[key]`

Both forms require an activation-resolved root receiver, non-private named
receiver hops, a supported computed-key span, and a final
`DeleteComputedProperty` operation. Broader optional delete forms remain
pre-VM declines.

For `delete box?.child[key]`, the source expression program carries an optional
named first hop followed by key evaluation and `DeleteComputedProperty`. The
compiler lowers it by loading the root receiver, emitting
`JumpIfNullishReplaceUndefined` before the named hop, emitting the named receiver
read and key span only on the live path, then executing `DeleteComputedProperty`.
The nullish branch jumps to a short-circuit block that pops the placeholder value
and loads literal `true`, so the computed key is skipped when the root receiver
is nullish.

For `delete box.child?.[key]`, the source expression program already contains
the optional-computed delete branch shape:

```text
[activation-resolved base, GetNamedProperty(non-optional)+,
 JumpIfNullish, key span, DeleteComputedProperty, Jump, Pop, true]
```

The compiler recognizes the whole tail shape, reuses the non-optional named
receiver hops, emits `JumpIfNullishReplaceUndefined`, appends the key span and
`DeleteComputedProperty` on the live path, and emits the same short-circuit
`true` block for the nullish branch.

The admitted path reuses existing VM semantics:

- `GetNamedProperty` for receiver hops
- `JumpIfNullishReplaceUndefined` plus `Pop` / `LoadLiteral(true)` for
  delete's nullish short-circuit result
- `DeleteComputedProperty` for descriptor-aware strict/sloppy delete behavior

No VM fallback, `ExpressionProgram` callback, `ExecutionPlanRunner` callback, or
AST evaluator path is introduced.

## Consequences

- Optional computed deletes for the two accepted shapes route through the
  production unified-bytecode fast path.
- Key evaluation order is preserved: the computed key is not evaluated on the
  nullish short-circuit path and is evaluated before the delete on the live path.
- The `OptionalChainDependency` decline is narrowed, not removed. Simple
  optional computed delete without a named receiver hop (`delete box?.[key]`),
  chained optional delete neighbors, private/super property access, dynamic
  lookup, and richer computed-key payloads stay outside production routing until
  a future slice owns their selector, compiler, VM, and route proof together.
- Proof needs both selector/opcode assertions and public invocation route-log
  tests because ordinary JavaScript behavior can pass through the existing IR
  route even when the unified fast path is not taken.
