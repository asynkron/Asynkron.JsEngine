# ADR 0361: Admit resumable static-block class literals through shared class creation

## Status

Accepted

## Context

ADR 0306 admitted `LoadClassLiteral` for ordinary production unified bytecode by
delegating class creation to the existing `CreateClassValueFromLiteral(...)`
path. ADR 0327 then used the same bridge for a narrow resumable private-member
class-literal shape, but kept static blocks out of that route as neighboring
class-definition work.

The B24d delivery needed to decide whether static-block-only class expressions
were another VM implementation problem or a class-creation subset that could
reuse the existing resumable class-literal slot environment. The useful split is
between static blocks that only execute against the current class-creation
environment and static blocks that create nested closures. The former can be
synchronized through the captured calling environment around class creation; the
latter need a materialized function body environment so created closures observe
resumable activation slots after suspension and later mutation.

## Decision

Admit the narrow B24d resumable class-literal shape through
`UnifiedBytecodeOpCode.LoadClassLiteral` and the shared class creation helper
when all of these are true:

- the class literal has static blocks;
- the static-block plans lowered successfully;
- the class literal does not mix in neighboring class-element families owned by
  other B24 leaves;
- no static block creates a nested function, arrow, or class closure that would
  capture resumable activation state.

The route keeps static-block execution inside the existing class-definition
machinery. `LoadClassLiteral` supplies the captured resumable calling
environment, synchronizes unified slots around class creation, and lets
`ClassDefinitionExtensions.ExecuteStaticBlock` execute the cached static-block
IR plan. This preserves activation reads and writes without duplicating
static-block semantics in `UnifiedBytecodeVirtualMachine`.

Closure-producing static blocks remain pre-VM declines until a future
materialized body-environment route owns that lifetime. Mixed class-element
neighbors such as computed/static/private/public member combinations remain
owned by their specific B24 slices.

## Consequences

- Static-block-only class expressions can route through resumable production
  unified bytecode while still using the shared class-definition implementation.
- The VM does not duplicate static-block execution semantics; it only owns the
  `LoadClassLiteral` bridge and slot synchronization around class creation.
- Future B24 widening must keep positive route proof paired with no-route proof
  for closure-producing static blocks and any class element body that needs the
  materialized body environment.
- The class/static-block bridge remains a known `ExecutionPlanRunner.RunScript`
  island inside class-definition creation, not a new runner fallback from the
  resumable VM itself.

## Evidence

- Delivery PR #3352 merged as commit `0a299ede7`:
  `Admit resumable class static blocks`.
- Changed files:
  - `src/Asynkron.JsEngine/Execution/UnifiedBytecode/UnifiedBytecodeProductionEligibility.cs`
  - `tests/Asynkron.JsEngine.Tests/UnifiedBytecodeResumableClassExpressionTests.cs`
  - `tests/Asynkron.JsEngine.Tests/UnifiedBytecodeResumableLiteralTests.cs`
  - `docs/plans/bytecode-burndown-checklist.md`
  - `docs/unified-bytecode-expansion-contract.md`
  - `docs/bytecode-progress.md`
  - `docs/roadmap.md`
- Focused proof from the delivery:
  - `rtk dotnet test tests/Asynkron.JsEngine.Tests --filter "FullyQualifiedName~UnifiedBytecodeResumableClassExpressionTests"` passed 52 tests.
  - Selected eligibility/coverage pack passed 686 tests.
  - Broad `UnifiedBytecodeProduction|UnifiedBytecodeResumable` pack passed 1652 tests.
  - `rtk git diff --check` was clean.
  - Runner AST-eval seam scan returned no matches.
  - `rtk ./tools/profile forloop --memory` reported 6.96 MB.

## Related

- `docs/adrs/0306-admit-class-literals-in-unified-bytecode-through-shared-class-creation.md`
- `docs/adrs/0327-admit-resumable-class-literal-private-members-through-shared-class-creation.md`
- `docs/rules/unified-bytecode-prototypes.md`
