# ADR 0283: Keep unified bytecode object destructuring model-first and static-key owned

## Status

Accepted

## Context

Issue #2677 widened production unified-bytecode routing to admit **object
destructuring** driver shapes. Before this slice only **array** destructuring
was admitted (`ArrayDestructuringInit/Element/Rest/Close`); object destructuring
(`const { a, b } = obj`) and expression-level `ApplyBindingTarget` destructuring
declined with `DestructuringDependency`. This was the
[expansion contract](../unified-bytecode-expansion-contract.md) "Ranked Next
Unsupported Buckets" #3 (model-first).

The accepted array path is descriptor-backed and VM-owned across instructions,
emitter, compiler, VM, eligibility, and contract docs (ADR
[0251](0251-keep-unified-bytecode-iterator-and-destructuring-drivers-model-first.md)).
Object destructuring needed the same model-first treatment rather than ad-hoc
per-instruction opcodes. Object destructuring differs from array destructuring:
there is no iterator — the source is coerced with `ToObject` and named
properties are read directly and in source order, a trailing rest collects the
remaining own enumerable keys, and there is no `IteratorClose` obligation.

Object destructuring also carries observable, spec-defined behavior the VM must
preserve exactly: property-read order, getter side effects, default-value
evaluation order, computed-key evaluation, and abrupt completion when the source
is non-coercible or a getter throws.

## Decision

Admit only the **proven, narrowest object-destructuring sub-shape** through a new
VM-owned driver opcode family, mirroring the array precedent.

- New instructions `ObjectDestructuringInitInstruction`,
  `ObjectDestructuringPropertyInstruction`,
  `ObjectDestructuringRestInstruction`, and
  `ObjectDestructuringCloseInstruction` are emitted by `DestructuringEmitter`
  (`TryEmitObjectDestructuring`) and executed by both the IR interpreter
  (`ExecutionPlanRunner`) and the unified-bytecode VM.
- New opcodes `ObjectDestructuringInit`, `ObjectDestructuringProperty`,
  `ObjectDestructuringRest`, and `ObjectDestructuringClose` compile to
  `UnifiedBytecodeDriverDescriptor` entries. The property opcode carries the
  static property name through a string-constant index.
- The VM owns `UnifiedObjectDestructuringState` (coerced source object + the set
  of consumed keys). `Init` runs `ToObject` (throwing `TypeError` for
  null/undefined), each `Property` reads a named property in source order and
  records the key, `Rest` collects the remaining own enumerable keys minus the
  consumed ones into a fresh object, and `Close` releases the driver slot.
- Eligibility admits the family only when the source lowers to expression
  bytecode and every property/rest target resolves to a unified flat slot.
- Admitted shape: static identifier property keys, identifier targets, no
  computed/dynamic keys, no defaults, no nested patterns, optional identifier
  rest.
- Everything else — computed/dynamic-name keys, defaults, nested patterns,
  non-slot-resolvable targets, and expression-level `ApplyBindingTarget`
  destructuring — keeps the generic `BindingVariableDeclarationInstruction`
  path and still declines with `DestructuringDependency` before VM execution.
- Abrupt completion (non-coercible source or a throwing getter) closes the
  VM-owned driver state; normal completion clears the slot via `Close`.

## Consequences

- `const { a, b } = obj` and `const { a, ...rest } = obj` gain the same flat-slot
  VM execution speedup that array destructuring already enjoys instead of
  falling back to mixed IR/AST execution.
- The admit/decline boundary stays auditable against the contract: unsupported
  shapes remain on the interpreter path with a stable decline code.
- Spec fidelity (property-read order, getter side effects, rest collection,
  abrupt cleanup) is identical between the interpreter and the VM because both
  execute the same instruction family and the VM reuses
  `TypedAstEvaluator.TryToObjectForDestructuring`.
- Future widening (computed keys, defaults, nested patterns) must add the same
  full stack — instruction shape, emitter lowering, compiler emission, VM state,
  eligibility admission, proof pack, and contract update — together, never a
  partial per-instruction opcode.

## Evidence

- Focused proof passed:
  `dotnet test tests/Asynkron.JsEngine.Tests --filter "FullyQualifiedName~UnifiedBytecodeProductionEligibilityTests"`
  and `~UnifiedBytecodeProductionInvocationTests`.
- Proof pack covers property-read order (getter side effects), rest collection,
  abrupt cleanup (non-coercible source and throwing getter), and negative
  fallback (computed keys, defaults, nested patterns decline).

## Related

- [docs/unified-bytecode-expansion-contract.md](../unified-bytecode-expansion-contract.md)
- `src/Asynkron.JsEngine/Execution/Emitters/DestructuringEmitter.cs`
- `src/Asynkron.JsEngine/Execution/UnifiedBytecode/UnifiedBytecodeProductionEligibility.cs`
- `src/Asynkron.JsEngine/Execution/UnifiedBytecode/UnifiedBytecodeVirtualMachine.cs`
- `src/Asynkron.JsEngine/Ast/TypedAstEvaluator.ExecutionPlanRunner.Handlers.ObjectDestructuring.cs`
- ADR 0251: `docs/adrs/0251-keep-unified-bytecode-iterator-and-destructuring-drivers-model-first.md`
- ADR 0201: `docs/adrs/0201-keep-unified-bytecode-production-routing-decline-first.md`
