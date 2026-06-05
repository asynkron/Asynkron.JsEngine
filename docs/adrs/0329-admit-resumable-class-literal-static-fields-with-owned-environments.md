# ADR 0329: Admit resumable class literal static fields with owned environments

## Status

Accepted

## Context

ADR 0306 admitted `LoadClassLiteral` through the existing class-definition
machinery, and later B24 slices admitted narrow resumable class-literal
families by proving the class creation environment each family needed. B24c
needed the same decision for public static fields inside generator and async
bodies.

Static fields are evaluated during class definition, not during instance
construction. A static initializer such as `static value = seed + 1` can read a
resumable activation binding after suspension. Those bindings live in
`UnifiedBytecodeResumeState` flat slots, so the captured calling environment
alone is not enough. However, closure-producing initializer values, such as
`static read = () => seed` or object-method literals that close over `seed`,
need a longer-lived materialized body environment for the created function or
method. That ownership was not part of this slice.

## Decision

Admit the B24c public static-field class-literal subset through
`UnifiedBytecodeOpCode.LoadClassLiteral` on the resumable route when all of
these are true:

- every class element is a public non-computed static field;
- the class has no `extends` clause, static blocks, static methods/accessors,
  computed static names, private static elements, instance elements, or broad
  neighboring class-element families;
- each static field initializer is either non-capturing or reads resumable
  activation slots as a value expression that can be satisfied by a temporary
  class-definition environment;
- no static field initializer creates a closure or object/class literal whose
  nested body captures a resumable activation binding.

When a routed static field needs activation slots, the resumable VM creates a
class-definition environment from the resume state's flat slots, delegates class
creation to `CreateClassValueFromLiteral(...)`, and synchronizes any mutations
back into the unified slots. Closure-valued static field initializers remain a
pre-VM decline until the resumable route owns a materialized body environment
for those created functions.

## Consequences

- Public non-computed static-field class expressions can route through
  resumable production unified bytecode for generator and async bodies.
- The VM does not duplicate static field semantics; it reuses existing
  class-definition creation with an environment bridge only when that bridge
  owns the initializer dependencies.
- Future B24 widening must distinguish immediate value reads from closures that
  outlive class creation. A field initializer that creates a function, method,
  accessor, or nested class/object body with activation captures needs a
  materialized body-environment route, not a broader `LoadClassLiteral`
  allowlist.

## Evidence

- Delivery PR #3197 merged as commit `43aac5564`:
  `Agent: task planitem-planmanual1780639098493226000-full-unified-bytecode-execution-burndown-b-61145a55dd`.
- Changed files:
  - `src/Asynkron.JsEngine/Execution/UnifiedBytecode/UnifiedBytecodeProductionEligibility.cs`
  - `src/Asynkron.JsEngine/Execution/UnifiedBytecode/UnifiedBytecodeVirtualMachine.cs`
  - `tests/Asynkron.JsEngine.Tests/UnifiedBytecodeResumableClassExpressionTests.cs`
  - `tests/Asynkron.JsEngine.Tests/UnifiedBytecodeResumableLiteralTests.cs`
  - `docs/plans/bytecode-burndown-checklist.md`
  - `docs/unified-bytecode-expansion-contract.md`
- Focused proof added static-field route coverage for generator and async
  bodies, runtime closure-read behavior for immediate static field values,
  decline coverage for static blocks, computed/static/private neighboring
  elements, static accessors, closure-valued static fields, object-method
  closure initializers, and nested class closure initializers.

## Related

- `docs/adrs/0306-admit-class-literals-in-unified-bytecode-through-shared-class-creation.md`
- `docs/adrs/0327-admit-resumable-class-literal-private-members-through-shared-class-creation.md`
- `docs/adrs/0328-admit-resumable-class-literal-public-instance-fields-with-activation-safe-initializers.md`
- `docs/rules/unified-bytecode-prototypes.md`
