# Unified Bytecode Expansion Contract

Date: 2026-06-01
Scope: Shared contract for parallel unified-bytecode lane work.

## Source-Of-Truth Surfaces
- Unified opcode surface: `src/Asynkron.JsEngine/Execution/UnifiedBytecode/UnifiedBytecodeProgram.cs` (`UnifiedBytecodeOpCode`)
- Unified compiler ownership: `src/Asynkron.JsEngine/Execution/UnifiedBytecode/UnifiedBytecodeCompiler.cs`
- Unified VM ownership: `src/Asynkron.JsEngine/Execution/UnifiedBytecode/UnifiedBytecodeVirtualMachine.cs`
- Production eligibility and decline taxonomy: `src/Asynkron.JsEngine/Execution/UnifiedBytecode/UnifiedBytecodeProductionEligibility.cs`
- Statement diagnostics support surface: `src/Asynkron.JsEngine/Execution/StatementInstructionDiagnosticsCodec.cs` (`IsSupportedKind`)
- Expression bytecode capability map: `docs/expression-bytecode-coverage.md`
- Current production routing evidence: `docs/performance/unified-bytecode-branch-production-routing.md`
- Primary sync route coverage: `docs/performance/unified-bytecode-primary-sync-route-coverage.md`

## No-Mixed-Execution Rule
Production-eligible unified programs are all-or-nothing VM execution. Accepted
programs must execute fully in `UnifiedBytecodeVirtualMachine` and must not
fallback into `ExpressionProgram`, `ExecutionPlanRunner`, or AST evaluators.
The bounded `ApplyBindingTarget` bridge is the current exception: the VM owns
dispatch and operand state, then applies an already-lowered `BindingTargetProgram`
for assignment destructuring parity instead of falling back to expression or
statement interpretation.

## Ordinary Sync Routing Boundary
- Ordinary sync invocation keeps the dedicated simple-return binary and
  binary-chain fast paths ahead of the broader production unified-bytecode
  route, matching ADR 0258's route-priority contract.
- For ordinary sync functions that pass both
  `CanUseProductionUnifiedBytecodeFastPath` and
  `UnifiedBytecodeProductionEligibility.Evaluate`, the production VM is the
  primary default attempt before `SyncIrCallTrampoline` and generic
  `ExecutionPlanRunner` interpretation.
- Current route coverage estimate: 100% of accepted ordinary sync production
  programs attempt `UnifiedBytecodeVirtualMachine` before generic IR fallback.
  This is a selector coverage estimate, not a full ECMAScript function-surface
  claim; unsupported buckets below remain pre-VM declines.

- Coverage evidence: `docs/performance/unified-bytecode-primary-sync-route-coverage.md`

## Current Support Matrix

### Unified Opcode Inventory (current)
- `LoadSlot`
- `LoadDynamicIdentifier`
- `LoadThis`
- `LoadNewTarget`
- `LoadLiteral`
- `StoreSlot`
- `InitializeSlot`
- `DeclareDynamicVar`
- `StoreDynamicIdentifier`
- `ResolveDynamicIdentifierReference`
- `LoadDynamicIdentifierReference`
- `StoreDynamicIdentifierReference`
- `PopDynamicIdentifierReference`
- `Binary`
- `RequireObjectCoercible`
- `ResolvePropertyKey`
- `GetNamedProperty`
- `GetNamedPropertyOptional`
- `GetComputedProperty`
- `GetNamedPropertyForCompoundSet`
- `GetComputedPropertyForCompoundSet`
- `SetNamedProperty`
- `SetComputedProperty`
- `UpdateNamedProperty`
- `UpdateComputedProperty`
- `UpdateDynamicIdentifier`
- `TypeOf`
- `TypeOfIdentifier`
- `TypeOfDynamicIdentifier`
- `DeleteDynamicIdentifier`
- `DeleteNamedProperty`
- `DeleteComputedProperty`
- `UnaryPlus`
- `UnaryMinus`
- `UnaryLogicalNot`
- `UnaryBitwiseNot`
- `UnaryVoid`
- `ToString`
- `DuplicateTop`
- `SwapTopTwo`
- `Pop`
- `CreateArray`
- `ArrayPush`
- `ArrayPushHole`
- `ArraySpread`
- `CreateObject`
- `DefineObjectProperty`
- `DefineComputedObjectProperty`
- `ObjectSpread`
- `Jump`
- `JumpWithDriverCleanup`
- `JumpIfFalse`
- `JumpIfShortCircuitFalse`
- `JumpIfShortCircuitTrue`
- `JumpIfShortCircuitNotNullish`
- `JumpIfNullishReplaceUndefined`
  *(Note: `Jump` was already in the inventory; `ConditionalExpression` (`?:`) is now admitted via ADR 0297 using the existing `Jump`, `JumpIfShortCircuitFalse`, and `Pop` opcodes without new additions.)*
- `Return`
- `ReturnUndefined`
- `Throw`
- `Break`
- `Continue`
- `PushEnvironment`
- `PopEnvironment`
- `EnterTry`
- `EnterCatch`
- `LeaveTry`
- `EndFinally`
- `EnterWith`
- `LeaveWith`
- `TdzHeadInit`
- `IteratorInit`
- `IteratorMoveNext`
- `IteratorClose`
- `ForInInit`
- `ForInMoveNext`
- `ArrayDestructuringInit`
- `ArrayDestructuringElement`
- `ArrayDestructuringRest`
- `ArrayDestructuringClose`
- `ObjectDestructuringInit`
- `ObjectDestructuringProperty`
- `ObjectDestructuringRest`
- `ObjectDestructuringClose`
- `PrepareIdentifierCallTarget`
- `PrepareDynamicIdentifierCallTarget`
- `PrepareNamedCallTarget`
- `PrepareComputedCallTarget`
- `PrepareNamedOptionalCallTarget`
- `PrepareComputedOptionalCallTarget`
- `PrepareNamedSuperCallTarget`
- `PrepareComputedSuperCallTarget`
- `CallInvocationBoundary`
- `ConstructInvocationBoundary`
- `SuperConstructInvocationBoundary`
- `LoadFunctionLiteral`
- `Yield`
- `StoreResumeValue`
- `AwaitAndDiscard`
- `AwaitedReturn`
- `YieldStar`
- `LoadClassLiteral`
- `ApplyBindingTarget`
- `EnsureHasName`

