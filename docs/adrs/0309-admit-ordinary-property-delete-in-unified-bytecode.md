# ADR 0309: Admit ordinary property delete in unified bytecode

## Status

Accepted

## Context

Production unified bytecode still declined ordinary property delete expression
programs with `DeleteDependency`, even though expression bytecode already emits
dedicated delete operations for named and computed property references:

```js
delete box.value
delete box[key]
delete box.child.value
```

The surrounding property-read and property-write lanes had already proven the
activation-resolved receiver, simple computed-key, strictness, and
descriptor-aware property helper boundaries needed for an owned VM route. The
risk was admitting the whole delete family at once: optional-chain delete,
private names, `super`, and unresolved dynamic identifier delete still have
separate semantics and must not enter the production VM through the ordinary
property path.

## Decision

Admit only ordinary named and computed property delete expression programs to
production unified bytecode.

The accepted boundary is:

1. named delete: an activation-resolved receiver, zero or more non-optional
   non-private named receiver reads, and a final non-private
   `DeleteNamedProperty`;
2. computed delete: an activation-resolved receiver, a simple
   compiler-owned key operand, and a final `DeleteComputedProperty`; and
3. no optional-chain short-circuiting in the delete expression program.

The compiler lowers accepted delete operations to owned
`DeleteNamedProperty` and `DeleteComputedProperty` opcodes. The VM executes them
through `PropertyHandle.Resolve(...).Delete()` with the active strictness
threaded from the function route and current scope, so configurable descriptor
removal, failed non-configurable deletes, sloppy `false`, and strict `TypeError`
behavior stay owned by the same runtime property-delete machinery as the
existing execution-plan route.

The delivery deliberately keeps adjacent families as pre-VM declines:

- private named deletes decline as `PrivateFieldDependency`;
- optional-chain deletes decline as `OptionalChainDependency`;
- `super` property delete remains in the super/delete reference boundary; and
- dynamic identifier delete remains limited to the separate with-backed
  dynamic-name lane.

## Consequences

- `DeleteDependency` is narrowed, not removed.
- Ordinary property delete can now use the production unified-bytecode fast path
  without calling back into `ExpressionProgram`, `ExecutionPlanRunner`, or AST
  evaluation.
- Future delete widening must prove selector, compiler, VM, route logging, and
  neighboring negative declines in the same slice, especially for optional
  chains, private names, and `super`.
- Descriptor-aware strict/sloppy behavior is part of the route contract, not
  optional parity coverage.

## Evidence

- Delivery PR #2900 merged as commit `2d2850df`.
- Changed production surfaces:
  - `src/Asynkron.JsEngine/Execution/UnifiedBytecode/UnifiedBytecodeProductionEligibility.cs`
  - `src/Asynkron.JsEngine/Execution/UnifiedBytecode/UnifiedBytecodeCompiler.cs`
  - `src/Asynkron.JsEngine/Execution/UnifiedBytecode/UnifiedBytecodeProgram.cs`
  - `src/Asynkron.JsEngine/Execution/UnifiedBytecode/UnifiedBytecodeVirtualMachine.cs`
  - `docs/unified-bytecode-expansion-contract.md`
- Focused proof added by the delivery:
  - eligibility accepts named, nested named, and computed property delete with
    owned delete opcodes;
  - invocation tests prove production fast-path logs for named and computed
    property delete;
  - computed-key coercion is observed exactly once; and
  - strict/sloppy non-configurable descriptor delete behavior is exercised on
    the production route.

## Issue / PR

Issue
`planitem-planmanual1780240661926543000-burn-down-unified-bytecode-production-decl-df696bb0ff`
/ PR #2900.

## Related

- `docs/adrs/0018-keep-delete-super-reference-check-before-property-key.md`
- `docs/adrs/0121-keep-descriptor-delete-result-semantics-strictness-owned.md`
- `docs/adrs/0221-keep-unified-bytecode-property-reads-vm-owned-and-observable.md`
- `docs/adrs/0308-admit-nested-named-property-write-receiver-chains-in-unified-bytecode.md`
- `docs/rules/js-spec-property-access.md`
- `docs/rules/unified-bytecode-prototypes.md`
