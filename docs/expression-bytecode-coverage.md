# Expression Bytecode Coverage Map

This map tracks how concrete `ExpressionNode` types relate to current expression bytecode compilation.

## Scope And Baseline Note
- This is baseline documentation and evidence only.
- It does not claim runtime behavior changes, new bytecode support, or performance wins.

## Source-Of-Truth Surfaces
- Compiler dispatch/support: `src/Asynkron.JsEngine/Execution/Instructions/ExpressionProgramCompiler.cs`
- Bytecode capability inventory: `src/Asynkron.JsEngine/Execution/Instructions/ExpressionOp.cs` (`ExpressionOpKind`)
- Unsupported/failure taxonomy: `src/Asynkron.JsEngine/Execution/Instructions/ExpressionProgramCompileFailure.cs`
- Failure detail classification probes: `tests/Asynkron.JsEngine.Tests/ExecutionPlanDiagnosticsTests.cs`

## Status Legend
- `supported`: compiled directly by `ExpressionProgramCompiler.TryCompileExpression`.
- `shape-dependent`: compiled directly, but some shapes classify to explicit `ExpressionProgramFailureCode` values.
- `unsupported`: not compiled directly; expected to classify as `UnsupportedExpressionNode` or another unsupported code path.
- `not-compiled-directly`: handled through lowering/specialized instruction seams before direct expression-bytecode compilation.

## ExpressionOpKind Capability Inventory
| Capability group | ExpressionOpKind values |
| --- | --- |
| Loads and references | `LoadLiteralConstant`, `LoadRegexLiteral`, `LoadIdentifier`, `LoadThis`, `LoadImportMeta`, `LoadNewTarget`, `LoadFunctionLiteral`, `LoadClassLiteral` |
| Object and array construction | `CreateArray`, `CreateObject`, `ArrayPush`, `ObjectSetDataProperty`, `ObjectSetComputedDataProperty`, `ObjectSetAccessor` |
| Member and index access | `LoadProperty`, `LoadComputedProperty`, `StoreProperty`, `StoreComputedProperty`, `DeleteProperty`, `DeleteComputedProperty` |
| Optional-chain-aware access | `LoadOptionalProperty`, `LoadOptionalComputedProperty`, `CallOptional`, `MemberCallOptional`, `BeginOptionalChainSegment`, `EndOptionalChainSegment`, `SkipOptionalChainSegment` |
| Calls and construction | `Call`, `MemberCall`, `Construct`, `ConstructMember` |
| Operators and transforms | `Unary`, `Binary`, `PrivateFieldIn`, `ToPropertyKey`, `DuplicateTop`, `Pop` |
| Control flow and stack branching | `Jump`, `JumpIfTrue`, `JumpIfFalse`, `JumpIfNullOrUndefined` |
| Template and string assembly | `BuildTemplateString` |

## Expression Family And Risk Groups

### Low-Risk Directly Supported Families
| Expression node | Status | Compiler coverage | Representative tests |
| --- | --- | --- | --- |
| `BinaryExpression` | `supported` | `TryCompileBinaryExpression` including private-field-in handling | `ExpressionProgramLoweringTests.ReturnInstruction_SimpleBinaryExpression_IsLoweredToExpressionProgram` |
| `ClassExpression` | `supported` | `LoadClassLiteral` | `Issue400And722ExpressionBytecodeTraceabilityTests.ConstantPools_RecordLiteralStringObjectAndIdentifierEntries` |
| `ConditionalExpression` | `supported` | `TryCompileConditionalExpression` | `ExpressionProgramLoweringTests` conditional coverage |
| `FunctionExpression` | `supported` | `LoadFunctionLiteral` | `Issue400And722ExpressionBytecodeTraceabilityTests.ConstantPools_RecordLiteralStringObjectAndIdentifierEntries` |
| `IdentifierExpression` | `supported` | `LoadIdentifier` | `ExpressionProgramLoweringTests.ThrowInstruction_AwaitedIdentifier_UsesAwaitedProgram` |
| `ImportMetaExpression` | `supported` | `LoadImportMeta` | `ExecutionPlanDiagnosticsTests` import-meta diagnostics coverage |
| `LiteralExpression` | `supported` | `LoadLiteralConstant` | `Issue400And722ExpressionBytecodeTraceabilityTests.ConstantPools_RecordLiteralStringObjectAndIdentifierEntries` |
| `NewTargetExpression` | `supported` | `LoadNewTarget` | `ExecutionPlanDiagnosticsTests` new.target coverage |
| `RegexLiteralExpression` | `supported` | `LoadRegexLiteral` | `ExpressionProgramLoweringTests` literal/return program checks |
| `SequenceExpression` | `supported` | `TryCompileSequenceExpression` | `ExpressionProgramLoweringTests` sequence and evaluation-order checks |
| `TemplateLiteralExpression` | `supported` | `TryCompileTemplateLiteralExpression` | `ExpressionProgramLoweringTests` template-related expression program coverage |
| `ThisExpression` | `supported` | `LoadThis` | `ExpressionProgramLoweringTests` object/member expression program coverage |