### Production Decline Families (current)
- `None`
- `AsyncLikeFunction`
- `GeneratorFunction`
- `CapturedOrDynamicActivation`
- `ArgumentsObjectDependency`
- `ThisDependency` *(conditional — see Production This-Binding Boundary below; currently never triggered for ordinary sync, and no longer triggered on the resumable async/generator route — see Production Resumable Boundary and ADR 0283)*
- `NewTargetDependency`
- `ArrowLexicalThisDependency`
- `ClassConstructorActivation`
- `FunctionNameParameterCollision`
- `FunctionDeclarationDependency`
- `ClassDeclarationDependency`
- `ParameterVarDeclarationDependency`
- `MaterializedActivationDependency`
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
- `UnsupportedPlanShape`
- `CallInvocationBoundary`

## Checked Production Decline Ledger

This ledger is a drift-guarded baseline for widening work. It distinguishes
ordinary sync pre-gates from `UnifiedBytecodeProductionEligibility` declines:
pre-gates fall through to the existing sync route before eligibility runs,
eligibility declines keep the plan out of the production VM, and accepted rows
must still obey the no-mixed-execution rule.

### Eligibility Decline Rows

| Decline code | Owning source / current example | Current fallback route | Planned batch / lane | Proof command |
|---|---|---|---|---|
| `None` | `UnifiedBytecodeProductionEligibility.Accept` for accepted programs, for example `function passThrough(x) { var y = x; return y; }` | Production unified-bytecode VM | Baseline accepted route | `rtk dotnet test tests/Asynkron.JsEngine.Tests --filter "FullyQualifiedName~UnifiedBytecodeProductionEligibilityTests&FullyQualifiedName~Evaluate_LinearSlotLiteralReturnPlan_Accepts"` |
| `AsyncLikeFunction` | `Evaluate` activation gate and awaited with-object plan decline | Existing async / awaited IR route | Resumable async/generator lane | `rtk dotnet test tests/Asynkron.JsEngine.Tests --filter "FullyQualifiedName~UnifiedBytecodeProductionEligibilityTests&FullyQualifiedName~Evaluate_AsyncLikeActivation_DeclinesBeforePlanInspection"` |
| `GeneratorFunction` | `Evaluate` activation gate for ordinary sync production routing | Existing generator IR route | Resumable async/generator lane | `rtk dotnet test tests/Asynkron.JsEngine.Tests --filter "FullyQualifiedName~UnifiedBytecodeProductionEligibilityTests&FullyQualifiedName~Evaluate_GeneratorActivation_DeclinesBeforePlanInspection"` |
| `CapturedOrDynamicActivation` | Activation descriptor `HasCapturedOrDynamicActivation`, including captured function scope or unresolved dynamic activation outside the with-backed lane | Existing sync IR / environment route | Dynamic activation lane | `rtk dotnet test tests/Asynkron.JsEngine.Tests --filter "FullyQualifiedName~UnifiedBytecodeProductionEligibilityTests&FullyQualifiedName~Evaluate_ActivationDependencies_DeclineBeforeCompile"` |
| `ArgumentsObjectDependency` | Activation descriptor and expression scan for `arguments` object access or arguments call targets | Existing sync IR / arguments-object route | Arguments object lane | `rtk dotnet test tests/Asynkron.JsEngine.Tests --filter "FullyQualifiedName~UnifiedBytecodeProductionEligibilityTests&FullyQualifiedName~Evaluate_ArgumentsAccess_DeclinesWithArgumentsDependency"` |
| `ThisDependency` | Activation descriptor gate retained for future ordinary sync shapes where `this` cannot be threaded into the VM | Existing sync IR route | This-binding follow-up lane | `rtk dotnet test tests/Asynkron.JsEngine.Tests --filter "FullyQualifiedName~UnifiedBytecodeProductionEligibilityTests&FullyQualifiedName~Evaluate_ActivationDependencies_DeclineBeforeCompile"` |
| `NewTargetDependency` | Activation descriptor gate for `new.target` dependency | Existing constructor / sync IR route | Constructor boundary lane | `rtk dotnet test tests/Asynkron.JsEngine.Tests --filter "FullyQualifiedName~UnifiedBytecodeProductionEligibilityTests&FullyQualifiedName~Evaluate_ActivationDependencies_DeclineBeforeCompile"` |
| `ArrowLexicalThisDependency` | Activation descriptor gate for arrow lexical `this` / `new.target` ownership before ordinary sync routing | Existing arrow invocation route | Arrow route lane | `rtk dotnet test tests/Asynkron.JsEngine.Tests --filter "FullyQualifiedName~UnifiedBytecodeProductionEligibilityTests&FullyQualifiedName~Evaluate_OrdinarySyncActivationDescriptorBlockers_DeclineBeforeCompile"` |
| `ClassConstructorActivation` | Activation descriptor gate for class constructor activation before ordinary sync routing | Constructor route | Constructor boundary lane | `rtk dotnet test tests/Asynkron.JsEngine.Tests --filter "FullyQualifiedName~UnifiedBytecodeProductionInvocationTests&FullyQualifiedName~OrdinarySyncActivationBlockers_DeclineUnifiedBytecodeAndFallBack"` |
| `FunctionNameParameterCollision` | Activation descriptor gate for named function expressions whose name collides with a parameter | Existing function-name environment route | Function-name environment lane | `rtk dotnet test tests/Asynkron.JsEngine.Tests --filter "FullyQualifiedName~UnifiedBytecodeProductionEligibilityTests&FullyQualifiedName~Evaluate_OrdinarySyncActivationDescriptorBlockers_DeclineBeforeCompile"` |
| `FunctionDeclarationDependency` | Activation descriptor gate for function declaration hoisting outside the with-backed materialized-activation lane | Existing hoisting route | Hoisting / dynamic-name lane | `rtk dotnet test tests/Asynkron.JsEngine.Tests --filter "FullyQualifiedName~UnifiedBytecodeProductionInvocationTests&FullyQualifiedName~OrdinarySyncActivationBlockers_DeclineUnifiedBytecodeAndFallBack"` |
| `ClassDeclarationDependency` | Statement-instruction scan for class declaration production before VM execution | Existing class declaration route | Class declaration lane | `rtk dotnet test tests/Asynkron.JsEngine.Tests --filter "FullyQualifiedName~UnifiedBytecodeProductionEligibilityTests&FullyQualifiedName~Evaluate_ClassDeclarationInstruction_DeclinesBeforeCompile"` |
| `ParameterVarDeclarationDependency` | Activation descriptor gate for parameter `var` declarations without initializer outside the with-backed materialized-activation lane | Existing hoisting route | Hoisting / dynamic-name lane | `rtk dotnet test tests/Asynkron.JsEngine.Tests --filter "FullyQualifiedName~UnifiedBytecodeProductionInvocationTests&FullyQualifiedName~OrdinarySyncActivationBlockers_DeclineUnifiedBytecodeAndFallBack"` |
| `MaterializedActivationDependency` | Activation descriptor gate for materialized activation bindings not proven safe by `ActivationSlotShape.MaterializedBindingNames` and the with-backed dynamic-name lane | Existing execution-plan route | Materialized activation lane | `rtk dotnet test tests/Asynkron.JsEngine.Tests --filter "FullyQualifiedName~UnifiedBytecodeProductionEligibilityTests&FullyQualifiedName~Evaluate_OrdinarySyncActivationDescriptorBlockers_DeclineBeforeCompile"` |
| `CallDependency` | Direct eval, out-of-boundary call-target preparation, complex call arguments, and descriptor-level non-parameter callee calls | Existing sync IR call route | Wider call invocation lane | `rtk dotnet test tests/Asynkron.JsEngine.Tests --filter "FullyQualifiedName~UnifiedBytecodeProductionEligibilityTests&FullyQualifiedName~Evaluate_CallWithComplexTemplateLiteralSubstitution_DeclinesCallDependency"` |
| `DynamicLookupDependency` | Unresolved identifier loads/stores/typeof/update outside the with-backed dynamic-name path | Existing sync IR / environment lookup route | Dynamic-name lane | `rtk dotnet test tests/Asynkron.JsEngine.Tests --filter "FullyQualifiedName~UnifiedBytecodeProductionEligibilityTests&FullyQualifiedName~Evaluate_DynamicIdentifierLookup_DeclinesWithDynamicLookupDependency"` |
| `PropertyReadCandidateRequiresVmSupport` | Candidate recognition bucket for property-read shapes that should not route until compiler and VM support land in the same slice | Existing sync IR property route | Property read VM-support lane | `rtk dotnet test tests/Asynkron.JsEngine.Tests --filter "FullyQualifiedName~ExpressionProgramCoverageMapTests&FullyQualifiedName~UnifiedBytecodeExpansionContract_ListsRequiredHeadingsAndCurrentEnums"` |
| `PropertyReadBoundaryOutOfScope` | Named/computed property reads outside the admitted activation-resolved boundaries | Existing sync IR property route | Property read widening lane | `rtk dotnet test tests/Asynkron.JsEngine.Tests --filter "FullyQualifiedName~UnifiedBytecodeProductionEligibilityTests&FullyQualifiedName~Evaluate_ComputedPropertyReadOutsideFirstBoundary_DeclinesWithBoundaryCode"` |
| `PropertyWriteDependency` | Property writes and compound/logical property writes outside the admitted direct property-write shapes and simple nested named receiver assignment shape | Existing sync IR property-write route | Property write widening lane | `rtk dotnet test tests/Asynkron.JsEngine.Tests --filter "FullyQualifiedName~UnifiedBytecodeProductionEligibilityTests&FullyQualifiedName~Evaluate_ThisBaseMultiHopNamedCompoundPropertyWrite_DeclinesWithBoundaryCode"` |
| `PropertyUpdateDependency` | Property and identifier update expressions outside the admitted direct update and simple nested named receiver update boundary | Existing sync IR update route | Property update lane | `rtk dotnet test tests/Asynkron.JsEngine.Tests --filter "FullyQualifiedName~UnifiedBytecodeProductionEligibilityTests&FullyQualifiedName~Evaluate_NestedNamedPropertyUpdate_AcceptsOwnedPropertyOpcodes"` |
| `DeleteDependency` | `delete` expressions outside the admitted ordinary named/computed property delete lane and the with-backed dynamic-name delete lane | Existing sync IR delete route | Delete semantics lane | `rtk dotnet test tests/Asynkron.JsEngine.Tests --filter "FullyQualifiedName~UnifiedBytecodeProductionEligibilityTests&FullyQualifiedName~Evaluate_PropertyReadAdjacentFamilies_DeclineWithExplicitCodes"` |
| `SuperPropertyDependency` | Super property reads/writes/updates and out-of-boundary super call targets | Existing class / constructor route | Super semantics lane | `rtk dotnet test tests/Asynkron.JsEngine.Tests --filter "FullyQualifiedName~UnifiedBytecodeProductionEligibilityTests&FullyQualifiedName~Evaluate_SuperPropertyAccess_DeclinesWithExplicitCode"` |
| `OptionalChainDependency` | Optional chains outside the admitted optional property-read, optional-call, and exact optional computed delete boundaries | Existing sync IR optional-chain route | Optional-chain widening lane | `rtk dotnet test tests/Asynkron.JsEngine.Tests --filter "FullyQualifiedName~UnifiedBytecodeProductionEligibilityTests&FullyQualifiedName~Evaluate_OptionalSpreadCallExpressionPlan_DeclinesWithOptionalChainDependency"` |
| `ObjectLiteralOrSpreadDependency` | Object methods/accessors, non-simple object spread sources, unsupported array spread, and spread construct arguments | Existing sync IR literal/spread route | Literal/spread lane | `rtk dotnet test tests/Asynkron.JsEngine.Tests --filter "FullyQualifiedName~UnifiedBytecodeProductionEligibilityTests&FullyQualifiedName~Evaluate_NonSimpleSourceArraySpread_DeclinesWithExplicitCode"` |
| `PrivateFieldDependency` | Private member reads/writes/updates and `#name in obj` represented as named-property or private-field ops | Existing private-name route | Private-name lane | `rtk dotnet test tests/Asynkron.JsEngine.Tests --filter "FullyQualifiedName~UnifiedBytecodeProductionEligibilityTests&FullyQualifiedName~Evaluate_PrivateFieldIn_DeclinesWithExplicitCode"` |
| `ForInDriverStateDependency` | Unsupported for-in driver state such as awaited object source | Existing for-in IR driver route | Driver-state lane | `rtk dotnet test tests/Asynkron.JsEngine.Tests --filter "FullyQualifiedName~UnifiedBytecodeProductionEligibilityTests&FullyQualifiedName~IsSupportedForInInit_AwaitedSource_Declines"` |
| `DestructuringDependency` | Binding declarations, unsupported destructuring driver shapes, computed/default/nested declaration destructuring, and destructuring targets outside the admitted driver or descriptor-backed assignment lanes | Existing destructuring IR route | Destructuring driver lane | `rtk dotnet test tests/Asynkron.JsEngine.Tests --filter "FullyQualifiedName~UnifiedBytecodeProductionEligibilityTests&FullyQualifiedName~Evaluate_UnsupportedDestructuringDriverShapes_DeclineWithExplicitReason"` |
| `LabelControlFlow` | Labeled break/continue that exits an intervening iterator/for-in driver loop not directly targeted by the abrupt jump | Existing IR loop-control route | Multi-driver labeled cleanup lane | `rtk dotnet test tests/Asynkron.JsEngine.Tests --filter "FullyQualifiedName~UnifiedBytecodeProductionEligibilityTests&FullyQualifiedName~Evaluate_LabeledBreakCrossingDriverLoop_DeclinesWithLabelControlFlow"` |
| `UnsupportedPlanShape` | Missing activation slot metadata, unsupported instruction families, unsupported compiler shapes, unsupported resumable opcodes, and unknown production opcode defaults | Existing execution-plan route | Statement/control-flow ownership lane | `rtk dotnet test tests/Asynkron.JsEngine.Tests --filter "FullyQualifiedName~ExpressionProgramCoverageMapTests&FullyQualifiedName~UnifiedBytecodeExpansionContract_ListsRequiredHeadingsAndCurrentEnums"` |
| `CallInvocationBoundary` | Plan-structural call invocation outside the currently executable call boundary, separate from descriptor-level `CallDependency` | Existing sync IR call route | Wider call invocation lane | `rtk dotnet test tests/Asynkron.JsEngine.Tests --filter "FullyQualifiedName~ExpressionProgramCoverageMapTests&FullyQualifiedName~UnifiedBytecodeExpansionContract_ListsRequiredHeadingsAndCurrentEnums"` |

