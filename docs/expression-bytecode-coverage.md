# Expression Bytecode Coverage Map

This map tracks how concrete `ExpressionNode` types relate to `ExpressionProgramCompiler` bytecode compilation.

Status meanings:
- `supported`: compiled directly by `ExpressionProgramCompiler.TryCompileExpression`.
- `shape-dependent`: compiled directly, but some shapes fail with explicit `ExpressionProgramFailureCode` classifications.
- `unsupported`: currently not compiled directly and expected to classify as unsupported.
- `not-compiled-directly`: expression node is handled by lowering/specialized instruction seams before direct expression-bytecode compilation.

| Expression node | Status | Compiler coverage / failure classification | Representative tests |
| --- | --- | --- | --- |
| `ArrayExpression` | `shape-dependent` | `TryCompileArrayExpression`; spread and nested expression shapes compile; nested unsupported operands bubble classified failures | `ExpressionProgramLoweringTests` array/object assignment and declaration cases |
| `AssignmentExpression` | `shape-dependent` | `TryCompileAssignmentExpression`; unsupported compound shapes -> `UnsupportedCompoundAssignmentShape`; unsupported target shapes -> `UnsupportedExpressionNode` | `ExpressionProgramLoweringTests.AssignmentSlotInstruction_*` |
| `AwaitExpression` | `not-compiled-directly` | Lowering rewrites awaited seams into dedicated awaited programs/instructions; direct compile falls back unsupported | `ExpressionProgramLoweringTests.ThrowInstruction_AwaitedIdentifier_UsesAwaitedProgram` |
| `BinaryExpression` | `supported` | `TryCompileBinaryExpression` with dedicated control-flow ops and private-field-in handling | `ExpressionProgramLoweringTests.ReturnInstruction_SimpleBinaryExpression_IsLoweredToExpressionProgram` |
| `CallExpression` | `shape-dependent` | `TryCompileCallExpression`; unsupported member/property shapes classify `UnsupportedDirectMemberCallPropertyName`, `OptionalOrSuperMemberCallTarget`, `NestedOptionalCall`, `SuperCall` | `ExecutionPlanDiagnosticsTests.ExpressionFailureBuckets_TrackExpectedCodes` |
| `ClassExpression` | `supported` | `LoadClassLiteral` | `Issue400And722ExpressionBytecodeTraceabilityTests.ConstantPools_RecordLiteralStringObjectAndIdentifierEntries` |
| `ConditionalExpression` | `supported` | `TryCompileConditionalExpression` with branch/jump ops | `ExpressionProgramLoweringTests` conditional coverage |
| `DecoratorExpression` | `unsupported` | Not handled in compiler switch; classifies `UnsupportedExpressionNode` | No direct lowering coverage in current internal suite |
| `DestructuringAssignmentExpression` | `shape-dependent` | `TryCompileDestructuringAssignmentExpression`; unsupported source/target subshapes classify via nested failure code path | `Issue400And722ExpressionBytecodeTraceabilityTests.RepresentativeInstructions_UseExpressionProgramsInsteadOfAstPayloads` |
| `ExportDefaultExpression` | `unsupported` | Not handled in compiler switch; classifies `UnsupportedExpressionNode` | No direct lowering coverage in current internal suite |
| `FunctionExpression` | `supported` | `LoadFunctionLiteral` | `Issue400And722ExpressionBytecodeTraceabilityTests.ConstantPools_RecordLiteralStringObjectAndIdentifierEntries` |
| `IdentifierExpression` | `supported` | `LoadIdentifier` | `ExpressionProgramLoweringTests.ThrowInstruction_AwaitedIdentifier_UsesAwaitedProgram` |
| `ImportMetaExpression` | `supported` | `LoadImportMeta` | `ExecutionPlanDiagnosticsTests` import-meta diagnostics coverage |
| `IndexAssignmentExpression` | `shape-dependent` | `TryCompileIndexAssignmentExpression`; optional/super targets classify `OptionalOrSuperIndexAssignment` | `ExecutionPlanDiagnosticsTests.ExpressionFailureBuckets_TrackExpectedCodes` |
| `LiteralExpression` | `supported` | `LoadLiteralConstant` | `Issue400And722ExpressionBytecodeTraceabilityTests.ConstantPools_RecordLiteralStringObjectAndIdentifierEntries` |
| `MemberExpression` | `shape-dependent` | `TryCompileMemberExpression`; unsupported property names -> `UnsupportedDotAccessPropertyName`; super-member access -> `SuperMemberAccess` | `ExecutionPlanDiagnosticsTests.ExpressionFailureBuckets_TrackExpectedCodes` |
| `NewExpression` | `shape-dependent` | `TryCompileNewExpression`; nested optional/super call restrictions classify through call/member failure paths | `ExpressionProgramLoweringTests` constructor/literal expression coverage |
| `NewTargetExpression` | `supported` | `LoadNewTarget` | `ExecutionPlanDiagnosticsTests` new.target coverage |
| `ObjectExpression` | `shape-dependent` | `TryCompileObjectExpression`; unsupported member kind -> `UnsupportedObjectMemberKind`; invalid static/computed property names -> `UnsupportedStaticObjectPropertyName` / `InvalidComputedObjectKey` | `Issue400And722ExpressionBytecodeTraceabilityTests.ConstantPools_RecordLiteralStringObjectAndIdentifierEntries` |
| `PrivateIdentifierExpression` | `not-compiled-directly` | Compiled only as a special `BinaryOperator.In` left operand in `TryCompileBinaryExpression` -> `PrivateFieldIn` op | `ExpressionProgramLoweringTests` private-field binary coverage |
| `PropertyAssignmentExpression` | `shape-dependent` | `TryCompilePropertyAssignmentExpression`; optional/super restrictions -> `OptionalOrSuperPropertyAssignment` | `ExecutionPlanDiagnosticsTests.ExpressionFailureBuckets_TrackExpectedCodes` |
| `RegexLiteralExpression` | `supported` | `LoadRegexLiteral` | `ExpressionProgramLoweringTests` literal/return program checks |
| `SequenceExpression` | `supported` | `TryCompileSequenceExpression` | `ExpressionProgramLoweringTests` sequence and evaluation-order checks |
| `SuperExpression` | `not-compiled-directly` | Used in dedicated member/call/template guarded paths; bare super expression is not emitted as standalone direct node | `ExecutionPlanDiagnosticsTests` super-related failure-code assertions |
| `TaggedTemplateExpression` | `shape-dependent` | `TryCompileTaggedTemplateExpression`; super/optional/nested optional restrictions classify `SuperTaggedTemplate`, `OptionalTaggedTemplate`, `NestedOptionalTaggedTemplate`, `UnsupportedTaggedTemplateMemberAccessName` | `ExecutionPlanDiagnosticsTests.ExpressionFailureBuckets_TrackExpectedCodes` |
| `TemplateLiteralExpression` | `supported` | `TryCompileTemplateLiteralExpression` | `ExpressionProgramLoweringTests` template-related expression program coverage |
| `ThisExpression` | `supported` | `LoadThis` | `ExpressionProgramLoweringTests` object/member expression program coverage |
| `UnaryExpression` | `shape-dependent` | `TryCompileUnaryExpression`; unsupported operators classify `UnsupportedUnaryOperator`; delete super/optional restrictions classify `UnsupportedDeleteTarget` | `ExecutionPlanDiagnosticsTests.ExpressionFailureBuckets_TrackExpectedCodes` |
| `YieldExpression` | `not-compiled-directly` | Lowering emits `YieldInstruction` / `YieldStarInstruction` seams with expression programs for yielded operands; direct compile of `YieldExpression` is not used | `ExpressionProgramLoweringTests.YieldInstruction_*` and `YieldStarInstruction_*` |

## Notes
- This map is intentionally runtime-neutral. It does not change lowering or evaluator behavior.
- When adding a new concrete `ExpressionNode`, update this map and add or point to a representative test.
