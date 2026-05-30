# ADR 0294: Admit optional member access in unified bytecode with null-check opcodes

## Status

Accepted

## Context

Faktorial issue gh2771 and the associated build admitted `?.prop` and `?.[k]`
(non-chained optional member access) to the production unified-bytecode VM.

The expression-program IR already correctly emits:
- `GetNamedProperty(IsOptional:true)` for `a?.b` — a single op that encodes the
  null check inline with the property read.
- `JumpIfNullish(ReplaceWithUndefined:true)` + key load + `GetComputedProperty` for
  `a?.[k]` — a jump-over pattern that skips property access when the base is
  null/undefined.

Prior to this ADR, the eligibility gate declined both forms with
`OptionalChainDependency`. This ADR admits the simple (non-chained) shapes.

## Decision

### New opcodes (rule 40 four-surface coupling)

**`GetNamedPropertyOptional`** — handles `a?.b`:
- Operand: string constant index (property name).
- Semantics: if `stack[sp-1].IsNullOrUndefined`, replace top with `undefined` and
  continue; else call `GetNamedPropertyValue` as normal.

**`JumpIfNullishReplaceUndefined`** — handles the nullish guard in `a?.[k]`:
- Operand: unified bytecode PC target.
- Semantics: if `stack[sp-1].IsNullOrUndefined`, replace top with `undefined` and
  jump to target; else continue. The key is loaded and `GetComputedProperty`
  executes on the non-null path; the target lands one position after
  `GetComputedProperty` so both branches leave exactly one value on the stack.

### Eligibility gate

The admitted shapes are constrained to exact two- or four-op programs:

- `a?.b` (AC-1): `[activation-resolved base, GetNamedProperty(IsOptional:true,
  !ShortCircuitOnNullishTarget, non-private)]` — admitted by new shape function
  `TryIsFirstBoundaryOptionalNamedPropertyReadCandidate`.

- `a?.[k]` (AC-2): `[activation-resolved base, JumpIfNullish(ReplaceWithUndefined:true),
  simple key (identifier or literal), GetComputedProperty(!ShortCircuitOnNullishTarget)]`
  — admitted by new shape function `TryIsFirstBoundaryOptionalComputedPropertyReadCandidate`.

`OptionalChainDependency` is retained for:
- `GetNamedProperty(ShortCircuitOnNullishTarget:true)` — second hop of chained forms
  like `a?.b?.c` or `a?.b.c` (deferred).
- `GetComputedProperty(ShortCircuitOnNullishTarget:true)` — second hop of chained
  computed chains (deferred).
- Optional forms in programs that do not match the admitted two/four-op shapes (e.g.
  `a && obj?.value`, multi-hop chains).
- `JumpIfShortCircuited` — chaining-sentinel semantics (deferred).

### Deferred

Chained optional forms (`a?.b?.c`, `a?.[k]?.b`) use `ShortCircuitOnNullishTarget:true`
on the second hop. Admitting these requires either a sentinel-value propagation scheme
or a `JumpIfShortCircuited` opcode; both are deferred to a later slice. The ACs for
gh2771 do not require chained forms.

## Consequences

- `box?.value` and `box?.[key]` expressions execute through the production unified
  bytecode VM, gaining the same throughput as admitted call and arithmetic expressions.
- Two new opcodes (`GetNamedPropertyOptional`, `JumpIfNullishReplaceUndefined`) are
  added to `UnifiedBytecodeProgram.cs`, the compiler, the VM, and the prototype-only
  allowlist in the eligibility gate.
- `OptionalChainDependency` is narrowed: the admitted simple forms no longer decline;
  genuinely unsupported forms (chained, assignment-target, super-optional) retain their
  explicit decline reason.
- Lineage: ADR 0289 (optional-call admission), ADR 0293 (peek-semantics jump opcodes).