### Sync Production Pre-Gate Rows

These rows are owned by
`TypedAstEvaluator.SyncFunctionInvoker.CanUseProductionUnifiedBytecodeFastPath`.
They decline before `UnifiedBytecodeProductionEligibility.Evaluate`, so they do
not always have a `UnifiedBytecodeProductionDeclineCode`.

| Pre-gate key | Owning source / current example | Current fallback route | Planned batch / lane | Proof command |
|---|---|---|---|---|
| `pre-gate:newTarget` | `!newTarget.IsUndefined` for constructor-style invocation | Constructor / sync IR route | Constructor boundary lane | `rtk dotnet test tests/Asynkron.JsEngine.Tests --filter "FullyQualifiedName~UnifiedBytecodeProductionInvocationTests"` |
| `pre-gate:IsClassConstructor` | Class constructor invokers, including base constructors | Constructor route | Constructor boundary lane | `rtk dotnet test tests/Asynkron.JsEngine.Tests --filter "FullyQualifiedName~UnifiedBytecodeProductionInvocationTests"` |
| `pre-gate:IsArrowFunction` | Arrow functions, especially lexical `this` / `new.target` shapes | Existing arrow invocation route | Arrow route lane | `rtk dotnet test tests/Asynkron.JsEngine.Tests --filter "FullyQualifiedName~UnifiedBytecodeProductionInvocationTests"` |
| `pre-gate:IsAsyncLike` | Async and formerly-async ordinary invokers | Async route | Resumable async/generator lane | `rtk dotnet test tests/Asynkron.JsEngine.Tests --filter "FullyQualifiedName~UnifiedBytecodeProductionEligibilityTests&FullyQualifiedName~Evaluate_AsyncLikeActivation_DeclinesBeforePlanInspection"` |
| `pre-gate:IsGenerator` | Generator functions before ordinary sync production routing | Generator route | Resumable async/generator lane | `rtk dotnet test tests/Asynkron.JsEngine.Tests --filter "FullyQualifiedName~UnifiedBytecodeProductionEligibilityTests&FullyQualifiedName~Evaluate_GeneratorActivation_DeclinesBeforePlanInspection"` |
| `pre-gate:IsDefaultDerivedConstructor` | Default derived class constructor setup | Constructor route | Constructor boundary lane | `rtk dotnet test tests/Asynkron.JsEngine.Tests --filter "FullyQualifiedName~UnifiedBytecodeProductionInvocationTests"` |
| `pre-gate:hasParameterExpressions` | Default/rest/destructured parameter expressions | Existing parameter environment route | Parameter semantics lane | `rtk dotnet test tests/Asynkron.JsEngine.Tests --filter "FullyQualifiedName~UnifiedBytecodeProductionInvocationTests"` |
| `pre-gate:hasOnlySimpleIdentifierParameters` | Non-simple parameter lists | Existing parameter environment route | Parameter semantics lane | `rtk dotnet test tests/Asynkron.JsEngine.Tests --filter "FullyQualifiedName~UnifiedBytecodeProductionInvocationTests"` |
| `pre-gate:usesArguments` | Functions that read `arguments` | Existing arguments-object route | Arguments object lane | `rtk dotnet test tests/Asynkron.JsEngine.Tests --filter "FullyQualifiedName~UnifiedBytecodeProductionEligibilityTests&FullyQualifiedName~Evaluate_ArgumentsAccess_DeclinesWithArgumentsDependency"` |
| `pre-gate:needsArgumentsBinding` | Sloppy mapped arguments binding when not admitted by with-backed dynamic-name routing | Existing arguments-object route | Arguments object lane | `rtk dotnet test tests/Asynkron.JsEngine.Tests --filter "FullyQualifiedName~UnifiedBytecodeProductionInvocationTests"` |
| `pre-gate:functionDeclarationsOrParameterVar` | Hoistable function declarations or parameter `var` declarations without initializer unless with-backed dynamic-name routing owns the materialized activation | Existing hoisting route | Hoisting / dynamic-name lane | `rtk dotnet test tests/Asynkron.JsEngine.Tests --filter "FullyQualifiedName~UnifiedBytecodeProductionInvocationTests"` |
| `pre-gate:allowIdentifierCache` | Identifier cache disabled outside the with-backed dynamic-name lane | Existing environment lookup route | Dynamic-name lane | `rtk dotnet test tests/Asynkron.JsEngine.Tests --filter "FullyQualifiedName~UnifiedBytecodeProductionEligibilityTests&FullyQualifiedName~Evaluate_DynamicIdentifierLookup_DeclinesWithDynamicLookupDependency"` |
| `pre-gate:lexicalThisEnvironment` | Lexical `this` environment captured by arrow / class-derived shapes | Existing lexical-this route | This-binding lane | `rtk dotnet test tests/Asynkron.JsEngine.Tests --filter "FullyQualifiedName~UnifiedBytecodeProductionInvocationTests"` |
| `pre-gate:PrivateNameScope` | Active private-name scope on the invoker | Existing private-name route | Private-name lane | `rtk dotnet test tests/Asynkron.JsEngine.Tests --filter "FullyQualifiedName~UnifiedBytecodeProductionEligibilityTests&FullyQualifiedName~Evaluate_PrivateFieldIn_DeclinesWithExplicitCode"` |
| `pre-gate:capturedPrivateNameScopes` | Captured private-name scopes from enclosing classes | Existing private-name route | Private-name lane | `rtk dotnet test tests/Asynkron.JsEngine.Tests --filter "FullyQualifiedName~UnifiedBytecodeProductionInvocationTests"` |
| `pre-gate:superConstructor` | Derived-constructor super binding dependency outside the bounded production constructor admission path | Constructor / super route | Super / constructor lane | `rtk dotnet test tests/Asynkron.JsEngine.Tests --filter "FullyQualifiedName~UnifiedBytecodeProductionEligibilityTests&FullyQualifiedName~Evaluate_SuperCall_AcceptsSuperConstructInvocationBoundary"` |
| `pre-gate:superPrototype` | `super.prop` dependency on method home object state | Existing super route | Super semantics lane | `rtk dotnet test tests/Asynkron.JsEngine.Tests --filter "FullyQualifiedName~UnifiedBytecodeProductionEligibilityTests&FullyQualifiedName~Evaluate_SuperPropertyAccess_DeclinesWithExplicitCode"` |
| `pre-gate:instanceFields` | Class instance-field initialization state | Constructor / class route | Class fields lane | `rtk dotnet test tests/Asynkron.JsEngine.Tests --filter "FullyQualifiedName~UnifiedBytecodeProductionInvocationTests"` |
| `pre-gate:functionNameParameterConflict` | Named function expression whose name also appears as a parameter | Existing function-name environment route | Function-name environment lane | `rtk dotnet test tests/Asynkron.JsEngine.Tests --filter "FullyQualifiedName~UnifiedBytecodeProductionInvocationTests"` |
| `pre-gate:activationSlotShape` | `CanUseProductionUnifiedBytecodePlanShape` requires activation slots matching root scope, layout, and parameter-slot length unless with-backed dynamic names own the materialized activation | Existing execution-plan route | Slot-layout lane | `rtk dotnet test tests/Asynkron.JsEngine.Tests --filter "FullyQualifiedName~ExpressionProgramCoverageMapTests&FullyQualifiedName~UnifiedBytecodeExpansionContract_ListsRequiredHeadingsAndCurrentEnums"` |