### Shape-Dependent Families (Classified Failure Buckets)
| Expression node | Status | Compiler coverage and known failure buckets | Representative tests |
| --- | --- | --- | --- |
| `ArrayExpression` | `shape-dependent` | `TryCompileArrayExpression`; nested unsupported operands bubble classified nested failures | `ExpressionProgramLoweringTests` array/object assignment and declaration cases |
| `AssignmentExpression` | `shape-dependent` | `TryCompileAssignmentExpression`; `UnsupportedCompoundAssignmentShape`, `UnsupportedExpressionNode` | `ExpressionProgramLoweringTests.AssignmentSlotInstruction_*` |
| `CallExpression` | `shape-dependent` | `TryCompileCallExpression`; `UnsupportedDirectMemberCallPropertyName`, `OptionalOrSuperMemberCallTarget`, `NestedOptionalCall`, `SuperCall` | `ExecutionPlanDiagnosticsTests.ExpressionFailureBuckets_TrackExpectedCodes` |
| `DestructuringAssignmentExpression` | `shape-dependent` | `TryCompileDestructuringAssignmentExpression`; unsupported source/target subshapes classify through nested failure path | `Issue400And722ExpressionBytecodeTraceabilityTests.RepresentativeInstructions_UseExpressionProgramsInsteadOfAstPayloads` |
| `IndexAssignmentExpression` | `shape-dependent` | `TryCompileIndexAssignmentExpression`; `OptionalOrSuperIndexAssignment` | `ExecutionPlanDiagnosticsTests.ExpressionFailureBuckets_TrackExpectedCodes` |
| `MemberExpression` | `shape-dependent` | `TryCompileMemberExpression`; `UnsupportedDotAccessPropertyName`, `SuperMemberAccess` | `ExecutionPlanDiagnosticsTests.ExpressionFailureBuckets_TrackExpectedCodes` |
| `NewExpression` | `shape-dependent` | `TryCompileNewExpression`; optional/super restrictions classify through call/member failure paths | `ExpressionProgramLoweringTests` constructor/literal expression coverage |
| `ObjectExpression` | `shape-dependent` | `TryCompileObjectExpression`; `UnsupportedObjectMemberKind`, `UnsupportedStaticObjectPropertyName`, `InvalidComputedObjectKey` | `ExpressionProgramLoweringTests.ObjectExpression_StaticIdentifierKeyNode_IsLoweredToExpressionProgram` |
| `PropertyAssignmentExpression` | `shape-dependent` | `TryCompilePropertyAssignmentExpression`; `OptionalOrSuperPropertyAssignment` | `ExecutionPlanDiagnosticsTests.ExpressionFailureBuckets_TrackExpectedCodes` |
| `TaggedTemplateExpression` | `shape-dependent` | `TryCompileTaggedTemplateExpression`; `SuperTaggedTemplate`, `OptionalTaggedTemplate`, `NestedOptionalTaggedTemplate`, `UnsupportedTaggedTemplateMemberAccessName` | `ExecutionPlanDiagnosticsTests.ExpressionFailureBuckets_TrackExpectedCodes` |
| `UnaryExpression` | `shape-dependent` | `TryCompileUnaryExpression`; `UnsupportedUnaryOperator`, `UnsupportedDeleteTarget` | `ExecutionPlanDiagnosticsTests.ExpressionFailureBuckets_TrackExpectedCodes` |

### Unsupported Or Lowering-Rerouted Families
| Expression node | Status | Current routing/classification reason | Representative tests |
| --- | --- | --- | --- |
| `DecoratorExpression` | `unsupported` | Not handled in compiler switch; classifies `UnsupportedExpressionNode` | No direct lowering coverage in current internal suite |
| `ExportDefaultExpression` | `unsupported` | Not handled in compiler switch; classifies `UnsupportedExpressionNode` | No direct lowering coverage in current internal suite |
| `AwaitExpression` | `not-compiled-directly` | Lowering rewrites awaited seams into dedicated awaited programs/instructions | `ExpressionProgramLoweringTests.ThrowInstruction_AwaitedIdentifier_UsesAwaitedProgram` |
| `PrivateIdentifierExpression` | `not-compiled-directly` | Used only as `BinaryOperator.In` left operand (`PrivateFieldIn` path) | `ExpressionProgramLoweringTests` private-field binary coverage |
| `SuperExpression` | `not-compiled-directly` | Routed through dedicated member/call/template guarded paths, not standalone bytecode node compilation | `ExecutionPlanDiagnosticsTests` super-related failure-code assertions |
| `YieldExpression` | `not-compiled-directly` | Lowering emits `YieldInstruction` / `YieldStarInstruction` seams with expression programs for yielded operands | `ExpressionProgramLoweringTests.YieldInstruction_*` and `YieldStarInstruction_*` |

## Failure-Code Bucket Index
Current `ExpressionProgramFailureCode` values used by classification and diagnostics include:
- `UnsupportedExpressionNode`
- `UnsupportedDeleteTarget`
- `UnsupportedUnaryOperator`
- `UnsupportedUpdateTarget`
- `SuperCall`
- `NestedOptionalCall`
- `UnsupportedCompoundAssignmentShape`
- `OptionalOrSuperMemberUpdate`
- `SuperTaggedTemplate`
- `OptionalTaggedTemplate`
- `NestedOptionalTaggedTemplate`
- `OptionalOrSuperPropertyAssignment`
- `OptionalOrSuperIndexAssignment`
- `SuperMemberAccess`
- `UnsupportedObjectMemberKind`
- `UnsupportedStaticObjectPropertyName`
- `InvalidComputedObjectKey`
- `UnsupportedDotAccessPropertyName`
- `UnsupportedDirectMemberCallPropertyName`
- `UnsupportedTaggedTemplateMemberAccessName`
- `OptionalOrSuperMemberCallTarget`

## Maintenance Guardrails
- When adding a new concrete `ExpressionNode`, update this map and add or reference a representative test.
- Keep this map aligned with `ExpressionProgramCompiler`, `ExpressionOpKind`, and failure-code classification tests.
