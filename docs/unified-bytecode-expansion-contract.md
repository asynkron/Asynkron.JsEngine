# Unified Bytecode Expansion Contract

Date: 2026-05-28
Scope: Shared contract for parallel unified-bytecode lane work.

## Source-Of-Truth Surfaces
- Unified opcode surface: `src/Asynkron.JsEngine/Execution/UnifiedBytecode/UnifiedBytecodeProgram.cs` (`UnifiedBytecodeOpCode`)
- Unified compiler ownership: `src/Asynkron.JsEngine/Execution/UnifiedBytecode/UnifiedBytecodeCompiler.cs`
- Unified VM ownership: `src/Asynkron.JsEngine/Execution/UnifiedBytecode/UnifiedBytecodeVirtualMachine.cs`
- Production eligibility and decline taxonomy: `src/Asynkron.JsEngine/Execution/UnifiedBytecode/UnifiedBytecodeProductionEligibility.cs`
- Statement diagnostics support surface: `src/Asynkron.JsEngine/Execution/StatementInstructionDiagnosticsCodec.cs` (`IsSupportedKind`)
- Expression bytecode capability map: `docs/expression-bytecode-coverage.md`
- Current production routing evidence: `docs/performance/unified-bytecode-branch-production-routing.md`

## No-Mixed-Execution Rule
Production-eligible unified programs are all-or-nothing VM execution. Accepted
programs must execute fully in `UnifiedBytecodeVirtualMachine` and must not
fallback into `ExpressionProgram`, `ExecutionPlanRunner`, or AST evaluators.

## Current Support Matrix

### Unified Opcode Inventory (current)
- `LoadSlot`
- `LoadThis`
- `LoadNewTarget`
- `LoadLiteral`
- `StoreSlot`
- `Binary`
- `RequireObjectCoercible`
- `ResolvePropertyKey`
- `GetNamedProperty`
- `GetComputedProperty`
- `GetNamedPropertyForCompoundSet`
- `GetComputedPropertyForCompoundSet`
- `SetNamedProperty`
- `SetComputedProperty`
- `UpdateNamedProperty`
- `UpdateComputedProperty`
- `TypeOf`
- `TypeOfIdentifier`
- `UnaryPlus`
- `UnaryMinus`
- `UnaryLogicalNot`
- `UnaryBitwiseNot`
- `UnaryVoid`
- `ToString`
- `Pop`
- `CreateArray`
- `ArrayPush`
- `ArrayPushHole`
- `CreateObject`
- `DefineObjectProperty`
- `DefineComputedObjectProperty`
- `Jump`
- `JumpIfFalse`
- `Return`
- `PrepareIdentifierCallTarget`
- `PrepareNamedCallTarget`
- `PrepareComputedCallTarget`
- `CallInvocationBoundary`

### Production Decline Families (current)
- `None`
- `AsyncLikeFunction`
- `GeneratorFunction`
- `CapturedOrDynamicActivation`
- `ArgumentsObjectDependency`
- `ThisDependency`
- `NewTargetDependency`
- `CallDependency`
- `DynamicLookupDependency`
- `PropertyReadCandidateRequiresVmSupport`
- `PropertyReadBoundaryOutOfScope`
- `PropertyWriteDependency`
- `PropertyUpdateDependency`
- `DeleteDependency`
- `SuperPropertyDependency`
- `OptionalChainDependency`
- `ObjectLiteralOrSpreadDependency`
- `PrivateFieldDependency`
- `ForInDriverStateDependency`
- `DestructuringDependency`
- `LabelControlFlow`
- `BreakOrContinueControlFlow`
- `PrototypeOnlyBinaryOpcode`
- `PrototypeOnlyJumpOpcode`
- `PrototypeOnlyJumpIfFalseOpcode`
- `UnsupportedPlanShape`
- `CallInvocationBoundary`

### Statement Diagnostics Supported Kinds (current)
- `Jump`, `Break`, `Continue`, `SetCompletionValue`, `BreakableExit`
- `EvaluateAndDiscard`, `AwaitAndDiscard`, `Throw`, `Return`
- `AssignmentSlot`, `SimpleVariableDeclaration`, `BindingVariableDeclaration`, `StoreResumeValue`
- `FunctionDeclaration`, `ClassDeclaration`, `PushEnvironment`, `PopEnvironment`

### Expression Bytecode Family Groups (current)
- Loads and identifier references
- Call target resolution and super setup
- Array/object construction and property definitions
- Property get/set/update/delete operations
- Unary, binary, and conversion operations
- Control flow, stack mechanics, and invocation

## Reserved Ownership Lanes (planned, not implemented)
- Compiler-owned control-flow widening lanes
- VM-owned property and assignment semantics lanes
- Expression-op translation/normalization lanes
- Statement-storage/diagnostics compaction lanes
- Production-eligibility boundary and decline-taxonomy lanes

Reserved lanes define ownership for parallel work and do not imply runtime
support today.

## Iterator/Destructuring Model Boundary
- `ForInInitInstruction` and `ForInMoveNextInstruction` currently decline with
  `ForInDriverStateDependency`.
- `ArrayDestructuringInitInstruction`, `ArrayDestructuringElementInstruction`,
  `ArrayDestructuringRestInstruction`, and
  `ArrayDestructuringCloseInstruction` currently decline with
  `DestructuringDependency`.
- Decision for this lane: model-first. Do not add partial unified bytecode
  opcodes/VM paths for these stateful families until a full driver-state model
  is implemented.

## Proof Commands
```bash
rtk dotnet test tests/Asynkron.JsEngine.Tests --filter "FullyQualifiedName~ExpressionProgramCoverageMapTests"
rtk dotnet test tests/Asynkron.JsEngine.Tests --filter "FullyQualifiedName~UnifiedBytecodeProductionEligibilityTests"
rtk dotnet test tests/Asynkron.JsEngine.Tests --filter "FullyQualifiedName~UnifiedBytecodeProductionInvocationTests"
rtk rg "EvaluateExpression\\(|ProfileEvaluateExpression\\(" src/Asynkron.JsEngine/Ast/TypedAstEvaluator.ExecutionPlanRunner*
rtk ./tools/profile forloop --memory
```