### Prototype Opcode Guard Rows

These rows are owned by
`UnifiedBytecodeProductionEligibility.TryFindPrototypeOnlyOpcode`. The guard is
the final post-compile production subset check before VM entry.

| Prototype guard key | Owning source / current example | Current fallback route | Planned batch / lane | Proof command |
|---|---|---|---|---|
| `prototype-guard:Binary` | `Binary` opcodes currently admit the selector-owned production subset: arithmetic (`+`, `-`, `*`, `/`, `%`, `**`), equality/comparison (`==`, `!=`, `===`, `!==`, `<`, `<=`, `>`, `>=`), bitwise/shift (`&`, `|`, `^`, `<<`, `>>`, `>>>`), and relational/object tests (`in`, `instanceof`); binary operators outside that subset (for example `&&`, `\|\|`, `??`) decline as `UnsupportedPlanShape` | Existing expression route | Binary operator widening lane | `rtk dotnet test tests/Asynkron.JsEngine.Tests --filter "FullyQualifiedName~UnifiedBytecodeProductionEligibilityTests&FullyQualifiedName~Evaluate_PropertyReadAdjacentFamilies_DeclineWithExplicitCodes"` |
| `prototype-guard:Jump` | `Jump` and `JumpWithDriverCleanup` are admitted only for compiler-owned branch, loop, and driver cleanup shapes | Existing control-flow route if an unowned jump shape is introduced | Control-flow lane | `rtk dotnet test tests/Asynkron.JsEngine.Tests --filter "FullyQualifiedName~UnifiedBytecodeProductionEligibilityTests&FullyQualifiedName~Evaluate_LabeledBreakCrossingDriverLoop_DeclinesWithLabelControlFlow"` |
| `prototype-guard:JumpIfFalse` | `JumpIfFalse` and short-circuit conditional jump opcodes are admitted only for proven compiler-owned expression and statement control flow | Existing control-flow route if an unowned conditional shape is introduced | Control-flow lane | `rtk dotnet test tests/Asynkron.JsEngine.Tests --filter "FullyQualifiedName~UnifiedBytecodeProductionEligibilityTests&FullyQualifiedName~Evaluate_ConditionalExpression_ThisPropertyConditionAndArms_DeclinesWithPropertyReadBoundaryOutOfScope"` |
| `prototype-guard:DefaultUnsupportedOpcode` | Any future `UnifiedBytecodeOpCode` not explicitly listed in the switch declines as `UnsupportedPlanShape` until the selector, compiler, VM, and proof pack are updated together | Existing execution-plan route | Opcode ownership lane | `rtk dotnet test tests/Asynkron.JsEngine.Tests --filter "FullyQualifiedName~ExpressionProgramCoverageMapTests&FullyQualifiedName~UnifiedBytecodeExpansionContract_ListsRequiredHeadingsAndCurrentEnums"` |

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

