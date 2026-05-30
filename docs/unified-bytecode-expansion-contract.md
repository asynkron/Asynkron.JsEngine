# Unified Bytecode Expansion Contract

Date: 2026-05-29
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
- `JumpWithDriverCleanup`
- `JumpIfFalse`
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
- `CallInvocationBoundary`
- `LoadFunctionLiteral`
- `Yield`
- `StoreResumeValue`
- `AwaitAndDiscard`
- `AwaitedReturn`
- `YieldStar`
- `LoadFunctionLiteral`
- `EnsureHasName`

### Production Decline Families (current)
- `None`
- `AsyncLikeFunction`
- `GeneratorFunction`
- `CapturedOrDynamicActivation`
- `ArgumentsObjectDependency`
- `ThisDependency` *(conditional — see Production This-Binding Boundary below; currently never triggered for ordinary sync, and no longer triggered on the resumable async/generator route — see Production Resumable Boundary and ADR 0283)*
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

## Production This-Binding Boundary
- Ordinary sync functions that reference `this` are admitted to the production
  unified-bytecode path. The `_homeObject is not null` blanket rejection was
  removed from `CanUseProductionUnifiedBytecodeFastPath` in PR #2633 (ADR 0279).
- `_homeObject` is set for all class methods and object-literal methods defined
  with method-definition syntax. A class method is admitted when its plan body
  contains no super-property opcodes. The existing property-write boundary still
  applies: `this.prop = expr_using_this_prop` (compound read-then-write) is
  declined by `PropertyWriteDependency`; only simple assignments within the
  existing boundary (e.g. `this.prop = slot/constant`) are admitted.
- The plan-level `SuperPropertyDependency` decline in
  `UnifiedBytecodeProductionEligibility.TryFindExpressionDecline` is the safety
  net for class methods that use `super`. Super-property reads, writes, and
  updates (`GetNamedSuperProperty`, `GetComputedSuperProperty`,
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
  array/object literal spans (`[a, b]`, `{x: a, y: b}`) with no spread elements,
  no holes, no computed keys, no methods, no accessors, and no name inference
  (gh2705, ADR 0290). Computed member keys must also be simple literal or slot
  operands. For accepted member receiver chains, the call receiver is the final
  resolved receiver object, not the root object.
- Synchronous spread calls are admitted (gh2676): `f(...args)`, `f(...a, ...b)`,
  `obj.method(...args)`, and mixed `f(a, ...b, c)`. Each argument (positional or
  spread) lowers to one value-producing load; the `CallInvocationBoundary`
  operand low 16 bits hold the pushed argument value count and the high bits hold
  a spread-mask reference (`spreadMaskIndex + 1`, where `0` means no spread). The
  VM flattens spread iterables left-to-right at the boundary via the shared
  `TypedAstEvaluator.EnumerateSpread` helper, preserving iteration order and
  side-effects, then invokes through the existing callable helpers with
  receiver-as-`this`. Direct eval, construct/super spread, and optional spread
  calls stay declined.
- Synchronous non-spread construct calls are admitted (gh2690): `new F(...)`. The
  constructor value and each simple-operand argument are pushed left-to-right by
  their own loads (no receiver/`this`), then a single `ConstructInvocationBoundary`
  opcode (operand = pushed argument count) invokes `[[Construct]]` with the
  constructor itself as `new.target`, mirroring the spec-conformant construct
  reference helper. A non-constructor target throws `TypeError` at the boundary.
  Spread-onto-construct (`new F(...args)`), member-target constructs (`new a.b()`),
  non-simple argument constructs, and the super call family (`super(...)`,
  super-member call targets) stay declined — the latter is activation-gated by the
  derived-constructor decline in `SyncFunctionInvoker.CanUseProductionUnifiedBytecode`
  and so is unreachable (ADR 0286).
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
- Direct eval, construct/super calls,
  private/super member targets, arguments-object dependencies, unsupported
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
- Object destructuring with computed/dynamic keys, defaults, nested patterns,
  or rest targets that cannot resolve to unified slots, expression-level
  `ApplyBindingTarget` destructuring, async iterator drivers, awaited driver
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
  storage bodies that compile through `Yield` and `StoreResumeValue`.
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
- `YieldStar` remains outside production resumable routing until delegated
  `.return()` and `.throw()` resume behavior is modeled by the VM. Observable
  delegated abrupt-resume behavior continues through the existing IR generator
  path until that widening lands.

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
- Decision for this lane: model-first. Any future widening must preserve
  explicit driver-state descriptors and pre-VM declines for shapes that would
  require mixed IR/AST execution.

## Ranked Next Unsupported Buckets (current boundary)
1. Wider call invocation remains the highest-impact unsupported bucket.
   Synchronous spread calls are now admitted (gh2676). Optional calls are
   now admitted (gh2689, ADR 0289): `box?.read(args)`, `box.read?.(args)`,
   and `box[key]?.(args)`. Synchronous non-spread construct calls
   (`new F(...)`) are now admitted (gh2690, ADR 0286). Simple array and
   object literal arguments (`fn([a, b])`, `fn({x: a})`) are now admitted
   (gh2705, ADR 0290); holey arrays, spread elements, computed keys,
   methods, accessors, name inference, and private names continue to decline.
   Direct eval, super calls (`super(...)` and super-member call targets,
   activation-gated and unreachable in production), spread-onto-optional,
   spread-onto-construct, member-target/non-simple constructs,
   arguments-object dependencies, unsupported non-with dynamic lookup,
   private/super member targets, complex receiver/key shapes, and
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
   no defaults/nested patterns, optional identifier rest). Object destructuring
   with computed/dynamic-name keys, defaults, or nested patterns,
   expression-level `ApplyBindingTarget` destructuring, and targets that cannot
   resolve to unified slots remain outside the admitted boundary
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
