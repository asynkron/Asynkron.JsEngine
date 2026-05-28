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
- `IteratorInit`
- `IteratorMoveNext`
- `IteratorClose`
- `ForInInit`
- `ForInMoveNext`
- `ArrayDestructuringInit`
- `ArrayDestructuringElement`
- `ArrayDestructuringRest`
- `ArrayDestructuringClose`
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
- Stateful iterator, for-in, and array destructuring driver operations

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
  activation-resolved identifier calls, direct named member calls whose receiver
  chain is activation-resolved, and direct computed member calls whose receiver
  chain is activation-resolved. Arguments must be simple literal or slot
  operands; computed member keys must also be simple literal or slot operands.
  For accepted member receiver chains, the call receiver is the final resolved
  receiver object, not the root object.
- Accepted identifier-call programs use `PrepareIdentifierCallTarget` followed
  by `CallInvocationBoundary`; the VM resolves the callable from unified
  bytecode-owned slot state and invokes it through existing callable invocation
  helpers with the active `EvaluationContext` and caller `JsEnvironment` when
  the callee needs environment-aware or debug-aware invocation state.
- Accepted named member-call programs use `PrepareNamedCallTarget` followed by
  `CallInvocationBoundary`; the VM keeps the final receiver on the stack, loads
  the named callee from that receiver, and invokes through the existing
  receiver-as-`this` stack contract.
- Accepted computed member-call programs use `PrepareComputedCallTarget`
  followed by `CallInvocationBoundary`; the VM keeps the final receiver on the
  stack, preserves nullish-receiver-before-key-coercion ordering, consumes the
  computed key with normal property-key semantics, loads the callee from that
  receiver, and invokes through the existing receiver-as-`this` stack contract.
- Direct eval, spread calls, construct/super calls, optional calls,
  private/super member targets, arguments-object dependencies, dynamic lookup,
  complex receiver/key shapes, and receiver-binding-sensitive adjacent families
  still decline before VM execution.
- Accepted programs must still satisfy the no-mixed-execution rule: no
  callback into `ExpressionProgram`, `ExecutionPlanRunner`, or AST evaluation
  is allowed from `UnifiedBytecodeVirtualMachine`.

## Production Driver-State Boundary
- Current driver-state support is VM-owned and descriptor-backed. Driver
  instructions compile to `UnifiedBytecodeDriverDescriptor` entries rather than
  unrelated stack-op translations or AST payloads.
- Accepted sync iterator driver shapes include `IteratorInit`,
  `IteratorMoveNext`, and `IteratorClose` when the iterable source is lowered
  to synchronous expression bytecode, the iterator kind is `Sync`, and no TDZ
  head environment or awaited source program is required.
- Accepted for-in driver shapes include `ForInInit` and `ForInMoveNext` when
  the object source is lowered to synchronous expression bytecode and no TDZ
  head environment or awaited source program is required. The VM owns driver
  state and skips deleted properties during enumeration.
- Accepted array destructuring driver shapes include
  `ArrayDestructuringInit`, `ArrayDestructuringElement`,
  `ArrayDestructuringRest`, and `ArrayDestructuringClose` when the source is
  lowered to expression bytecode and element/rest targets resolve to unified
  flat slots. Normal and abrupt cleanup close the destructuring iterator from
  VM-owned driver state.
- Object destructuring, expression-level `ApplyBindingTarget` destructuring,
  async iterator drivers, TDZ head environments, awaited driver sources,
  dynamic-name shapes, and targets that cannot resolve to unified slots still
  decline before VM execution.

## Reserved Ownership Lanes (planned, not implemented)
- Compiler-owned control-flow widening lanes
- VM-owned property and assignment semantics lanes
- Expression-op translation/normalization lanes
- Statement-storage/diagnostics compaction lanes
- Production-eligibility boundary and decline-taxonomy lanes

Reserved lanes define ownership for parallel work and do not imply runtime
support today.

## Iterator/Destructuring Model Boundary
- `IteratorInitInstruction`, `IteratorMoveNextInstruction`, and
  `IteratorCloseInstruction` are eligible only for the synchronous lowered
  iterator-driver model described above.
- `ForInInitInstruction` and `ForInMoveNextInstruction` are eligible only for
  the lowered for-in driver model described above. Unsupported for-in driver
  shapes still decline with `ForInDriverStateDependency`.
- `ArrayDestructuringInitInstruction`,
  `ArrayDestructuringElementInstruction`,
  `ArrayDestructuringRestInstruction`, and
  `ArrayDestructuringCloseInstruction` are eligible only for the lowered
  direct-slot array destructuring model described above. Unsupported
  destructuring shapes still decline with `DestructuringDependency`.
- Decision for this lane: model-first. Any future widening must preserve
  explicit driver-state descriptors and pre-VM declines for shapes that would
  require mixed IR/AST execution.

## Next Unsupported Buckets (current boundary)
- Wider call invocation remains outside the admitted boundary. Direct eval,
  spread calls, construct/super calls, optional calls, arguments-object
  dependencies, dynamic lookup, private/super member targets, complex
  receiver/key shapes, and receiver-binding-sensitive adjacent families beyond
  the direct activation-resolved member-call boundary must still decline before VM
  execution.
- Async iterator drivers, TDZ head environments, and awaited iterator/for-in
  sources remain outside the admitted boundary and must decline before VM
  execution.
- Object destructuring, expression-level `ApplyBindingTarget` destructuring,
  dynamic-name destructuring shapes, and targets that cannot resolve to
  unified slots remain outside the admitted boundary
  (`DestructuringDependency`).
- Label-dependent control flow remains outside the admitted boundary
  (`LabelControlFlow`) and must decline before VM execution.
- Dynamic lookup families remain outside the admitted boundary
  (`DynamicLookupDependency`), including `with` environments and unresolved
  lookup shapes.

## Proof Commands
```bash
rtk dotnet test tests/Asynkron.JsEngine.Tests --filter "FullyQualifiedName~ExpressionProgramCoverageMapTests"
rtk dotnet test tests/Asynkron.JsEngine.Tests --filter "FullyQualifiedName~UnifiedBytecodeProductionEligibilityTests"
rtk dotnet test tests/Asynkron.JsEngine.Tests --filter "FullyQualifiedName~UnifiedBytecodePrototypeTests"
rtk dotnet test tests/Asynkron.JsEngine.Tests --filter "FullyQualifiedName~UnifiedBytecodeProductionInvocationTests"
rtk rg "EvaluateExpression\\(|ProfileEvaluateExpression\\(" src/Asynkron.JsEngine/Ast/TypedAstEvaluator.ExecutionPlanRunner*
rtk ./tools/profile forloop --memory
```
