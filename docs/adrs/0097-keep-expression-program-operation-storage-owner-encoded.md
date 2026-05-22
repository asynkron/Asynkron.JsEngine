# ADR 0097: Keep ExpressionProgram operation storage owner-encoded

## Status

Accepted

## Context

Issue #1514 / PR #1521 implemented the first production compact
`ExpressionProgram` operation storage after ADR 0095 required compaction work to
remain measurement-led. The accepted slice kept `PackedExpressionOp` as the
semantic decoded view but moved the backing operation representation behind the
`ExpressionProgram` owner.

The delivery added owner APIs for operation count, indexed decode, and
enumeration, then moved runtime evaluation, compiler prepend logic, diagnostic
printing, storage diagnostics, identifier collection, slot rewriting, tests, and
the profile runner away from direct `Operations` storage assumptions.

The proof stayed phase-separated:

1. focused build and expression lowering/storage/slot-stamping tests stayed
   green;
2. direct-storage and AST-eval seam scans stayed clean;
3. `./tools/profile forloop --memory` still reported `Total allocated:
   7.05 MB`; and
4. `forloop --expression-program-storage` reported `total_ops: 10` with
   `encoded_op_estimated_bytes: 80`.

## Decision

Keep compact expression-program operation storage behind `ExpressionProgram`.
Future code should treat decoded `PackedExpressionOp` values as views, not as
the primary storage owner.

The owner contract is:

1. construction and rewrite paths build encoded operation storage at
   `ExpressionProgram` boundaries;
2. consumers use `OperationCount`, `GetOperation`, or `EnumerateOperations`
   instead of reaching into backing operation arrays;
3. runtime execution decodes operations allocation-free as value views;
4. diagnostics and test bridges decode through owner APIs so printable plans
   remain stable and readable; and
5. storage diagnostics report encoded operation bytes separately from semantic
   operation count and constant-pool counts.

This decision does not claim expression-op storage is the dominant runtime
allocation source. ADR 0095 still applies: future compaction or performance
claims must keep storage size, runtime allocations, and compile-time allocations
separate.

## Consequences

- `ExpressionProgram` is now the durable owner boundary for expression operation
  storage and decode semantics.
- New expression-op fields or flags must update the encoded storage and decoded
  view together, with focused semantic coverage for both sides of the encoded
  branch.
- Diagnostics and tests should prefer decoded owner APIs over direct structural
  storage assertions, so storage can keep compacting without losing readable
  failure output.
- Future storage measurements should include `EstimatedEncodedOperationBytes`
  and keep nested expression-program traversal complete.
- This ADR extends ADR 0095 and is enforced by the root
  `.claude/rules/expression-bytecode-packing.md` rule.
