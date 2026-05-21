# Expression Bytecode Packing

When adding or changing `PackedExpressionOp` data, prove that the factory,
decoder property, printer, compiler, and runner all agree on the same backing
channel.

## Rules

1. Treat `flags`, `int0`, `int1`, and `metadata` as distinct storage channels.
   Do not write semantic state to one channel and decode it from another.
2. Before changing a packed operation factory, inspect the matching decoded
   property on `PackedExpressionOp` and every consumer that reads it through
   `ExpressionOpView`.
3. Keep binary enum-like state in flags only when the decoder is explicitly
   flag-based. If a value needs more than a boolean, add or reuse a matching
   encoded field and update the decoder at the same time.
4. Add focused semantic coverage for both sides of an encoded branch, not only
   a trace/printer assertion. For accessors, that means proving getter and
   setter descriptors behave correctly at runtime.
5. Before starting compact-encoding work that claims allocation or storage
   benefit, capture current-worktree evidence and keep storage size, runtime
   allocations, and compile-time allocations separate. `./tools/profile forloop
   --memory` is the minimum runtime signal; use representative lowering
   diagnostics or tiny tooling-only diagnostics for operation counts,
   `MaxStackDepth`, and constant-pool sizes. Do not choose expression-op
   storage as the first compacting target unless the evidence points there.

## Why

Issue #758 / PR #890 fixed computed object literal setters after
`DefineComputedObjectAccessor` wrote `AccessorKind` into `int0` while
`AccessorKind` decoded from `Flag0`. The bytecode looked structurally valid, but
runtime descriptor creation interpreted the setter as a getter. Packed
expression ops are intentionally compact, so future changes need an explicit
writer/reader/consumer check to avoid another silent channel mismatch.

Issue #1403 measured `ExpressionProgram` storage and allocation before compact
encoding work. The current-worktree `forloop --memory` sample allocated 7.05 MB
and was dominated by engine/bootstrap allocations, while representative
lowering diagnostics only supplied operation/stack-shape context. That evidence
did not justify expression-op compacting as the first optimization target and
showed why compaction work must start with phase-separated measurements rather
than a storage-shape assumption.
