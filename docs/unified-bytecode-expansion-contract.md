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
- `ReturnUndefined`
- `Throw`
- `PushEnvironment`
- `PopEnvironment`
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

## Production Loop-Control Boundary
- Current production control-flow support is compiler-owned, not
  source-syntax-owned.
- Accepted loop-control shapes include branch joins, direct branches, canonical
  condition-first loop backedges, unlabeled `BreakInstruction` and
  `ContinueInstruction` target jumps, simple for-style update continue targets,
  and simple do-while consequent backedges proven by PR #2489.
- Labeled breakable control flow still declines with `LabelControlFlow`.
  Unsupported complex loop/control-flow shapes must decline before VM execution
  instead of falling back from inside `UnifiedBytecodeVirtualMachine`.
- `BreakOrContinueControlFlow` remains listed above because it is still a
  decline-taxonomy enum member, but PR #2489 removed the blanket production
  pre-scan that rejected every break/continue instruction.

## Production Block Lexical Scope Boundary
- Current production block-scope support is slot-layout-owned, not
  source-syntax-owned.
- Accepted block scopes are non-capturing, non-iterating lexical scopes with
  flat slot mappings. Destructuring-free `let` / `const` declarations compile
  through `SimpleVariableDeclaration` into active-scope flat slots.
- `UnifiedBytecodeProgram.SlotCount` covers root activation slots and admitted
  block-scope flat slots. Parameter slot metadata preserves formal positions,
  using `-1` for unused or unmapped formals so invocation does not shift later
  arguments.
- `PushEnvironment` initializes the admitted scope's lexical flat slots to
  `JsValue.Uninitialized`; `PopEnvironment` is an owned VM cleanup opcode for
  the admitted linear shape. Neither opcode creates a `JsEnvironment`, uses
  name fallback, or calls back into `ExpressionProgram`, `ExecutionPlanRunner`,
  or AST evaluation.
- `BindingVariableDeclaration`, object/array destructuring driver
  instructions, `with`, direct eval / dynamic lookup, captured activation,
  per-iteration bindings, and `using` / `await using` remain pre-VM declines.

## Production Call Invocation Boundary
- Current executable call support is intentionally limited to no-spread
  activation-resolved identifier calls where the callee is loaded from an owned
  activation slot and arguments are simple literal or slot operands.
- Accepted identifier-call programs use `PrepareIdentifierCallTarget` followed
  by `CallInvocationBoundary`; the VM resolves the callable from unified
  bytecode-owned slot state and invokes it through existing callable invocation
  helpers with the active `EvaluationContext` and caller `JsEnvironment` when
  the callee needs environment-aware or debug-aware invocation state.
- Named member calls, computed member calls, direct eval, spread calls,
  construct/super calls, optional calls, arguments-object dependencies, dynamic
  lookup, and receiver-binding-sensitive adjacent families still decline before
  VM execution.
- Accepted programs must still satisfy the no-mixed-execution rule: no
  callback into `ExpressionProgram`, `ExecutionPlanRunner`, or AST evaluation
  is allowed from `UnifiedBytecodeVirtualMachine`.

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

## Next Unsupported Buckets (current boundary)
- Wider call invocation remains outside the admitted boundary. Named member
  calls, computed member calls, direct eval, spread calls, construct/super
  calls, optional calls, arguments-object dependencies, dynamic lookup, and
  receiver-binding-sensitive adjacent families must still decline before VM
  execution.
- Iterator-driver state remains outside the admitted boundary
  (`ForInDriverStateDependency`), including `for-in` driver instructions.
- Destructuring driver-state execution remains outside the admitted boundary
  (`DestructuringDependency`) for array/object binding and destructuring flows.
- Label-dependent control flow remains outside the admitted boundary
  (`LabelControlFlow`) and must decline before VM execution.
- Dynamic lookup families remain outside the admitted boundary
  (`DynamicLookupDependency`), including `with` environments and unresolved
  lookup shapes.

## Proof Commands
```bash
rtk dotnet test tests/Asynkron.JsEngine.Tests --filter "FullyQualifiedName~ExpressionProgramCoverageMapTests"
rtk dotnet test tests/Asynkron.JsEngine.Tests --filter "FullyQualifiedName~UnifiedBytecodeProductionEligibilityTests"
rtk dotnet test tests/Asynkron.JsEngine.Tests --filter "FullyQualifiedName~UnifiedBytecodeProductionInvocationTests"
rtk rg "EvaluateExpression\\(|ProfileEvaluateExpression\\(" src/Asynkron.JsEngine/Ast/TypedAstEvaluator.ExecutionPlanRunner*
rtk ./tools/profile forloop --memory
```