## Production This-Binding Boundary
- Ordinary sync functions that reference `this` are admitted to the production
  unified-bytecode path. The `_homeObject is not null` blanket rejection was
  removed from `CanUseProductionUnifiedBytecodeFastPath` in PR #2633 (ADR 0279).
- `_homeObject` is set for all class methods and object-literal methods defined
  with method-definition syntax. A class method is admitted when its plan body
  contains no super-property opcodes. The existing property-write boundary still
  applies: compound read-then-write is admitted for the named-property shape
  (activation-resolved base — `this.prop` or `box.prop`, named key, production
  binary operator e.g. `+=`, simple RHS) via
  `TryIsFirstBoundaryNamedCompoundPropertyWriteCandidate`, and logical-assignment
  member writes (`&&=`, `||=`, `??=`) via
  `TryIsFirstBoundaryNamedLogicalPropertyWriteCandidate`. Direct computed logical
  writes (`box[key] &&= y`, `box[key] ||= y`, `box[key] ??= y`) are also admitted
  when they match `TryIsFirstBoundaryComputedLogicalPropertyWriteCandidate`
  (activation-resolved base, simple key, simple RHS, non-optional/non-private
  target). Simple nested named receiver assignments and updates
  (`box.child.value = y`, `box.child.value++`) are admitted through
  `TryIsFirstBoundaryNestedNamedPropertyWriteCandidate` and
  `TryIsFirstBoundaryNestedNamedPropertyUpdateCandidate`, which compile the
  receiver chain as owned `GetNamedProperty` opcodes before the final
  `SetNamedProperty` / `UpdateNamedProperty`. Nested named receiver chains
  ending in a simple computed delete (`delete box.child[key]`) are admitted by
  `TryIsFirstBoundaryComputedPropertyDeleteCandidate`; optional computed delete
  chains are admitted only for `delete box?.child[key]` and
  `delete box.child?.[key]` shapes with activation-resolved receivers, supported
  computed-key spans, and compiler-owned nullish short-circuit-to-true lowering
  (ADR 0317).
  The compiler emits the named receiver reads and final `DeleteComputedProperty`,
  while the VM's descriptor-aware delete helper owns strict/sloppy results.
  Retained declines include simple optional computed deletes without a named
  receiver hop (`delete box?.[key]`), chained optional delete neighbors,
  private/super property access, dynamic lookup, richer unowned computed keys,
  deeper compound/logical property chains
  (`box.child.value += …`, `box.child.value &&= …`), and computed-expression
  keys (`box[key+suffix] += …`).
  Note: slot-identifier logical assignment (`x &&= y`) remains admitted.
