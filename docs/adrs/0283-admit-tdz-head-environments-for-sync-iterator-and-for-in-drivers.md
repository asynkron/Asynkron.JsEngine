# 283. Admit TDZ head environments for sync iterator and for-in drivers

- Status: Accepted
- Date: 2026-05-30
- Tags: execution, unified-bytecode, iterators, for-in, tdz

## Context

The production unified bytecode path admits `for...of` and `for...in` loops whose
driver state can be expressed entirely with flat activation slots and synchronous
expression bytecode. Until now `UnifiedBytecodeProductionEligibility` declined any
iterator or for-in driver that owned a temporal-dead-zone (TDZ) head environment —
that is, a lexical loop head such as `for (const x of values)` or `for (let k in obj)`.
Those shapes fell back to the legacy IR/AST runner, which builds a dedicated child
`JsEnvironment` for the head bindings and marks them `JsValue.Uninitialized` before the
source is evaluated, so `for (const x of [x])` throws a `ReferenceError`.

The unified-bytecode expansion is being widened one driver-state shape at a time
(issue #2678). The investigation identified three independently shippable slices:

- **Slice A — TDZ head environments** for the already-supported *sync* iterator and
  for-in drivers (this ADR).
- **Slice B — awaited iterable/object sources** (`AwaitedProgram`), still declined.
- **Slice C — the async iterator driver kind** (`IteratorDriverKind.Await`), still
  declined.

The instruction shapes (`IteratorInitInstruction` / `ForInInitInstruction`) already
carry `TdzBindings` / `TdzIsConst`, so admitting Slice A is an admit-and-implement
change, not a schema change.

## Decision

Slice A admits sync iterator and for-in drivers that own a TDZ head environment onto
the production unified bytecode path, and keeps async-kind and awaited-source drivers
declined.

- **Eligibility.** `IsSupportedIteratorInit` and `IsSupportedForInInit` no longer
  decline on `!TdzBindings.IsDefaultOrEmpty`. They still decline, with their explicit
  reasons, when the driver kind is not `Sync` ("Async iterator driver state is not
  eligible…") or when an awaited source is present ("…must be lowered to synchronous
  expression bytecode."). The no-mixed-execution invariant is preserved: a shape is
  admitted only because the compiler and VM fully implement its head-environment
  setup.

- **Compiler.** For a driver with TDZ bindings, the compiler resolves each head
  binding to a flat activation slot and emits a new `TdzHeadInit` instruction
  **before** the iterable/object source expression ops. If any head binding cannot be
  resolved to a flat activation slot the compiler declines, so an incompletely modeled
  head environment is never admitted.

- **VM.** The `TdzHeadInit` opcode marks the resolved head slots `JsValue.Uninitialized`
  (mirroring the existing `EnterScope` flat-slot TDZ path). Because the marking runs
  before source evaluation, a read of the head binding inside the source throws the
  correct `ReferenceError`. The per-iteration binding re-initializes the slot for the
  loop body, matching the legacy runner's observable behavior.

The driver-state ownership boundary for the production path is therefore:

| Driver state | Production path |
| --- | --- |
| Sync kind, sync source, no TDZ head | Admitted (pre-existing) |
| Sync kind, sync source, **TDZ head** | **Admitted (Slice A)** |
| Sync kind, **awaited source** | Declined (Slice B) |
| **Async kind** (`Await`) | Declined (Slice C) |

## Consequences

- `for (const x of …)`, `for (let x of …)`, and `for (const k in …)` now execute on
  the production VM with correct TDZ and per-iteration semantics, removing a legacy
  fallback for a common loop shape.
- `TdzHeadInit` is an additional opcode and the driver descriptor gained
  `TdzHeadSlots`/`TdzHeadIsConst`. The descriptor reuses the existing payload model;
  const-ness is carried for parity even though the production VM does not enforce const
  at store time (const violations are gated upstream, consistent with the existing
  flat-slot lexical-scope path).
- Async-kind and awaited-source drivers remain on the legacy path and keep declining
  with their explicit reasons, leaving Slices B and C to land separately.
