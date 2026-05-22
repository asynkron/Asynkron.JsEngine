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
6. When adding or extending `ExpressionProgram` storage diagnostics, treat the
   diagnostic walker as a coverage surface. Include every expression-program
   carrier that can hide below a top-level plan: nested function/class literals,
   class static block execution plans, destructuring source programs, catch
   binding target programs, and recursive binding-target subprograms. Add
   focused regression coverage for each newly discovered carrier, because a
   measurement tool that undercounts nested bytecode can steer later compaction
   work toward the wrong owner.
7. Keep expression-runtime side-state packed when the state is binary. Optional
   chain short-circuit metadata belongs in the packed `ExpressionFlagStack` /
   `_expressionFlagBuffer` path, not in parallel per-stack-slot `bool[]` or
   `byte[]` buffers. When touching this owner surface, include focused semantic
   tests for nested optional-chain propagation and a source or reflection guard
   that proves expression runtime fields did not reintroduce unpacked bool/byte
   arrays.
8. Keep compact `ExpressionProgram` operation storage owner-owned. Future
   runtime, diagnostics, printers, collectors, rewriters, test bridges, and
   tooling should use `ExpressionProgram.OperationCount`,
   `ExpressionProgram.GetOperation(...)`, or
   `ExpressionProgram.EnumerateOperations()` instead of direct backing-array
   assumptions. When changing the encoded operation storage, update the decoded
   `PackedExpressionOp` view, allocation-stable runner access, printable
   diagnostics, and `EstimatedEncodedOperationBytes` accounting together.

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

Issue #1468 / PR #1473 added `ExpressionProgramStorageDiagnostics` as the
bounded storage measurement slice for future compaction decisions. Review then
caught missing traversal for class static block execution plans and catch
binding target programs. The lesson is that diagnostic completeness is part of
the measurement contract: if the walker skips nested execution plans or binding
subprograms, the storage numbers can look precise while excluding real bytecode
payloads.

Issue #1515 / PR #1518 confirmed that optional-chain runtime side-state was
already represented through the packed `ulong[]` expression flag buffer, then
added focused guards instead of rewriting the runner. The lesson is that
runtime side-state has the same packing contract as operation metadata: future
agents should preserve packed binary storage, prove nested short-circuit
semantics, and guard against quietly restoring per-slot bool/byte arrays.

Issue #1514 / PR #1521 implemented compact `ExpressionProgram` operation
storage behind the runtime owner after ADR 0095 required measurement-led
compaction. The accepted shape kept `PackedExpressionOp` as the decoded
semantic view, moved consumers to owner APIs, preserved printer/test readability,
and kept the `forloop --memory` proof allocation-stable at 7.05 MB. The lesson
is that operation compaction belongs inside `ExpressionProgram`, while all
runtime and diagnostic callers should decode through that owner boundary rather
than learning the encoded arrays directly. Related ADR:
`docs/adrs/0097-keep-expression-program-operation-storage-owner-encoded.md`.