- The plan-level `SuperPropertyDependency` decline in
  `UnifiedBytecodeProductionEligibility.TryFindExpressionDecline` is the safety
  net for class methods that use super-property reads, writes, and updates.
  Super-member calls are admitted only through the first call boundary with
  `PrepareNamedSuperCallTarget` / `PrepareComputedSuperCallTarget`.
  Super-property reads, writes, and updates (`GetNamedSuperProperty`, `GetComputedSuperProperty`,
  `SetNamedSuperProperty`, `SetComputedSuperProperty`,
  `UpdateNamedSuperProperty`, `UpdateComputedSuperProperty`,
  `EnsureSuperReference`) still decline before VM execution.
- Sloppy-mode `this` coercion is handled before VM entry: `boundThis` is
  computed via `CoerceThisValueForNonStrict` in `TryInvokeProductionUnifiedBytecode`,
  so the VM's `LoadThis` opcode always receives the correctly coerced value.
- Arrow functions still decline via the `IsArrowFunction` and
  `_lexicalThisEnvironment is not null` pre-gates.
- `HasThisDependency` in `UnifiedBytecodeProductionActivationDescriptor` is
  never set for ordinary sync functions (defaults to `false`). It is kept as an
  explicit gate for future shapes where `this` must be declined.

## Production Loop-Control Boundary
- Current production control-flow support is compiler-owned, not
  source-syntax-owned.
- Accepted loop-control shapes include branch joins, direct branches, canonical
  condition-first loop backedges, unlabeled `BreakInstruction` and
  `ContinueInstruction` target jumps, simple for-style update continue targets,
  and simple do-while consequent backedges proven by PR #2489.
- Labeled breakable control flow is admitted (ADR 0285): labeled statements,
  labeled loops, labeled block `break`, and labeled `break`/`continue` route
  through the same compiler-owned resolved-target path as the unlabeled case.
  Label resolution is not a source-syntax permission — a labeled construct
  routes whenever its unlabeled IR topology would route.
- The one labeled shape still declined with `LabelControlFlow` is a labeled
  `break`/`continue` that transfers control out of an enclosing iterator/for-in
  driver loop it is not directly targeting (driver-crossing). The VM's
  single-level driver cleanup only closes the driver whose break target equals
  the abrupt jump target, so an intervening inner iterator would be leaked;
  multi-driver labeled cleanup is the next loop-control widening frontier.
- Unsupported complex loop/control-flow shapes must decline before VM execution
  instead of falling back from inside `UnifiedBytecodeVirtualMachine`.
- The pre-ADR 0253 break/continue-only decline taxonomy is retired:
  PR #2489 removed the blanket production pre-scan that rejected every
  break/continue instruction, and no ordinary sync site should produce a
  break/continue-specific decline code.

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
  instructions, direct eval / unsupported dynamic lookup, captured activation,
  per-iteration bindings, and `using` / `await using` remain pre-VM declines.

## Production With-Backed Dynamic Name Boundary
- Current production dynamic-name support is with-backed and compiler-gated.
  The compiler may admit otherwise-supported sync functions that enter a
  `with` environment and then use dynamic identifier load, store, update,
  typeof, delete, assignment-reference, or receiver-aware identifier-call
  opcodes.
- `EnterWith` and `LeaveWith` create and unwind object environments through the
  existing `JsEnvironment` with-binding helpers. The VM-owned opcodes must
  preserve proxy traps, `Symbol.unscopables`, strict/sloppy assignment, delete,
  and receiver binding without falling back into mixed execution.
- The sync invocation bridge must create an activation environment that already
  contains function-scoped var bindings before VM execution, so a callee's
  hoisted var names shadow an outer `with` object instead of falling through to
  dynamic lookup.
- `PrepareDynamicIdentifierCallTarget` must resolve an active with binding
  regardless of identifier-cache state. When the binding exists, the receiver is
  the with binding object and the callee is read through that captured binding.
- Dynamic identifier support does not admit direct eval source execution,
  unresolved non-with dynamic activation, captured dynamic activation,
  arguments-object dependencies, or async/generator functions. Those shapes
  still decline before VM execution.

## Production Call Invocation Boundary
- Current executable call support covers
  activation-resolved identifier calls, direct named member calls whose
  optional-free named receiver chain is activation-resolved, and direct computed
  member calls whose receiver chain remains inside the shallow computed-call
  boundary. Arguments may be simple literal or slot operands, or simple
  array/object literal spans (`[a, b]`, `{x: a, y: b}`), including object
  literal spread entries whose spread source is a simple operand
  (`{...source}`), with no computed keys, no methods, no accessors, and no name
  inference (gh2705, ADR 0290). Computed member keys must also be simple
  literal or slot operands. For accepted member receiver chains, the call
  receiver is the final resolved receiver object, not the root object.
- Synchronous spread calls are admitted (gh2676): `f(...args)`, `f(...a, ...b)`,
  `obj.method(...args)`, and mixed `f(a, ...b, c)`. Each argument (positional or
  spread) lowers to one value-producing load; the `CallInvocationBoundary`
  operand low 16 bits hold the pushed argument value count and the high bits hold
  a spread-mask reference (`spreadMaskIndex + 1`, where `0` means no spread). The
  VM flattens spread iterables left-to-right at the boundary via the shared
  `TypedAstEvaluator.EnumerateSpread` helper, preserving iteration order and
  side-effects, then invokes through the existing callable helpers with
  receiver-as-`this`. Direct eval, super spread, and optional spread calls stay
  declined.
