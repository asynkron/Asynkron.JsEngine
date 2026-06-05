# ADR 0328: Admit resumable class literal public instance fields with activation-safe initializers

## Status

Accepted

## Context

ADR 0306 admitted `LoadClassLiteral` through shared class creation, and the
resumable route later admitted constructor/default-constructor class literals.
B24b needed to decide whether public instance fields should still decline as
unowned class-definition work or could route through the same captured calling
environment bridge.

Public instance fields are not only metadata. Their initializer programs run
during class construction and can read either class-instance state, such as
`this`, or resumable activation bindings captured from the generator/async
body. The first shape can reuse existing class-definition machinery; the second
shape needs a materialized function body environment because the binding lives
in `UnifiedBytecodeResumeState` flat slots rather than in the captured calling
environment.

## Decision

Admit the B24b public instance-field class-literal subset through
`UnifiedBytecodeOpCode.LoadClassLiteral` on the resumable route when all of
these are true:

- the class literal has one or more public instance fields;
- field names are non-computed;
- the class has no `extends` clause, static fields, static blocks/elements,
  private fields, private members, public accessors, or public methods;
- every lowered field initializer program either has no activation dependency
  or reads only state already owned by class-definition creation, such as
  `this`.

The eligibility gate inspects cached class-definition field initializer
programs. If an initializer reads a resumable activation slot, the route
declines before VM execution with a diagnostic that points at the captured
binding and the missing materialized body-environment owner.

## Consequences

- Public non-computed instance-field class expressions can now route through
  resumable production unified bytecode without duplicating class field
  initialization semantics in the VM.
- Activation-capturing field initializers remain explicit pre-VM declines until
  the resumable route owns a materialized body environment for class-definition
  execution.
- Future B24 widening must classify field initializer dependencies separately
  from the presence of `LoadClassLiteral`; class literal admission is safe only
  when the required class-definition state is already bridged.

## Evidence

- Delivery PR #3202 merged as commit `8307f41de`:
  `Agent: task planitem-planmanual1780639098493226000-full-unified-bytecode-execution-burndown-b-8327a9bee2`.
- Changed files:
  - `src/Asynkron.JsEngine/Execution/UnifiedBytecode/UnifiedBytecodeProductionEligibility.cs`
  - `tests/Asynkron.JsEngine.Tests/UnifiedBytecodeResumableClassExpressionTests.cs`
  - `tests/Asynkron.JsEngine.Tests/UnifiedBytecodeResumableLiteralTests.cs`
  - `docs/bytecode-progress.md`
  - `docs/plans/bytecode-burndown-checklist.md`
  - `docs/unified-bytecode-expansion-contract.md`
- Focused proof added route and decline coverage for `LoadClassLiteral`,
  generator fast-path logging, field initializer values, undefined fields,
  constructor-order interaction, activation-capturing initializer decline, and
  non-B24b neighboring shapes in
  `UnifiedBytecodeResumableClassExpressionTests`.

## Related

- `docs/adrs/0306-admit-class-literals-in-unified-bytecode-through-shared-class-creation.md`
- `docs/adrs/0327-admit-resumable-class-literal-private-members-through-shared-class-creation.md`
- `docs/rules/unified-bytecode-prototypes.md`
