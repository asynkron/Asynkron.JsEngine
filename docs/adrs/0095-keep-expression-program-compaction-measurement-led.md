# ADR 0095: Keep ExpressionProgram compaction measurement-led

## Status

Accepted

## Context

Issue #1403 measured `ExpressionProgram` storage and allocation before any
compact encoding work. The issue was intentionally evidence-only: no production
change was allowed unless a tiny diagnostic was explicitly justified.

The build-stage proof ran `./tools/profile forloop --memory` on the current
worktree. The sampled memory profile reported `Total allocated: 7.05 MB`, with
top sampled allocations of `JsValue[] 2.52 MB (35.8%)`, `String 935.20 KB
(13.0%)`, `PropertyDescriptor 415.41 KB (5.8%)`, and `Double 308.14 KB
(4.3%)`. The sampled allocation tree was dominated by `JsEngine`
constructor/bootstrap paths, not by `ExpressionProgram` operation arrays,
statement IR records, AST objects, environment/slot metadata, or expression
runner side-state.

The same pass inspected representative lowering diagnostics from existing
tests. Examples included:

1. `ReturnInstruction_SimpleBinaryExpression_IsLoweredToExpressionProgram`:
   `MaxStackDepth = 2`;
2. `ImportMetaExpression_IsLoweredToExpressionProgram`: `MaxStackDepth = 1`;
3. `ReturnInstruction_NestedOptionalCallExpression_IsLoweredToExpressionProgram`:
   `Call` op count = `2`; and
4. `SimpleVariableDeclaration_ObjectAccessors_AreLoweredToExpressionProgram`:
   `DefineObjectAccessor` op count = `2`.

That evidence satisfied the measurement issue, but it did not prove that
compact expression-op encoding is the first performance target. It also did not
measure compile-time allocations from `ExpressionProgramBuilder` lists,
dictionaries, or immutable array construction.

## Decision

Do not start expression-program compact encoding from an assumption that
`PackedExpressionOp` storage is the hot allocation source. Treat compacting work
as measurement-led:

1. record current-worktree `./tools/profile forloop --memory` output before
   claiming a storage or allocation win;
2. distinguish storage size, runtime allocations, and compile-time allocations
   in the issue or PR evidence;
3. keep representative operation counts, `MaxStackDepth`, and constant-pool
   observations tied to existing lowering tests or a tiny local diagnostic; and
4. choose a first compacting target only when the evidence identifies the
   measured cost as expression-op storage, statement IR record storage, runner
   side-state, compile-time builder allocations, or another concrete owner.

## Consequences

- Future expression-bytecode compaction work should begin with a bounded
  measurement pass, not a structural rewrite.
- A single runtime memory profile that is dominated by engine/bootstrap
  allocations is enough to reject a premature "expression-op arrays first"
  claim, but not enough to reject later targeted compile-time or hot-loop
  instrumentation.
- If compact encoding is still pursued, the next proof should add focused
  compile-time and hot-loop instrumentation so the result can separate builder
  churn from runtime execution storage.
- This ADR is caused by issue #1403 and complements the root
  `.claude/rules/expression-bytecode-packing.md` rule.