- Synchronous construct calls are admitted (gh2690 plus the construct-boundary
  widening): `new F(...)`, `new F(...args)`, `new box.Ctor(...)`, and
  `new box[key](...)` when the constructor/key/argument subexpressions are
  already production-safe. The constructor value and each logical argument are
  pushed left-to-right by their own loads (no receiver/`this`), then a single
  `ConstructInvocationBoundary` opcode invokes `[[Construct]]` with the
  constructor itself as `new.target`, mirroring the spec-conformant construct
  reference helper. Its operand uses the same low-16-bit argument count plus
  high-bit spread-mask reference encoding as `CallInvocationBoundary`; the VM
  flattens spread iterables at the construct boundary before calling
  `ReflectHelper.Construct`. A non-constructor target throws `TypeError` at the
  boundary.
- Derived-constructor non-spread `super(...)` calls are admitted through
  `SuperConstructInvocationBoundary`. The VM resolves the dynamic super
  constructor from the current super binding, invokes `[[Construct]]` with the
  caller's `new.target`, initializes `this`, and preserves the existing
  double-super-call `ReferenceError`. Spread super constructs
  (`super(...args)`) stay declined as spread construct arguments.
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
- Accepted named super-member call programs use `PrepareNamedSuperCallTarget`
  followed by `CallInvocationBoundary`; the VM resolves the named property
  against the active super binding and invokes with the derived receiver as
  `this`.
- Accepted computed super-member call programs use
  `PrepareComputedSuperCallTarget` followed by `CallInvocationBoundary`; the VM
  evaluates the key before resolving the super-member callee and invokes with
  the derived receiver as `this`.
- Accepted optional member-call programs use `PrepareNamedOptionalCallTarget`
  or `PrepareComputedOptionalCallTarget` followed by `CallInvocationBoundary`.
  Both opcodes pack a call-target constant index (low 16 bits) and a nullish
  short-circuit jump target (high 16 bits) into a single operand. The VM checks
  the receiver or callee for nullish before evaluation proceeds; if nullish the
  stack top is replaced with `Undefined` and execution jumps past the call
  boundary. Three patterns are admitted: receiver-optional named calls
  (`box?.read(args)`), callee-optional named calls (`box.read?.(args)`), and
  callee-optional computed calls (`box[key]?.(args)`). The `IsOptionalReceiverCheck`
  flag in `UnifiedBytecodeCallTarget` distinguishes the two named variants.
  (Optional calls admitted as of gh2689; ADR 0289.)
- Direct eval, spread super constructs, super property reads/writes/updates,
  private member targets, arguments-object dependencies, unsupported
  non-with dynamic lookup, deeper computed-member call receiver chains,
  complex receiver/key shapes, spread-onto-optional calls, and
  receiver-binding-sensitive adjacent families still decline before VM
  execution. (Synchronous spread calls are admitted as of gh2676; synchronous
  non-spread construct calls are admitted as of gh2690.)
- Accepted programs must still satisfy the no-mixed-execution rule: no
  callback into `ExpressionProgram`, `ExecutionPlanRunner`, or AST evaluation
  is allowed from `UnifiedBytecodeVirtualMachine`.

## Production Driver-State Boundary
- Current driver-state support is VM-owned and descriptor-backed. Driver
  instructions compile to `UnifiedBytecodeDriverDescriptor` entries rather than
  unrelated stack-op translations or AST payloads.
- `break` compiles as `JumpWithDriverCleanup`; before transferring control the
  VM closes active driver states whose descriptor break target matches the jump
  target. `continue` remains a plain same-loop `Jump`.
