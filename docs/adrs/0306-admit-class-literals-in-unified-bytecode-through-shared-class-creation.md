# ADR 0306: Admit class literals in unified bytecode through shared class creation

## Status

Accepted

## Context

Production unified bytecode already admitted function literals and simple
literal/object/value expression lanes, but class expressions still declined as
`ObjectLiteralOrSpreadDependency`. That kept ordinary sync functions such as:

```js
function makeClass(baseValue) {
    return class extends baseValue {
        value() { return 1; }
    };
}
```

outside the production route even though expression bytecode already represents
the shape as `ExpressionOpKind.LoadClassLiteral`.

Class literals are semantically heavier than plain object or function literals:
they can evaluate `extends`, create class name scopes, initialize static
elements, use private-name scope, and rely on named-evaluation hints for
anonymous class values. The safe production widening therefore was not to
duplicate those semantics inside `UnifiedBytecodeVirtualMachine` and not to add
a fallback to `ExpressionProgram`, `ExecutionPlanRunner`, or raw AST
evaluation.

## Decision

Admit class literal value creation as a production unified bytecode opcode by
lowering `ExpressionOpKind.LoadClassLiteral` to
`UnifiedBytecodeOpCode.LoadClassLiteral`.

The unified VM executes the opcode by calling the shared class creation entry
point exposed from `TypedAstEvaluator`:

```csharp
TypedAstEvaluator.CreateClassValueFromLiteral(classExpression, closureEnv, context)
```

That helper preserves the existing class-definition machinery and named
evaluation behavior:

```csharp
classExpression.Definition.CreateClassValue(
    environment,
    context,
    classExpression.Name ?? context.CurrentFunctionNameHint)
```

The production bridge must provide a calling environment whenever a program
contains `LoadClassLiteral`, matching the existing closure-environment
requirement for `LoadFunctionLiteral`. Class constructor activations remain
separate activation-level declines; this ADR admits evaluating a class
expression as a value inside an otherwise eligible ordinary sync function, not
executing class constructors through the production route.

## Consequences

- Class expression values can now route through production unified bytecode when
  the surrounding function is otherwise eligible.
- The VM owns the `LoadClassLiteral` instruction, but class semantics remain
  centralized in the existing class-definition creation path.
- The route still follows the no-mixed-execution rule: no opcode calls back into
  the expression interpreter, execution plan runner, or raw AST evaluator.
- Future literal/value opcode widening must keep selector eligibility, compiler
  lowering, VM execution, sync bridge environment preflight, production opcode
  inventory, expansion-contract documentation, and focused tests synchronized in
  the same slice.

## Evidence

- Delivery PR #2858 merged as commit `da5299d3`:
  `Agent: task planitem-planmanual1780240661926543000-burn-down-unified-bytecode-production-decl-ad43f135ac`.
- Changed files:
  - `src/Asynkron.JsEngine/Ast/ClassDefinitionExtensions.cs`
  - `src/Asynkron.JsEngine/Ast/TypedAstEvaluator.SyncFunctionInvoker.cs`
  - `src/Asynkron.JsEngine/Execution/UnifiedBytecode/UnifiedBytecodeCompiler.cs`
  - `src/Asynkron.JsEngine/Execution/UnifiedBytecode/UnifiedBytecodeProgram.cs`
  - `src/Asynkron.JsEngine/Execution/UnifiedBytecode/UnifiedBytecodeVirtualMachine.cs`
  - `src/Asynkron.JsEngine/Execution/UnifiedBytecode/UnifiedBytecodeProductionEligibility.cs`
  - `tests/Asynkron.JsEngine.Tests/UnifiedBytecodeProductionEligibilityTests.cs`
  - `docs/unified-bytecode-expansion-contract.md`
- Focused delivery proof passed:
  - `rtk dotnet build src/Asynkron.JsEngine/Asynkron.JsEngine.csproj -v minimal`
  - `rtk dotnet test tests/Asynkron.JsEngine.Tests/Asynkron.JsEngine.Tests.csproj --filter "FullyQualifiedName~UnifiedBytecodeProductionEligibilityTests" -v minimal`
  - Result: 309 filtered tests passed.
- Re-entry quality-gate repair updated
  `docs/unified-bytecode-expansion-contract.md` so the opcode inventory lists
  `LoadClassLiteral` instead of a duplicate `LoadFunctionLiteral`.
  - Focused proof:
    `rtk dotnet test tests/Asynkron.JsEngine.Tests/Asynkron.JsEngine.Tests.csproj -c Debug --no-build --filter "FullyQualifiedName~ExpressionProgramCoverageMapTests.UnifiedBytecodeExpansionContract_ListsRequiredHeadingsAndCurrentEnums"`

## Related

- `docs/adrs/0201-keep-unified-bytecode-production-routing-decline-first.md`
- `docs/adrs/0204-keep-unified-bytecode-sync-production-routing-slot-bridged.md`
- `docs/adrs/0246-keep-unified-bytecode-expansion-contract-source-of-truth-and-drift-guarded.md`
- `docs/rules/unified-bytecode-prototypes.md`
