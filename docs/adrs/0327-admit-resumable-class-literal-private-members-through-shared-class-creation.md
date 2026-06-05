# ADR 0327: Admit resumable class literal private members through shared class creation

## Status

Accepted

## Context

ADR 0306 admitted `LoadClassLiteral` for ordinary production unified bytecode by
executing class creation through the existing `CreateClassValueFromLiteral(...)`
machinery instead of duplicating class-definition semantics in the VM. The
resumable route later admitted constructor/default-constructor class literals
and private instance fields, but private methods and private accessors still
declined as unowned class-definition work.

The B24f delivery needed to decide whether private methods/accessors were a
separate VM implementation problem or another narrow shape that could reuse the
same captured calling environment bridge. The risk was over-admitting class
literals whose constructor or private member bodies close over resumable
activation slots: those locals live in the resume state's flat slots, not in a
materialized function body environment visible to class-definition-created
callables.

## Decision

Admit the narrow B24f resumable class-literal shape through
`UnifiedBytecodeOpCode.LoadClassLiteral` and the shared class creation helper
when all of these are true:

- the class literal has private instance methods and/or private instance
  accessors;
- it has no `extends` clause on this route;
- it has no fields, static blocks/elements, static members, computed members,
  or public members;
- neither the constructor body nor any admitted private member body captures a
  resumable activation binding.

The resumable route keeps `LoadClassLiteral` environment-backed. It uses the
captured `UnifiedBytecodeResumeState.CallingEnvironment`, synchronizes unified
slots around class creation, and delegates private-name scope, brand creation,
method/accessor descriptor setup, and invocation semantics to the existing
class-definition machinery.

Class literals outside that proof remain pre-VM declines. In particular,
activation-slot captures in constructor/private member bodies still require a
future materialized body-environment route, and neighboring B24 leaves such as
static elements, computed members, public accessors, fields, and class-member
`super` remain owned by their specific slices.

## Consequences

- Private method/accessor class literals can route through resumable production
  unified bytecode when the admitted shape is environment-safe.
- The VM still does not duplicate private-name or class-definition semantics.
- Future B24 widening must classify which part of class-definition state is
  already owned by the captured calling environment and must pair positive route
  proof with no-route proof for activation-slot captures and neighboring class
  element families.

## Evidence

- Delivery PR #3194 merged as commit `28a891696`:
  `Agent: task planitem-planmanual1780639098493226000-full-unified-bytecode-execution-burndown-b-7d0f3d6a80`.
- Changed files:
  - `src/Asynkron.JsEngine/Execution/UnifiedBytecode/UnifiedBytecodeProductionEligibility.cs`
  - `tests/Asynkron.JsEngine.Tests/UnifiedBytecodeResumableClassLiteralPrivateMemberTests.cs`
  - `docs/plans/bytecode-burndown-checklist.md`
  - `docs/unified-bytecode-expansion-contract.md`
- Focused proof added route and decline coverage for private methods,
  private accessors, private fields, constructor/member activation captures,
  and `extends` declines in
  `UnifiedBytecodeResumableClassLiteralPrivateMemberTests`.

## Related

- `docs/adrs/0306-admit-class-literals-in-unified-bytecode-through-shared-class-creation.md`
- `docs/adrs/0323-keep-resumable-unified-bytecode-context-sensitive-cleanup-declined.md`
- `docs/rules/ecmascript-private-names.md`
- `docs/rules/unified-bytecode-prototypes.md`