- Accepted sync iterator driver shapes include `IteratorInit`,
  `IteratorMoveNext`, and `IteratorClose` when the iterable source is lowered
  to synchronous expression bytecode, the iterator kind is `Sync`, and no
  awaited source program is required. Slice A (#2678) also admits a TDZ head
  environment (`for (const x of …)` / `for (let x of …)`): the compiler emits a
  `TdzHeadInit` instruction that resolves the head bindings to flat slots, and
  the VM marks them `JsValue.Uninitialized` before the source is evaluated so a
  read of the head binding inside the source throws a `ReferenceError`.
- Accepted for-in driver shapes include `ForInInit` and `ForInMoveNext` when
  the object source is lowered to synchronous expression bytecode and no awaited
  source program is required. Slice A (#2678) also admits a TDZ head environment
  (`for (const k in …)` / `for (let k in …)`) via the same `TdzHeadInit`
  mechanism. The VM owns driver state and skips deleted properties during
  enumeration.
- Accepted array destructuring driver shapes include
  `ArrayDestructuringInit`, `ArrayDestructuringElement`,
  `ArrayDestructuringRest`, and `ArrayDestructuringClose` when the source is
  lowered to expression bytecode and element/rest targets resolve to unified
  flat slots. Normal and abrupt cleanup close the destructuring iterator from
  VM-owned driver state.
- Accepted object destructuring driver shapes include
  `ObjectDestructuringInit`, `ObjectDestructuringProperty`,
  `ObjectDestructuringRest`, and `ObjectDestructuringClose` when the source is
  lowered to expression bytecode, all property keys are static identifiers (no
  computed keys), there are no defaults or nested patterns, and every property
  and rest target resolves to a unified flat slot. The VM coerces the source
  with `ToObject`, reads properties in source order (observable getter side
  effects preserved), and a trailing rest target collects the remaining own
  enumerable keys minus the consumed ones. Abrupt completion (a non-coercible
  source or a throwing getter) closes the VM-owned driver state.
- Accepted expression-level assignment destructuring shapes that lower through
  `ApplyBindingTarget` are admitted through a descriptor-backed
  `BindingTargetProgram` constant. The production VM duplicates the assignment
  RHS per expression bytecode stack semantics, applies the binding target in
  `BindingMode.Assign`, and syncs unified slots with the activation environment
  before and after the bridge so existing computed-key, default, rest, nested,
  TDZ, and iterator-closing semantics stay centralized.
- Object destructuring with computed/dynamic keys, defaults, nested patterns,
  or rest targets that cannot resolve to unified slots outside the
  descriptor-backed assignment lane, async iterator drivers, awaited driver
  sources, dynamic-name shapes, and targets that cannot resolve to unified
  slots still decline before VM execution. Sync-driver TDZ head environments
  are now admitted (Slice A, #2678); async-kind and awaited-source TDZ heads
  remain declined.

## Production Resumable Boundary
- Current production resumable support is a separate async/generator route with
  VM-owned suspension state. Accepted programs preserve program counter,
  operand stack, slots, pending await promise, pending resume payload, and
  completion state inside the unified runtime.
- Accepted resumable generator shapes include simple `yield` / resume-value
  storage bodies that compile through `Yield` and `StoreResumeValue`, plus
  sync `yield*` bodies whose delegated `.next()`, `.return(value)`, and
  `.throw(value)` resume behavior is owned by the VM `YieldStar` opcode.
- Accepted resumable async shapes include simple awaited discard and awaited
  return bodies that compile through `AwaitAndDiscard` and `AwaitedReturn`.
- `this`-dependent async and generator programs are admitted (resumable-route
  counterpart to the ordinary sync `this` support; see Production This-Binding
  Boundary above and ADR 0283). The strict/sloppy-coerced `boundThis` is
  computed in the async and sync-generator invokers via
  `CoerceThisValueForNonStrict` and stored on `UnifiedBytecodeResumeState` at
  construction so it survives suspension/resume across `yield`/`await`. The
  resumable `LoadThis` opcode pushes `state.ThisValue`. Property reads such as
  `this.x` remain outside the resumable opcode set and decline independently of
  the `this`-binding gate.
- Captured/dynamic activation, arguments objects, `new.target`, calls,
  dynamic lookup, labels, iterator/destructuring drivers, unsupported
  expression payloads, and unmodeled statement families still decline before
  VM execution.
- Async-generator `yield*`, awaited delegated sources, and unsupported
  delegated expression payloads remain outside production resumable routing
  until their promise/async-iterator settlement semantics are modeled by the VM.

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
- `ObjectDestructuringInitInstruction`,
  `ObjectDestructuringPropertyInstruction`,
  `ObjectDestructuringRestInstruction`, and
  `ObjectDestructuringCloseInstruction` are eligible only for the lowered
  direct-slot object destructuring model described above (static keys,
  identifier targets, no defaults, no nested patterns, optional identifier
  rest). Computed/dynamic-name keys, defaults, nested patterns, and non-slot
  targets keep the generic `BindingVariableDeclarationInstruction` path and
  still decline with `DestructuringDependency`. ADR
  [`0284`](adrs/0284-keep-unified-bytecode-object-destructuring-model-first-and-static-key-owned.md)
  records the model-first decision and admit/decline boundary.
- `ExpressionOpKind.ApplyBindingTarget` is eligible for ordinary sync
  production assignment destructuring when the expression compiler can lift the
  lowered `BindingTargetProgram` into the unified program descriptor table.
  This lane preserves existing binding-target semantics as a bridge; it does
  not make generic binding declarations or unsupported destructuring driver
  shapes production-eligible.
- Decision for this lane: model-first. Any future widening must preserve
  explicit driver-state descriptors and pre-VM declines for shapes that would
  require mixed IR/AST execution.

## Ranked Next Unsupported Buckets (current boundary)
1. Wider call invocation remains the highest-impact unsupported bucket.
   Synchronous spread calls are now admitted (gh2676). Optional calls are
   now admitted (gh2689, ADR 0289): `box?.read(args)`, `box.read?.(args)`,
   and `box[key]?.(args)`. Synchronous construct calls
   (`new F(...)`, spread arguments, and member/computed constructor targets)
   are now admitted (gh2690, ADR 0286 plus construct-boundary widening). The
   first bounded super invocation shapes are now admitted too (PR #2862, ADR
   0307): non-spread derived-constructor `super(...)` plus named/computed
   super-member calls. Simple array and
   object literal arguments (`fn([a, b])`, `fn({x: a})`) are now admitted
   (gh2705, ADR 0290). Simple object literal spread entries are now admitted
   through the `ObjectSpread` opcode when their spread source is a simple
   operand; non-simple spread sources, computed keys, methods, accessors, name
   inference, and private names continue to decline.
   Direct eval, super-property reads/writes/updates, spread super constructs,
   spread-onto-optional,
   arguments-object dependencies, unsupported non-with dynamic lookup,
   private member targets, complex receiver/key shapes, and
   receiver-binding-sensitive adjacent families beyond the direct
   activation-resolved and with-backed
   dynamic-identifier boundaries must still decline before VM execution.
2. Driver-state widening is next. Sync-driver TDZ head environments
   (`for (const x of …)` / `for (let k in …)`) are now admitted via the
   `TdzHeadInit` instruction (Slice A, #2678; see ADR 0288). Async iterator
   drivers and awaited iterator/for-in sources remain outside the admitted
   boundary and must decline before VM execution.
3. Destructuring widening is still model-first. Simple array and object
   destructuring driver shapes are admitted (static keys, identifier targets,
   no defaults/nested patterns, optional identifier rest), and expression-level
   assignment destructuring that lowers through `ApplyBindingTarget` is admitted
   through the descriptor-backed binding-target bridge. Generic binding
   declarations, unsupported driver shapes, and targets outside the direct-slot
   or descriptor-backed assignment lanes remain outside the admitted boundary
   (`DestructuringDependency`).
4. Dynamic lookup families remain outside the admitted boundary
   (`DynamicLookupDependency`) except for the explicit with-backed dynamic name
   slice above. Direct eval source execution, unresolved non-with lookup
   shapes, and captured dynamic activation still decline before VM execution.
5. Label-dependent control flow is now admitted (ADR 0285): labeled statements,
   labeled loops, labeled block `break`, and labeled `break`/`continue` route
   through the compiler-owned resolved-target path. The remaining
   `LabelControlFlow` decline is narrow — a labeled `break`/`continue` that
   crosses (exits) an enclosing iterator/for-in driver loop it is not directly
   targeting. Multi-driver labeled cleanup is the next loop-control widening
   frontier.

## Proof Commands
```bash
rtk dotnet test tests/Asynkron.JsEngine.Tests --filter "FullyQualifiedName~ExpressionProgramCoverageMapTests"
rtk dotnet test tests/Asynkron.JsEngine.Tests --filter "FullyQualifiedName~UnifiedBytecodeProductionEligibilityTests"
rtk dotnet test tests/Asynkron.JsEngine.Tests --filter "FullyQualifiedName~UnifiedBytecodePrototypeTests"
rtk dotnet test tests/Asynkron.JsEngine.Tests --filter "FullyQualifiedName~UnifiedBytecodeProductionInvocationTests"
rtk rg "EvaluateExpression\\(|ProfileEvaluateExpression\\(" src/Asynkron.JsEngine/Ast/TypedAstEvaluator.ExecutionPlanRunner*
rtk ./tools/profile forloop --memory
```
