# ADR 0298: Admit multi-hop optional named chains in unified bytecode via jump-based lowering

## Status

Accepted

## Context

Faktorial issue `planitem-planmanual1780198120145433000-widen-unified-bytecode-production-conditio-aae6bde47e`
and PR #2804 admitted `a?.b.c` (optional-start named chain) and `a?.b?.c`
(double-optional named chain) to the production unified-bytecode VM.

ADR 0296 admitted simple non-chained optional forms (`a?.b`, `a?.[k]`) and
explicitly deferred chained forms, saying they would require "either a
sentinel-value propagation scheme or a `JumpIfShortCircuited` opcode". The
preceding batch (issue `bbc04f2625` / PR #2802) admitted the two-or-more-hop
single-optional named chain `a?.b.c` and fixed-shape `a?.b[k]`. This ADR
covers the extension to double-optional (`a?.b?.c`) and documents the
jump-based lowering pattern used across all these named multi-hop forms.

## Decision

### Lowering pattern — reuse `JumpIfNullishReplaceUndefined`, no new opcode

For each optional hop in the chain (any `GetNamedProperty(IsOptional:true)`),
the compiler emits `JumpIfNullishReplaceUndefined(chain-end)` followed by a
plain `GetNamedProperty`. For non-optional continuation hops
(`GetNamedProperty(IsOptional:false, ShortCircuitOnNullishTarget:true)`), the
compiler emits only the plain `GetNamedProperty`.

All `JumpIfNullishReplaceUndefined` instructions in the chain share the **same
target PC** — the instruction immediately after the entire chain. This means a
nullish value at any hop (base or any `?.` intermediate) short-circuits the
entire remaining chain to `undefined` in a single jump.

No new VM opcode is needed. The original plan considered `JumpIfShortCircuitNullish`
and a sentinel-value propagation scheme; both are superseded by this simpler
approach. Zero changes were required to `UnifiedBytecodeVirtualMachine`.

### Admitted shapes

- **`a?.b.c`** (single optional start, plain continuation):
  `[base, GetNamedProperty(IsOptional:true), GetNamedProperty(ShortCircuit:true, IsOptional:false)+]`
  → emits `JumpIfNullishReplaceUndefined(end)`, `GetNamedProperty(b)`,
  `GetNamedProperty(c)`.

- **`a?.b?.c`** (double optional):
  `[base, GetNamedProperty(IsOptional:true), GetNamedProperty(IsOptional:true, ...)+]`
  → emits `JumpIfNullishReplaceUndefined(end)`, `GetNamedProperty(b)`,
  `JumpIfNullishReplaceUndefined(end)`, `GetNamedProperty(c)`.

The helper `TryIsFirstBoundaryOptionalNamedChainCandidate` accepts any sequence
that starts with an activation-resolved base, one `IsOptional:true` first hop,
and one or more subsequent hops that are either `ShortCircuitOnNullishTarget:true`
or `IsOptional:true`. The matching compiler helper
`TryAppendFirstBoundaryOptionalNamedPropertyReadChain` emits opcodes per hop type.

### Semantic consequence: real-undefined intermediates

In `a?.b.c`, if `a.b` evaluates to the actual value `undefined` (not null/undefined
that triggered the `?.`), the `GetNamedProperty(c)` plain hop runs on `undefined`
and correctly throws `TypeError`. The `JumpIfNullishReplaceUndefined` on the first
hop detects nullish (`null` or `undefined`) atomically and jumps; a non-nullish
value (including actual `undefined` returned from a property) does **not** trigger
the jump.

In `a?.b?.c`, the second `?.c` hop also has `JumpIfNullishReplaceUndefined`, so an
actual-undefined `a.b` short-circuits to `undefined` without throwing.

### Deferred

- Computed multi-hop chains (`a?.b?.[k]`, `a?.[k]?.b`) — still `OptionalChainDependency`.
- Mixed named-then-computed-then-optional chains — still `OptionalChainDependency`.
- Optional chains in assignment targets, super, dynamic lookup — still declined.
- `JumpIfShortCircuited` (call-target expression programs) — unchanged, still declined.

## Consequences

- `a?.b.c` and `a?.b?.c` execute through the production unified bytecode VM, gaining
  the same throughput as other admitted property-read and arithmetic expressions.
- No new `UnifiedBytecodeOpCode` values were added; `JumpIfNullishReplaceUndefined`
  is reused for all optional hops.
- `OptionalChainDependency` is further narrowed: named multi-hop forms no longer
  decline; computed-hop forms, assignment-target, and super-optional forms retain
  the explicit decline reason.
- Tests: AC-1 (nullish base → undefined), AC-2 (deep value), AC-3 (real-undefined
  intermediate → TypeError for `a?.b.c`), per-hop short-circuit for `a?.b?.c`,
  base-evaluated-exactly-once (Gate 1), and two stale pre-guard decline tests
  flipped to assert eligibility.
- Lineage: ADR 0296 (optional member access admission), ADR 0293 (peek-semantics
  jump opcodes), ADR 0224 (side-effect-free compiler helpers before emission).
