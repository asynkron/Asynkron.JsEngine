# Unified Bytecode Expansion Contract

Date: 2026-06-02
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
- For ordinary sync functions that pass both
  `CanUseProductionUnifiedBytecodeFastPath` and
  `UnifiedBytecodeProductionEligibility.Evaluate`, the production VM is the
  primary default attempt before dedicated simple-return/simple-binary IR
  shortcuts, `SyncIrCallTrampoline`, and generic `ExecutionPlanRunner`
  interpretation. This supersedes ADR 0258's earlier route-priority contract
  for admitted ordinary sync production shapes.
- Current route coverage estimate: 100% of accepted ordinary sync production
  programs attempt `UnifiedBytecodeVirtualMachine` before simple IR shortcuts
  and generic IR fallback. This is a selector coverage estimate, not a full
  ECMAScript function-surface claim; unsupported buckets below remain pre-VM
  declines.

- Coverage evidence: `docs/performance/unified-bytecode-primary-sync-route-coverage.md`

## Final Production Decline Status
- The accepted production scope has no unclassified decline rows: every current
  `UnifiedBytecodeProductionDeclineCode`, sync pre-gate, and prototype opcode
  guard is represented in the checked ledger below with an owning fallback route
  and proof command.
- Remaining declines are scoped to non-production-only or not-yet-admitted
  semantic lanes. They must stay as pre-VM declines until a future slice owns
  selector, compiler, VM/runtime semantics, public route proof, no-route
  neighbor proof, and no-mixed-execution source gates together.
- Benchmark and profile rows are routing evidence only when they also report
  the `unified-bytecode-production-fast-path` route-hit signal. Use
  `rtk ./tools/profile <profile> --route-hits` for route evidence over manifest
  workloads without requiring the external profiler.

## Current Completion Audit
- Completing the decline-burndown plan did not make unified bytecode the only
  execution model. The result is a wider production route plus a checked decline
  ledger. Any shape that misses the sync pre-gates, production eligibility, or
  compiler lowering still falls back to the existing IR/expression-bytecode
  execution paths.
- Non-undefined `new.target` is no longer a standalone ordinary-sync production
  pre-gate. Constructor route eligibility is now owned by the constructor/class
  activation rows and by concrete opcode/compiler support such as `LoadNewTarget`
  and `ConstructInvocationBoundary`.
- The current gap is not missing VM switch coverage. The `UnifiedBytecodeOpCode`
  inventory and `UnifiedBytecodeVirtualMachine` switch are expected to stay in
  lockstep; the current opcode surface has VM handling for every listed opcode.
  Remaining work is primarily semantic admission and lowering, not adding switch
  cases for already-declared opcodes.
- `UnifiedBytecodeCompiler` now has generated audit coverage for every declared
  IR instruction record. Function-scoped `FunctionDeclarationInstruction`
  entries compile as no-ops after fast activation hoisting installs the callable
  value, and descriptor-backed runtime declarations lower to `DeclareFunction`.
  Block-scoped function declarations behind unsupported lexical environment
  shapes can still decline through the surrounding `PushEnvironment` or call
  boundary, but they are no longer a stale compiler-decline row.
  `ClassDeclarationInstruction` is VM-owned through `DeclareClass`, with
  environment-backed lexical declaration installation and class-value creation
  handled inside the unified VM. Static synchronous
  `BindingVariableDeclarationInstruction` shapes are now VM-owned through
  `ApplyDeclarationBindingTarget`, while binding defaults, computed binding
  names, assignment targets, awaited declarations, and `using` declarations
  still decline before VM execution.
- Expression lowering is still the largest surface. `ExpressionOpKind` contains
  more shapes than the general unified lowering loop accepts. Several operations
  are admitted only through narrow shape helpers, while remaining optional-call
  chain shapes and some call/reference helpers remain outside general unified
  bytecode lowering. Private delete syntax is rejected before bytecode
  eligibility.
- The remaining direct general-expression lowering gaps are drift-checked under
  `### General Expression Lowering Gaps (current)`. Property set/update
  operations plus the `SwapTopTwo`, `DuplicateTopTwo`, and
  `RotateTopThreeRight` stack operations are no longer helper-only compiler
  shapes; they lower through the general expression loop, subject to the same
  private-name and name-inference semantic guards as the owned property-write
  helpers.
- Optional-chain short-circuit provenance is now VM-owned for the general
  expression loop. `JumpIfShortCircuited` lowers directly to unified bytecode,
  and the VM tracks a packed side flag per operand-stack slot so short-circuited
  `undefined` remains distinct from ordinary `undefined`.
- There is no retained discard-specific production decline. `EvaluateAndDiscard`
  compiles the supported expression program first and appends `Pop`, so
  discarded statements route when their underlying operation family is already
  selector/compiler/VM-owned. Remaining discarded-expression declines are
  declines of the underlying family, such as dynamic lookup, unsupported private
  neighbors, optional-chain neighbors, or other semantic lanes not yet admitted.
- The current `ExpressionOpKind` names with no unified compiler reference list is
  empty. Computed super-property operations also route their required
  `EnsureSuperReference` op through the general unified expression loop.
- Slot update lowering is now VM-owned for activation-resolved ordinary-sync
  shapes via `UpdateSlot`. The opcode is used by `IncrementSlotInstruction`
  and activation-resolved `UpdateIdentifier`, while dynamic/with-backed updates
  continue to use `UpdateDynamicIdentifier`.
- Public class methods with active or captured private-name scopes are no longer
  blanket pre-gated when their body otherwise satisfies production
  unified-bytecode routing. The invocation bridge threads those private-name
  scopes into the VM, so `#name in receiver` can execute through
  `PrivateFieldIn` and direct private named reads/writes/updates can execute
  through `GetNamedProperty` / `SetNamedProperty` / `UpdateNamedProperty`.
  Direct private named compound and logical writes execute through
  `GetNamedPropertyForCompoundSet` plus `SetNamedProperty` when the receiver
  chain is otherwise the admitted activation-resolved named-write shape.
  Direct private named method calls can execute through `PrepareNamedCallTarget`.
  Private member deletes are parser early errors, so they no longer enter
  production eligibility.
- Simple-return arrows can route with captured lexical `this` / `new.target`
  values and captured environment reads, including named and computed property
  reads from those dynamic identifier bases. Simple-return captured identifier
  assignment, compound assignment, update, and delete are VM-owned through the
  dynamic identifier environment opcodes. Simple-return named and computed
  property writes, compound/logical writes, updates, and deletes to captured
  dynamic identifier bases are also VM-owned. The production invocation bridge
  resolves those captured values before entering the VM and avoids sloppy-call
  `this` coercion for arrows.
  Arrows that need a lexical-this environment or super binding still decline.
- Simple base class constructors with public or private instance fields and
  simple derived constructors with public or private instance fields can route through
  production unified bytecode. The production constructor bridge performs the
  existing base pre-body field initialization/private branding before VM entry
  and consumes the derived pending field initializer after `super(...)`.
- Resumable async/generator unified bytecode is narrower than ordinary sync
  production bytecode. The resumable eligibility opcode allow-list is now
  audited against the `ExecuteResumable` switch, so opcodes already implemented
  by the resumable VM cannot remain stale declines. Broad async/generator
  control flow, calls, dynamic lookup, arguments, awaited iterator sources, and
  async-generator delegated yield shapes still decline.
- Async generators have a narrow production route through
  `AsyncGeneratorInvoker`: simple-parameter direct-yield `async function*`
  bodies can initialize `UnifiedBytecodeResumeState` and settle
  `Yield`/`Completed`/`Throw`/`PendingAwait` results from
  `UnifiedBytecodeVirtualMachine.ExecuteResumable` without constructing the
  `ExecutionPlanRunner`. Async-generator `yield*` and `yield* await ...`
  delegation must remain explicit pre-VM declines until delegated async iterator
  settlement is VM-owned.
- Generator-lowered synthetic resume and driver targets
  (`__yield_lower_resume*`, internal `yield*` state slots) are now added to the
  unified slot layout when the original activation analysis did not include
  them. Parenthesized `yield (left && right)`, `return yield`, and `yield*`
  state/result shapes therefore compile and route without borrowing parameter
  slots.
- Ordinary sync and arrow functions with plain leading identifier parameters
  and a final rest identifier parameter can route through production unified
  bytecode. Ordinary final-rest functions can also route with bounded implicit
  `arguments` reads and identifier operations. The invocation bridge materializes
  the rest array into the rest parameter slot and mirrors that binding into
  materialized call/dynamic environments before VM entry when such an environment
  is needed.
- Top-level script programs can route when otherwise admitted, including
  slotless `let` / `const` declarations backed by the active dynamic
  environment. `DeclareDynamicLexical` creates the TDZ lexical binding and
  `InitializeDynamicLexical` completes initialization while preserving name
  inference. Accepted top-level expression spans can also read dynamic
  identifier bases such as the global `Math` object and invoke named members
  through the normal `PrepareNamedCallTarget` plus `CallInvocationBoundary`
  contract.
- Top-level script programs that resolve a `typeof <identifier>` to a
  block-scoped lexical binding decline the production route. On the flat-slot
  script path a block-scoped `let` / `const` (such as a `for (let i ...)`
  counter) keeps its flat slot after its block exits, so a `TypeOfIdentifier`
  read against that slot would observe the binding's stale value instead of
  `"undefined"`. `UnifiedBytecodeProductionEligibility.EvaluateScript` declines
  these scripts (`TryFindBlockScopedTypeOfIdentifierLeak`) so they run via the
  scope-chain-aware IR runner, which performs a runtime environment lookup that
  respects block scope. The companion script-root-slot fast path in
  `ExecutionPlanBuilder` applies the same guard via
  `ScriptFastPathBlockBindingLeakDetector` for any out-of-scope reference to a
  block-scoped lexical binding.
- `ApplyBindingTarget` is the one explicit bridge inside accepted VM execution:
  the VM owns dispatch and stack/slot state, then applies an already-lowered
  `BindingTargetProgram` for assignment destructuring parity. This is not a
  fallback into `ExecutionPlanRunner`, but it is also not fully native unified
  bytecode.
- The contract drift test now verifies exact membership for the current
  production decline inventory and checked ledger rows. A stale extra decline
  row should fail before it can mask completed widening work.

## Current Support Matrix

### Unified Opcode Inventory (current)
- `LoadSlot`
- `LoadDynamicIdentifier`
- `LoadThis`
- `LoadNewTarget`
- `LoadImportMeta`
- `LoadTemplateObject`
- `LoadLiteral`
- `LoadRegexLiteral`
- `StoreSlot`
- `UpdateSlot`
- `InitializeSlot`
- `DeclareDynamicVar`
- `DeclareDynamicLexical`
- `InitializeDynamicLexical`
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
- `EnsureSuperReference`
- `GetNamedSuperProperty`
- `GetComputedSuperProperty`
- `SetNamedSuperProperty`
- `SetComputedSuperProperty`
- `UpdateNamedSuperProperty`
- `UpdateComputedSuperProperty`
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
- `PrivateFieldIn`
- `ToString`
- `Pop`
- `DuplicateTop`
- `DuplicateTopTwo`
- `SwapTopTwo`
- `RotateTopThreeRight`
- `CreateArray`
- `ArrayPush`
- `ArrayPushHole`
- `ArraySpread`
- `CreateObject`
- `DefineObjectProperty`
- `DefineComputedObjectProperty`
- `DefineObjectMethod`
- `DefineComputedObjectMethod`
- `DefineObjectAccessor`
- `DefineComputedObjectAccessor`
- `ObjectSpread`
- `Jump`
- `JumpWithDriverCleanup`
- `JumpIfFalse`
- `JumpIfShortCircuitFalse`
- `JumpIfShortCircuitTrue`
- `JumpIfShortCircuitNotNullish`
- `JumpIfShortCircuited`
- `JumpIfNullishReplaceUndefined`
  *(Note: `Jump` was already in the inventory; `ConditionalExpression` (`?:`) is now admitted via ADR 0297 using the existing `Jump`, `JumpIfShortCircuitFalse`, and `Pop` opcodes without new additions.)*
- `Return`
- `ReturnUndefined`
- `Throw`
- `ThrowReferenceError`
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
- `PrepareIdentifierOptionalCallTarget`
- `PrepareDynamicIdentifierCallTarget`
- `PrepareDynamicIdentifierOptionalCallTarget`
- `PrepareNamedCallTarget`
- `PrepareComputedCallTarget`
- `PrepareNamedOptionalCallTarget`
- `PrepareComputedOptionalCallTarget`
- `PrepareNamedSuperCallTarget`
- `PrepareComputedSuperCallTarget`
- `CallInvocationBoundary`
- `ConstructInvocationBoundary`
- `SuperConstructInvocationBoundary`
- `DeclareClass`
- `DeclareFunction`
- `LoadFunctionLiteral`
- `Yield`
- `StoreResumeValue`
- `AwaitAndDiscard`
- `AwaitValue`
- `AwaitedReturn`
- `YieldStar`
- `LoadClassLiteral`
- `ApplyBindingTarget`
- `ApplyDeclarationBindingTarget`
- `EnsureHasName`

### Production Decline Families (current)
- `None`
- `AsyncLikeFunction`
- `GeneratorFunction`
- `CapturedOrDynamicActivation`
- `ArgumentsObjectDependency`
- `ArrowLexicalThisDependency`
- `ClassConstructorActivation`
- `CallDependency`
- `DynamicLookupDependency`
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
- `UnsupportedPlanShape`
- `CallInvocationBoundary`

### Compiler Decline Owner Leaves (current)

`UnifiedBytecodeCompiler.TryCompile` is still wrapped as
`UnsupportedPlanShape` by the production eligibility routes when compilation
fails, but the burn-down owner is no longer one opaque umbrella. These leaves
group the exact reason templates below by the surface that must own the future
admission work. Dynamic residue remains separate in the burn-down checklist's
Dynamic Residue section; direct-eval and Function-constructor residue are not
counted as compiler-decline work here.

| Leaf | Owner surface | Current fallback route | Sync / resumable applicability |
|---|---|---|---|
| `A51a` | Entrypoint, invalid target, loop-shaped topology, unsupported breakable/loop control | Existing execution-plan runner | both |
| `A51b` | Activation-slot metadata, slot-layout, declaration / assignment / update storage targets | Existing execution-plan runner | both |
| `A51c` | Catch binding, lexical dynamic declaration, active-with dynamic-name, TDZ-head binding storage | Existing environment-aware execution-plan runner | both |
| `A51d` | Iterator, for-in, `yield*`, resume-target, and driver state slots | Existing iterator/generator/async IR drivers | both; `yield*`/resume-target are resumable-owned |
| `A51e` | Array/object destructuring state slots and targets | Existing destructuring IR helpers | both |
| `A51f` | General expression-loop unsupported op, binding-target expression, dynamic identifier, `arguments`, and private-neighbor gaps | Expression-program / execution-plan runner | both |
| `A51g` | Call-target preparation, direct-eval call boundary, member/super/private call-target, invocation-boundary shapes | Existing call/eval/super IR paths | both; dynamic residue remains D1/D4 |
| `A51h` | Array/object/template literal, spread source, computed object key, and simple literal-span shapes | Existing expression-program literal/spread evaluation | both |
| `A51i` | Computed/optional/private property-read and receiver-boundary shapes | Existing expression-program property evaluation | both |
| `A51j` | Property write, compound/logical write, update, delete, name-inference, key-span, and RHS-span shapes | Existing expression-program mutation evaluation | both |
| `A51k` | Simple binary/unary/control/conditional operand spans | Existing expression-program evaluation | both |
| `A51l` | Catch/try/driver cleanup topology diagnostics not otherwise captured by concrete driver rows | Existing execution-plan runner | both |
| `A51m` | Measured property-read span rollback diagnostics | Existing property-read expression evaluation | both |
| `B47a` | Resumable-only `yield*` state-slot and synthetic resume-target layout | Existing generator/async execution-plan route | resumable only |

### Compiler Decline Reason Templates (current)

This exact template list is drift-checked against non-empty `reason = ...`
assignments in `UnifiedBytecodeCompiler.cs`. Updating, adding, or removing one
of these templates requires updating this contract and the checklist owner leaf
above in the same slice.

- `Activation slot metadata is required.`
- `Array literal RHS span does not match expected boundary.`
- `Binding-target expressions are not available in this unified bytecode compilation context.`
- `Call target preparation is only supported for activation-resolved identifier and direct member calls.`
- `Complex computed keys and name-inferred computed properties are not admitted in simple object literals.`
- `Complex value expressions are not admitted in simple computed object properties.`
- `Computed compound property writes with name inference are not supported.`
- `Computed logical property binary RHS uses an unsupported operator.`
- `Computed logical property writes only admit simple or simple-binary RHS spans.`
- `Computed logical property writes require an RHS expression.`
- `Computed logical property writes with name inference are not supported.`
- `Computed member call targets require receiver and key operands.`
- `Computed object key call target requires an activation-resolved identifier slot.`
- `Computed object keys only admit activation-resolved zero-argument identifier calls.`
- `Computed property reads require RequireObjectCoercible(Depth: 1) in the first production boundary.`
- `Computed property reads require ResolvePropertyKey in the first production boundary.`
- `Computed property writes with name inference are not supported in the general expression loop.`
- `Computed property writes with name inference are not supported.`
- `Computed super call targets require exactly one computed key operand.`
- `Declaration target '{declarationTargetSymbol.Name}' is not eligible for unified bytecode storage.`
- `Direct eval call-target preparation requires an eval identifier target.`
- `Dynamic identifier assignment references are not eligible outside an active with environment.`
- `Expected ArrayPush or ArraySpread after array element operand.`
- `Expected ArrayPush or ArraySpread after element.`
- `Expected CreateArray at index {startIndex}.`
- `Expected CreateObject at index {startIndex}.`
- `Expected DefineComputedObjectProperty after key, ResolvePropertyKey, and value.`
- `Expected DefineObjectProperty or value operand after first operand.`
- `Expected value operand after ResolvePropertyKey.`
- `Failed to emit measured named property read span.`
- `Failed to emit measured optional named property read span.`
- `Failed to emit measured optional named read chain span.`
- `Identifier '{identifier.Name.Name}' requires dynamic lookup and is not eligible outside an active with environment.`
- `Identifier '{storeIdentifier.Name.Name}' requires dynamic lookup and is not eligible outside an active with environment.`
- `Identifier '{storeIdentifier.Name.Name}' resolves to an activation slot and is not eligible for dynamic unified bytecode assignment references.`
- `Identifier assignment reference '{referenceIdentifier.Name.Name}' requires dynamic lookup and is not eligible outside an active with environment.`
- `Identifier assignment reference '{referenceIdentifier.Name.Name}' resolves to an activation slot and is not eligible for dynamic unified bytecode assignment references.`
- `Identifier assignment reference '{storeReferenceIdentifier.Name.Name}' requires dynamic lookup and is not eligible outside an active with environment.`
- `Identifier assignment reference '{storeReferenceIdentifier.Name.Name}' resolves to an activation slot and is not eligible for dynamic unified bytecode assignment references.`
- `Identifier call target '{identifier.Name.Name}' requires dynamic lookup and is not eligible outside an active with environment.`
- `Identifier call target '{identifierCallTarget.Name.Name}' requires dynamic lookup and is not eligible outside an active with environment.`
- `Instruction flow reached an invalid target index.`
- `Iterator/for-in driver TDZ head binding '{binding.Name}' could not be resolved to a flat activation slot.`
- `Lexical declaration target '{targetSymbol.Name}' requires dynamic identifier operations.`
- `Literal spans only admit direct computed member calls with simple receiver, key, and arguments.`
- `Literal spans only admit optional computed member calls with simple receiver, key, and arguments.`
- `Literal spans only admit optional named member calls with simple receiver and arguments.`
- `Logical argument count does not match call.ArgumentCount in the call-target preparation boundary.`
- `Logical computed property writes require jump target at cleanup start.`
- `Logical named property writes require jump target at cleanup start.`
- `Loop-shaped unified bytecode plan detected at instruction {instructionIndex}.`
- `Loop-shaped unified bytecode plan detected at instruction {targetIndex}.`
- `Member call receiver is outside the direct named-chain boundary.`
- `Mismatched named compound property read/write operands are not supported.`
- `Mismatched named logical property read/write operands are not supported.`
- `Named compound property writes with name inference are not supported.`
- `Named logical property writes with name inference are not supported.`
- `Named member call targets require a receiver expression.`
- `Named super call targets must be the first operation in the invocation boundary.`
- `Nested named computed property writes with name inference are not supported.`
- `Nested named property writes with name inference are not supported.`
- `Object literal RHS span does not match expected boundary.`
- `Object methods, object accessors, private names, and name inference are not admitted in simple object literals.`
- `Only direct identifier and member calls with explicit receiver records are supported.`
- `Only one-argument non-spread direct eval is supported by the call-target preparation boundary.`
- `Only optional and simple identifier catch bindings are eligible for unified bytecode compilation.`
- `Only ordinary explicit-this calls are supported in the general expression loop.`
- `Only supported simple binary operators are admitted in this boundary.`
- `Optional computed compound property writes are not supported.`
- `Optional computed logical property writes are not supported.`
- `Optional computed property reads are not supported.`
- `Optional identifier call targets require an activation-resolved identifier slot or admitted dynamic identifier operations.`
- `Optional named compound property writes are not supported.`
- `Optional named logical property writes are not supported.`
- `Optional named property reads with short-circuit target are not supported in the general expression loop.`
- `Optional receiver properties are outside the call-target preparation boundary.`
- `Private named compound property receiver reads are not supported.`
- `Private named logical property receiver reads are not supported.`
- `Private named member call targets are outside the call-target preparation boundary.`
- `Private named property deletes are not supported in the general expression loop.`
- `Private named property reads are not supported.`
- `Private named receiver properties are outside the call-target preparation boundary.`
- `Private named super call targets are outside the call-target preparation boundary.`
- `Private named super call targets are outside the general expression loop boundary.`
- `Private names and name inference are not admitted in simple object literals.`
- `Private nested named property receiver reads are not supported.`
- `Private nested named property updates are not supported.`
- `Private nested named property writes are not supported.`
- `Property writes with name inference are not supported in the general expression loop.`
- `Property writes with name inference are not supported.`
- `Resume target '{resumeSymbol.Name}' is not in the activation slot layout.`
- `Simple binary spans require simple activation-resolved or admitted dynamic operands.`
- `Simple unary spans require a simple activation-resolved or admitted dynamic operand.`
- `Spread sources only admit direct named member calls with simple arguments.`
- `Template literal RHS span does not match expected boundary.`
- `Template literal RHS span does not match expected nested property-write boundary.`
- `Unsupported array destructuring state slot '{arrayDestructuringClose.IteratorSlot.Name}'.`
- `Unsupported array destructuring state slot '{arrayDestructuringElement.IteratorSlot.Name}'.`
- `Unsupported array destructuring state slot '{arrayDestructuringInit.IteratorSlot.Name}'.`
- `Unsupported array destructuring state slot '{arrayDestructuringRest.IteratorSlot.Name}'.`
- `Unsupported assignment target '{assignmentTargetSymbol.Name}'.`
- `Unsupported breakable construct: only loop-style breakable wrappers are eligible for unified bytecode compilation.`
- `Unsupported catch binding slot '{identifier.Name.Name}'.`
- `Unsupported compound assignment target '{compoundTargetSymbol.Name}'.`
- `Unsupported compound computed property binary operator '{binary.Operator}'.`
- `Unsupported compound property binary operator '{binary.Operator}'.`
- `Unsupported computed property key op '{operation.Kind}'.`
- `Unsupported computed property key span.`
- `Unsupported conditional alternate in simple literal span.`
- `Unsupported conditional consequent in simple literal span.`
- `Unsupported declaration target '{declaration.TargetSymbol?.Name}'.`
- `Unsupported declaration target '{targetSymbol.Name}'.`
- `Unsupported destructuring target '{targetSymbol.Name}'.`
- `Unsupported direct eval argument op '{operation.Kind}'.`
- `Unsupported driver state slot '{stateSymbol.Name}'.`
- `Unsupported driver value slot '{valueSymbol.Name}'.`
- `Unsupported entrypoint.`
- `Unsupported expression op '{operation.Kind}'.`
- `Unsupported for-in state slot '{awaitedForInInit.StateSlot.Name}'.`
- `Unsupported for-in state slot '{forInInit.StateSlot.Name}'.`
- `Unsupported identifier '{identifier.Name.Name}' at dynamic property-read boundary.`
- `Unsupported instruction in unified bytecode plan: {instructions[instructionIndex].GetType().Name}.`
- `Unsupported instruction in unified bytecode plan: {nameof(IncrementSlotInstruction)}.`
- `Unsupported iterator close state slot '{iteratorClose.IteratorSlot.Name}'.`
- `Unsupported iterator state slot '{awaitedIteratorInit.IteratorSlot.Name}'.`
- `Unsupported iterator state slot '{iteratorInit.IteratorSlot.Name}'.`
- `Unsupported logical assignment target '{logicalTargetSymbol.Name}'.`
- `Unsupported logical control-expression operand in simple literal span.`
- `Unsupported loop control flow at instruction {instructionIndex}.`
- `Unsupported loop control flow at instruction {sourceInstructionIndex}.`
- `Unsupported member call receiver op '{propertyRead.Kind}'.`
- `Unsupported object destructuring state slot '{objectDestructuringClose.SourceSlot.Name}'.`
- `Unsupported object destructuring state slot '{objectDestructuringInit.SourceSlot.Name}'.`
- `Unsupported object destructuring state slot '{objectDestructuringProperty.SourceSlot.Name}'.`
- `Unsupported object destructuring state slot '{objectDestructuringRest.SourceSlot.Name}'.`
- `Unsupported property-read base op '{operation.Kind}'.`
- `Unsupported simple operand op '{operation.Kind}'.`
- `Unsupported typeof identifier '{identifier.Name.Name}'.`
- `Update target '{updateIdentifier.Name.Name}' requires dynamic lookup and is not eligible outside an active with environment.`
- `arguments assignment references are not supported.`
- `arguments call targets are outside the optional identifier call-target preparation boundary.`
- `arguments typeof is not supported.`
- `delete identifier '{deleteIdentifier.Name.Name}' requires dynamic lookup and is not eligible outside an active with environment.`
- `typeof identifier '{identifier.Name.Name}' requires dynamic lookup and is not eligible outside an active with environment.`
- `typeof identifier '{typeOfIdentifier.Name.Name}' requires dynamic lookup and is not eligible outside an active with environment.`
- `yield* requires a state slot for resumable unified bytecode routing.`
- `yield* state slot '{yieldStar.StateSlotSymbol.Name}' is not in the activation slot layout.`

## Checked Production Decline Ledger

This ledger is a drift-guarded baseline for widening work. It distinguishes
ordinary sync pre-gates from `UnifiedBytecodeProductionEligibility` declines:
pre-gates fall through to the existing sync route before eligibility runs,
eligibility declines keep the plan out of the production VM, and accepted rows
must still obey the no-mixed-execution rule.

The decline-family list above is a coarse taxonomy, not a sub-gate burn-down
counter. A family such as `CallDependency` or `PropertyWriteDependency` remains
listed until every concrete shape in that family is VM-owned; individual
widening slices reduce the text in the ledger row and the associated proof
surface before the family itself can disappear.

### Concrete Remaining Gate View

This is the current human-maintained burn-down view beneath the coarse decline
families. It is intentionally phrased as testable shapes rather than enum names;
the next audit step is to generate this list from the selector/compiler
predicates and proof tests.

- Async-like ordinary functions, generator functions, awaited with-object plans,
  and resumable opcode shapes outside the `EvaluateResumable` subset. That subset
  now covers value-tier reads, synchronous call dispatch, optional
  chains/optional calls (`o?.a`, `o?.[k]`, `o?.m()`, `f?.()`), free/dynamic
  identifier reads and calls, PROPERTY WRITES (`o.x = v`, `o[k] = v`,
  `this.x = v` via `SetNamedProperty`/`SetComputedProperty`), SLOT UPDATES
  (`x++`/`x--`/`++x`/`--x` via `UpdateSlot`, restricted to parameter and `var`
  targets by the `IsLexicalSlotUpdateTarget` const-safety guard), and PROPERTY
  UPDATES/DELETES (`o.x++`, `o[k]--`, `delete o.x`, `delete o[k]` via
  `UpdateNamedProperty`/`UpdateComputedProperty`/`DeleteNamedProperty`/
  `DeleteComputedProperty`) between suspension points; computed optional calls
  (`o?.[k]()`), lexical-slot updates (`let i; i++`, where the resume state has no
  const-slot metadata to enforce a `const` reassignment `TypeError`),
  dynamic-identifier `typeof`/`delete`, free dynamic writes/updates/deletes
  (`freeGlobal++`, `delete freeGlobal`), super-property updates/deletes, and
  `super`/construct boundaries inside resumable bodies remain outside it.
- Captured function scopes outside the simple-return captured-closure route,
  unresolved non-with dynamic activation, arrow lexical `this` / `new.target`,
  and class-constructor activation outside the bounded constructor routes.
- Unbounded real `arguments` object dependency.
- Direct eval outside the one-argument non-spread eval-identifier boundary.
- Out-of-boundary call-target preparation, complex receiver/key/call-target
  shapes, complex call arguments outside admitted simple/binary/unary/`typeof`/
  property-read/control-expression/literal spans, and remaining
  receiver-binding-sensitive adjacent call families.
- Unresolved identifier loads/stores/updates outside ordinary, with-backed,
  direct-eval-backed, simple-return captured-closure dynamic-name paths, and
  accepted top-level dynamic-global read paths.
- Named/computed property reads outside activation-resolved and read-only
  dynamic-identifier-base boundaries, especially captured-closure dynamic bases
  and richer computed-key spans.
- Property writes and compound/logical writes outside direct property-write
  shapes, supported computed expression-key mutation shapes (including whole-span
  control-expression keys such as `obj[cond ? a : b]`, `obj[a && b]`,
  `obj[a ?? b]`, except branches needing slot-layout/call-target threading),
  simple-return dynamic-base writes, and current nested named receiver shapes.
- Property/identifier updates outside direct update, computed expression-key
  update, simple-return dynamic-base update, and the current simple nested named
  receiver update boundary.
- Delete expressions outside ordinary named/computed property delete,
  simple-return dynamic-base delete, ordinary dynamic-key computed delete, and
  with-backed dynamic-name delete.
- Out-of-boundary super call targets and remaining class/constructor
  super-adjacent call shapes.
- Optional-chain shapes outside admitted optional property-read, optional-call,
  and exact optional named/computed delete boundaries.
- Object/array spread sources outside simple operands, admitted member-call
  spans, and simple-control-expression spans; richer computed object keys/values
  outside admitted simple, member-call, and simple-control-expression spans;
  object methods/accessors outside restricted simple literal spans.
- Private-name operations outside admitted `#name in obj`, direct private
  reads/writes/updates, direct private compound/logical writes, and direct
  private named method calls.
- Awaited for-in sources, async iterator drivers, and unsupported for-in driver
  state.
- Awaited destructuring binding values, unsupported destructuring driver shapes,
  and destructuring targets outside direct-slot or descriptor-backed lanes.
- Missing slot metadata, unsupported instruction families, unsupported compiler
  shapes, unsupported resumable opcodes, and unknown production opcode defaults.
- Pre-gates: class constructor invokers outside bounded constructor routes;
  arrow functions whose lowered body still has an unowned dependency;
  runtime-dependent default and destructured parameter expressions; identifier-cache
  disabled outside ordinary/with-backed lanes; super constructor/prototype state
  outside admitted paths.

### Eligibility Decline Rows

| Decline code | Owning source / current example | Current fallback route | Planned batch / lane | Proof command |
|---|---|---|---|---|
| `None` | `UnifiedBytecodeProductionEligibility.Accept` for accepted programs, for example `function passThrough(x) { var y = x; return y; }` | Production unified-bytecode VM | Baseline accepted route | `rtk dotnet test tests/Asynkron.JsEngine.Tests --filter "FullyQualifiedName~UnifiedBytecodeProductionEligibilityTests&FullyQualifiedName~Evaluate_LinearSlotLiteralReturnPlan_Accepts"` |
| `AsyncLikeFunction` | `Evaluate` activation gate and awaited with-object plan decline | Existing async / awaited IR route | Resumable async/generator lane | `rtk dotnet test tests/Asynkron.JsEngine.Tests --filter "FullyQualifiedName~UnifiedBytecodeProductionEligibilityTests&FullyQualifiedName~Evaluate_AsyncLikeActivation_DeclinesBeforePlanInspection"` |
| `GeneratorFunction` | `Evaluate` activation gate for ordinary sync production routing | Existing generator IR route | Resumable async/generator lane | `rtk dotnet test tests/Asynkron.JsEngine.Tests --filter "FullyQualifiedName~UnifiedBytecodeProductionEligibilityTests&FullyQualifiedName~Evaluate_GeneratorActivation_DeclinesBeforePlanInspection"` |
| `CapturedOrDynamicActivation` | Activation descriptor `HasCapturedOrDynamicActivation`, including captured function scope outside the admitted simple-return captured-closure route or unresolved dynamic activation outside the with-backed lane | Existing sync IR / environment route | Dynamic activation lane | `rtk dotnet test tests/Asynkron.JsEngine.Tests --filter "FullyQualifiedName~UnifiedBytecodeProductionEligibilityTests&FullyQualifiedName~Evaluate_ActivationDependencies_DeclineBeforeCompile"` |
| `ArgumentsObjectDependency` | Activation descriptor gate for unbounded real `arguments` object dependency; implicit `arguments` reads/assignments/updates/deletes/calls materialize the existing arguments object and use the bounded identifier route, including admitted simple literal-default and final-rest parameter routes, while parameter/lexical `arguments` reads, `typeof`, assignments, updates, deletes, and call targets route as activation slots | Existing sync IR / arguments-object route | Arguments object lane | `rtk dotnet test tests/Asynkron.JsEngine.Tests --filter "FullyQualifiedName~UnifiedBytecodeProductionEligibilityTests&FullyQualifiedName~Evaluate_CallImplicitArgumentsObject_AcceptsDynamicIdentifierCallTarget"` |
| `ArrowLexicalThisDependency` | Activation descriptor gate for arrow lexical `this` / `new.target` ownership before ordinary sync routing | Existing arrow invocation route | Arrow route lane | `rtk dotnet test tests/Asynkron.JsEngine.Tests --filter "FullyQualifiedName~UnifiedBytecodeProductionEligibilityTests&FullyQualifiedName~Evaluate_OrdinarySyncActivationDescriptorBlockers_DeclineBeforeCompile"` |
| `ClassConstructorActivation` | Activation descriptor gate for class constructor activation outside the admitted base constructor routes with simple identifier parameters, simple literal-default parameters, or final-rest identifier parameters; explicit derived-constructor `super(...)` routes with simple identifier parameters, simple literal-default parameters, final-rest identifier parameters, and post-super `this` body reads/writes; and default-derived constructor `super(...args)` forwarding route | Constructor route | Constructor boundary lane | `rtk dotnet test tests/Asynkron.JsEngine.Tests --filter "FullyQualifiedName~UnifiedBytecodeProductionConstructCallTests&FullyQualifiedName~Constructor"` |
| `CallDependency` | Direct eval outside the one-argument non-spread eval-identifier boundary, out-of-boundary call-target preparation, and complex call arguments excluding admitted simple/binary template-literal substitutions, simple/binary computed object keys, zero-argument activation-resolved identifier-call computed object keys, simple activation or dynamic identifier-call arguments, simple-binary arguments, nested simple, simple-binary, simple-unary, simple-property-read, simple unary (`fn(-x)`) and typeof-identifier (`fn(typeof x)`) operands, baseline single-hop optional named property-read (`fn(box?.value)`), optional-start-then-plain named read chain (`fn(box?.child.value)`), baseline optional computed property-read (`fn(box?.[key])`), optional-computed-then-named read chain (`fn(box?.[key].value)`), optional-named-then-plain-computed read chain (`fn(box?.prop[key])`, `fn(box?.a.b[key])`), simple-control-expression, and simple-`typeof` array/object/template literal values/elements, dynamic operands inside simple array/object/template call arguments for admitted identifier-call and member-call targets, and embedded named member calls on admitted dynamic identifier bases such as `Math.sqrt(...)` in supported expression spans | Existing sync IR call route | Wider call invocation lane | `rtk dotnet test tests/Asynkron.JsEngine.Tests --filter "FullyQualifiedName~UnifiedBytecodeProductionEligibilityTests"` |
| `DynamicLookupDependency` | Unresolved identifier loads/stores/update outside the admitted ordinary, with-backed, direct-eval-backed, simple-return captured-closure dynamic-name paths, and top-level dynamic-global read paths used by accepted script expression spans; plans that need direct eval plus post-eval dynamic identifier reads now route when captured activation, with-chain, and arguments-object dependencies are absent | Existing sync IR / environment lookup route | Dynamic-name lane | `rtk dotnet test tests/Asynkron.JsEngine.Tests --filter "FullyQualifiedName~UnifiedBytecodeProductionEligibilityTests&FullyQualifiedName~Evaluate_DirectEvalDeclaredVarRead_AcceptsOrdinaryDynamicNameProgram"` |
| `PropertyReadBoundaryOutOfScope` | Named/computed property reads outside the admitted activation-resolved and read-only dynamic-identifier-base boundaries, including captured closure dynamic bases | Existing sync IR property route | Property read widening lane | `rtk dotnet test tests/Asynkron.JsEngine.Tests --filter "FullyQualifiedName~UnifiedBytecodeProductionEligibilityTests&FullyQualifiedName~Evaluate_ComputedPropertyReadOutsideFirstBoundary_DeclinesWithBoundaryCode"` |
| `PropertyWriteDependency` | Property writes and compound/logical property writes outside the admitted direct property-write shapes, supported computed expression-key mutation shapes (including rich binary/unary computed keys such as `box.child[key + 1] = value`, and whole-span control-expression computed keys such as the ternary `box.child[cond ? a : b] = value`, logical-and `obj[a && b] = value`, and nullish-coalesce `obj[a ?? b] = value` — admitted via the shared `IsSupportedComputedPropertyKeySpan`/`TryAppendComputedPropertyKeySpan` control-expression delegation; control-expression keys whose branches need slot-layout/call-target threading, e.g. a member-call branch, remain declined), simple-return dynamic-base named/computed property writes and compound/logical writes, simple nested named receiver assignment shape, nested named receiver plus computed-write shape (`box.child[key] = value`), computed-read prefix plus named-write shape (`box[key].child = value`), nested named compound-write shape, nested named logical-write shape, nested named receiver plus computed compound-write shape (`box.child[key] += value`), and nested named receiver plus computed logical-write shape (`box.child[key] &&= value`, `||=`, `??=`) | Existing sync IR property-write route | Property write widening lane | `rtk dotnet test tests/Asynkron.JsEngine.Tests --filter "FullyQualifiedName~UnifiedBytecodeProductionEligibilityTests&FullyQualifiedName~Evaluate_LogicalAndAssignment_UnsupportedShapes_DeclineWithExplicitCodes"` |
| `PropertyUpdateDependency` | Property and identifier update expressions outside the admitted direct update, computed expression-key update, simple-return dynamic-base named/computed property update, simple nested named receiver update boundary, and nested named receiver plus computed-update shape (`box.child[key]++`, `++box.child[key]`, `box.child[key]--`, `--box.child[key]`, including deep prefixes such as `box.child.branch[key]++`, rich binary keys such as `box.child[key + 1]++`, and whole-span control-expression keys such as `box.child[cond ? a : b]++`; a computed receiver prefix such as `box[k1].child[k2]++` stays declined) | Existing sync IR update route | Property update lane | `rtk dotnet test tests/Asynkron.JsEngine.Tests --filter "FullyQualifiedName~UnifiedBytecodeProductionEligibilityTests&FullyQualifiedName~Evaluate_NestedNamedComputedPropertyUpdate_AcceptsOwnedPropertyOpcodes"` |
| `DeleteDependency` | `delete` expressions outside the admitted ordinary named/computed property delete lane, simple-return dynamic-base named/computed property delete lane, ordinary dynamic-key computed delete lane, bounded implicit-`arguments` identifier delete lane, and with-backed dynamic-name delete lane | Existing sync IR delete route | Delete semantics lane | `rtk dotnet test tests/Asynkron.JsEngine.Tests --filter "FullyQualifiedName~UnifiedBytecodeProductionEligibilityTests&FullyQualifiedName~Evaluate_NestedComputedPropertyDeleteDynamicKey_AcceptsOrdinaryDynamicNameOpcode"` |
| `SuperPropertyDependency` | Out-of-boundary super call targets; super property reads/writes/updates are admitted by dedicated VM opcodes | Existing class / constructor route for remaining call-target shapes | Super semantics lane | `rtk dotnet test tests/Asynkron.JsEngine.Tests --filter "FullyQualifiedName~UnifiedBytecodeProductionEligibilityTests&FullyQualifiedName~Evaluate_SuperPropertyAccess_AcceptsOwnedOpcodes"` |
| `OptionalChainDependency` | Optional chains outside the admitted optional property-read, optional-call, and exact optional named/computed delete boundaries | Existing sync IR optional-chain route | Optional-chain widening lane | `rtk dotnet test tests/Asynkron.JsEngine.Tests --filter "FullyQualifiedName~UnifiedBytecodeProductionEligibilityTests&FullyQualifiedName~Evaluate_OptionalChainNamedPrefixPlainCallExpressionPlan_AcceptsExecutableInvocationBoundary"` |
| `ObjectLiteralOrSpreadDependency` | Object/array spread sources outside simple operands, direct named/computed member-call spans, receiver/callee-optional named/computed member-call spans, and simple-control-expression spans in spread sources, computed object keys, or object property values over activation-slot or admitted dynamic receiver/key/argument operands, and object methods/accessors only when they appear inside restricted simple literal spans | Existing sync IR literal/spread route for remaining spans | Literal/spread lane | `rtk dotnet test tests/Asynkron.JsEngine.Tests --filter "FullyQualifiedName~UnifiedBytecodeProductionEligibilityTests&FullyQualifiedName~Evaluate_ObjectLiteralUnsupportedFeatures_DeclineWithExplicitCodes"` |
| `PrivateFieldDependency` | Private-name operations outside the admitted routes; `#name in obj`, direct private named reads/writes/updates, direct private named compound/logical writes, and direct private named method calls are VM-owned when the surrounding class method is otherwise production-eligible. Private member deletes are parser early errors before production eligibility. | Existing private-name route for remaining private member access | Private-name lane | `rtk dotnet test tests/Asynkron.JsEngine.Tests --filter "FullyQualifiedName~UnifiedBytecodeProductionEligibilityTests&FullyQualifiedName~Evaluate_PrivateFieldIn_AcceptsAndVmChecksPrivateBrand"` |
| `ForInDriverStateDependency` | Unsupported for-in driver state outside the lowered synchronous source and resumable awaited-source lanes | Existing for-in IR driver route | Driver-state lane | `rtk dotnet test tests/Asynkron.JsEngine.Tests --filter "FullyQualifiedName~UnifiedBytecodeProductionEligibilityTests&FullyQualifiedName~EvaluateResumable_AwaitedForInSource_AdmitsAwaitValueAndForInDriver"` |
| `DestructuringDependency` | Awaited binding values, unsupported destructuring driver shapes, and targets outside the admitted driver or descriptor-backed lanes; declaration defaults and computed binding names route through `ApplyDeclarationBindingTarget` | Existing destructuring IR route for remaining shapes | Destructuring driver lane | `rtk dotnet test tests/Asynkron.JsEngine.Tests --filter "FullyQualifiedName~UnifiedBytecodeProductionEligibilityTests&FullyQualifiedName~Evaluate_DeclarationDestructuringDescriptorShapes_AcceptDescriptorOpcode"` |
| `UnsupportedPlanShape` | Missing activation slot metadata, unsupported instruction families, unsupported compiler shapes, unsupported resumable opcodes, and unknown production opcode defaults | Existing execution-plan route | Statement/control-flow ownership lane | `rtk dotnet test tests/Asynkron.JsEngine.Tests --filter "FullyQualifiedName~ExpressionProgramCoverageMapTests&FullyQualifiedName~UnifiedBytecodeExpansionContract_ListsRequiredHeadingsAndCurrentEnums"` |
| `CallInvocationBoundary` | Plan-structural call invocation outside the currently executable call boundary, separate from descriptor-level `CallDependency` | Existing sync IR call route | Wider call invocation lane | `rtk dotnet test tests/Asynkron.JsEngine.Tests --filter "FullyQualifiedName~ExpressionProgramCoverageMapTests&FullyQualifiedName~UnifiedBytecodeExpansionContract_ListsRequiredHeadingsAndCurrentEnums"` |

### Sync Production Pre-Gate Rows

These rows are owned by
`TypedAstEvaluator.SyncFunctionInvoker.CanUseProductionUnifiedBytecodeFastPath`.
They decline before `UnifiedBytecodeProductionEligibility.Evaluate`, so they do
not always have a `UnifiedBytecodeProductionDeclineCode`.

| Pre-gate key | Owning source / current example | Current fallback route | Planned batch / lane | Proof command |
|---|---|---|---|---|
| `pre-gate:IsClassConstructor` | Class constructor invokers outside the admitted base constructor routes with simple identifier parameters, simple literal-default parameters, or final-rest identifier parameters, and outside the explicit derived-constructor `super(...)` routes with the same admitted parameter variants and post-super `this` body reads/writes | Constructor route | Constructor boundary lane | `rtk dotnet test tests/Asynkron.JsEngine.Tests --filter "FullyQualifiedName~UnifiedBytecodeProductionInvocationTests"` |
| `pre-gate:IsArrowFunction` | Arrow functions outside the admitted simple-return route whose lowered body proves no super, unadmitted call-target, nested function/class literal, or other unowned dependency. Simple literal-default identifier parameters, including defaults folded to literals before invocation, and final-rest identifier parameters are admitted on the same VM slot-initialization path as ordinary functions. Captured lexical `this` / `new.target`, statically resolved flat-slot reads, ordinary environment identifier reads/calls, captured identifier assignment/compound assignment/update/delete, and named/computed property reads, simple property writes, compound/logical property writes, property updates, or property deletes from those dynamic identifier bases are VM-owned; lexical-this environments remain a separate pre-gate. | Existing arrow invocation route | Arrow route lane | `rtk dotnet test tests/Asynkron.JsEngine.Tests --filter "FullyQualifiedName~UnifiedBytecodeProductionInvocationTests"` |
| `pre-gate:IsAsyncLike` | Async and formerly-async ordinary invokers | Async route | Resumable async/generator lane | `rtk dotnet test tests/Asynkron.JsEngine.Tests --filter "FullyQualifiedName~UnifiedBytecodeProductionEligibilityTests&FullyQualifiedName~Evaluate_AsyncLikeActivation_DeclinesBeforePlanInspection"` |
| `pre-gate:IsGenerator` | Generator functions before ordinary sync production routing | Generator route | Resumable async/generator lane | `rtk dotnet test tests/Asynkron.JsEngine.Tests --filter "FullyQualifiedName~UnifiedBytecodeProductionEligibilityTests&FullyQualifiedName~Evaluate_GeneratorActivation_DeclinesBeforePlanInspection"` |
| `pre-gate:hasParameterExpressions` | Runtime-dependent default parameter expressions outside the admitted simple literal-default identifier route, where literal includes defaults folded to literals before invocation, including its bounded implicit-arguments read/property-read/update/assignment/delete/call-target variant and admitted base/derived-constructor variants, plus destructured parameter expressions; final rest identifier parameters are admitted when the surrounding activation is otherwise production-owned | Existing parameter environment route | Parameter semantics lane | `rtk dotnet test tests/Asynkron.JsEngine.Tests --filter "FullyQualifiedName~UnifiedBytecodeProductionInvocationTests"` |
| `pre-gate:hasOnlySimpleIdentifierParameters` | Non-simple parameter lists outside the admitted final-rest identifier route, including its bounded implicit-arguments read/property-read/update/assignment/delete/call-target variant for ordinary functions and admitted base/derived-constructor variants, and simple literal-default identifier routes | Existing parameter environment route | Parameter semantics lane | `rtk dotnet test tests/Asynkron.JsEngine.Tests --filter "FullyQualifiedName~UnifiedBytecodeProductionInvocationTests"` |
| `pre-gate:usesArguments` | Functions that read, assign, update, delete, call, or `typeof` the real implicit `arguments` object through bounded identifier use; simple literal-default parameters and ordinary final-rest parameters are admitted when the existing implicit-arguments predicate owns the body, including simple named/computed property reads such as `arguments.length` and `arguments[0]`, bounded identifier update, assignment, delete, and call-target shapes; shadowing parameter/lexical `arguments` reads, `typeof`, assignments, updates, deletes, and calls are activation-slot traffic | Existing arguments-object route | Arguments object lane | `rtk dotnet test tests/Asynkron.JsEngine.Tests --filter "FullyQualifiedName~UnifiedBytecodeProductionEligibilityTests&FullyQualifiedName~Evaluate_CallImplicitArgumentsObject_AcceptsDynamicIdentifierCallTarget"` |
| `pre-gate:needsArgumentsBinding` | Sloppy mapped arguments binding when an implicit arguments object is needed and not admitted by with-backed dynamic-name routing | Existing arguments-object route | Arguments object lane | `rtk dotnet test tests/Asynkron.JsEngine.Tests --filter "FullyQualifiedName~UnifiedBytecodeProductionInvocationTests"` |
| `pre-gate:allowIdentifierCache` | Identifier cache disabled outside the ordinary and with-backed dynamic-name lanes, including the admitted mixed `with` plus outside ordinary dynamic-name path | Existing environment lookup route | Dynamic-name lane | `rtk dotnet test tests/Asynkron.JsEngine.Tests --filter "FullyQualifiedName~UnifiedBytecodeProductionEligibilityTests&FullyQualifiedName~Evaluate_WithThenOutsideDynamicIdentifier_AcceptsMixedDynamicNameProgram"` |
| `pre-gate:lexicalThisEnvironment` | Lexical `this` environment captured by arrow / class-derived shapes | Existing lexical-this route | This-binding lane | `rtk dotnet test tests/Asynkron.JsEngine.Tests --filter "FullyQualifiedName~UnifiedBytecodeProductionInvocationTests"` |
| `pre-gate:superConstructor` | Derived-constructor super binding dependency outside the bounded production constructor admission path | Constructor / super route | Super / constructor lane | `rtk dotnet test tests/Asynkron.JsEngine.Tests --filter "FullyQualifiedName~UnifiedBytecodeProductionEligibilityTests&FullyQualifiedName~Evaluate_SuperCall_AcceptsSuperConstructInvocationBoundary"` |
| `pre-gate:superPrototype` | Method home-object super prototype state; admitted for VM-owned super property reads/writes/updates and first-boundary super calls | Existing super route for remaining shapes | Super semantics lane | `rtk dotnet test tests/Asynkron.JsEngine.Tests --filter "FullyQualifiedName~UnifiedBytecodeProductionEligibilityTests&FullyQualifiedName~Evaluate_SuperPropertyAccess_AcceptsOwnedOpcodes"` |
| `pre-gate:activationSlotShape` | `CanUseProductionUnifiedBytecodePlanShape` requires activation slots matching root scope, layout, and parameter-slot length unless with-backed dynamic names or environment-backed class declarations own the materialized activation | Existing execution-plan route | Slot-layout lane | `rtk dotnet test tests/Asynkron.JsEngine.Tests --filter "FullyQualifiedName~ExpressionProgramCoverageMapTests&FullyQualifiedName~UnifiedBytecodeExpansionContract_ListsRequiredHeadingsAndCurrentEnums"` |

### Prototype Opcode Guard Rows

These rows are owned by
`UnifiedBytecodeProductionEligibility.TryFindPrototypeOnlyOpcode`. The guard is
the final post-compile production subset check before VM entry.

| Prototype guard key | Owning source / current example | Current fallback route | Planned batch / lane | Proof command |
|---|---|---|---|---|
| `prototype-guard:Binary` | `Binary` opcodes currently admit the selector-owned production subset: arithmetic (`+`, `-`, `*`, `/`, `%`, `**`), equality/comparison (`==`, `!=`, `===`, `!==`, `<`, `<=`, `>`, `>=`), bitwise/shift (`&`, `|`, `^`, `<<`, `>>`, `>>>`), and relational/object tests (`in`, `instanceof`); binary operators outside that subset (for example `&&`, `\|\|`, `??`) decline as `UnsupportedPlanShape` | Existing expression route | Binary operator widening lane | `rtk dotnet test tests/Asynkron.JsEngine.Tests --filter "FullyQualifiedName~UnifiedBytecodeProductionEligibilityTests&FullyQualifiedName~Evaluate_PropertyReadAdjacentFamilies_DeclineWithExplicitCodes"` |
| `prototype-guard:Jump` | `Jump` and `JumpWithDriverCleanup` are admitted only for compiler-owned branch, loop, and driver cleanup shapes | Existing control-flow route if an unowned jump shape is introduced | Control-flow lane | `rtk dotnet test tests/Asynkron.JsEngine.Tests --filter "FullyQualifiedName~UnifiedBytecodeProductionEligibilityTests&FullyQualifiedName~Evaluate_LabeledBreakCrossingDriverLoop_AcceptsWithDriverCleanupTopology"` |
| `prototype-guard:JumpIfFalse` | `JumpIfFalse` and short-circuit conditional jump opcodes are admitted only for proven compiler-owned expression and statement control flow | Existing control-flow route if an unowned conditional shape is introduced | Control-flow lane | `rtk dotnet test tests/Asynkron.JsEngine.Tests --filter "FullyQualifiedName~UnifiedBytecodeProductionEligibilityTests&FullyQualifiedName~Evaluate_ConditionalExpression_ThisPropertyConditionAndArms_Accepts"` |
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

### General Expression Lowering Gaps (current)
- None.

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
  (activation-resolved base, supported computed-key span, simple RHS,
  non-optional target). Direct computed assignments, compound
  writes, logical writes, and
  updates may use the same supported computed-key expression span as optional
  computed reads (`box[key + suffix] = y`, `box[key + suffix] += y`,
  `box[key + suffix] &&= y`, `box[key + suffix]++`) when the RHS is a simple
  operand and the operator-specific shape is already admitted. Simple nested
  named receiver assignments, compound writes, logical writes, and updates
  (`box.child.value = y`,
  `box.child.value += y`,
  `box.child.value &&= y`, `box.child.value++`) are admitted through
  `TryIsFirstBoundaryNestedNamedPropertyWriteCandidate` and
  prefix-aware `TryIsFirstBoundaryNamedCompoundPropertyWriteCandidate` and
  prefix-aware `TryIsFirstBoundaryNamedLogicalPropertyWriteCandidate` and
  `TryIsFirstBoundaryNestedNamedPropertyUpdateCandidate`, which compile the
  receiver chain as owned `GetNamedProperty` opcodes before the final
  `SetNamedProperty` / `UpdateNamedProperty`. Optional named and computed read
  chains, including named-then-computed continuations, may also start after a
  plain named receiver prefix (`box.child?.value`, `box.child?.[key]`,
  `box.child?.items[key]`), with the prefix emitted as owned `GetNamedProperty`
  reads before the optional jump-to-chain-end boundary. Nested named receiver
  chains ending in a simple
  computed delete (`delete box.child[key]`, `delete box.child[left + right]`)
  are admitted by `TryIsFirstBoundaryComputedPropertyDeleteCandidate`; optional
  named deletes (`delete box?.value`, `delete box.child?.value`) and optional
  computed delete chains (`delete box?.[key]`, `delete box?.child[key]`,
  `delete box.child?.[key]`, and `delete box?.child?.[key]`) are admitted for
  activation-resolved receivers, supported computed-key spans, and
  compiler-owned nullish short-circuit-to-true lowering (ADR 0317 plus the
  optional delete follow-ups). The compiler emits the named receiver reads and
  final `DeleteNamedProperty` /
  `DeleteComputedProperty`, while the VM's descriptor-aware delete helper owns
  strict/sloppy results.
  Retained declines include unsupported chained optional delete neighbors,
  private receiver-chain/mutation neighbors outside the admitted direct named
  shape, dynamic lookup, unsupported computed-key spans, unsupported RHS
  spans, and optional/super/private neighbors of computed-key mutation shapes.
  Private member deletes are rejected before this production gate.
  Note: slot-identifier logical assignment (`x &&= y`) remains admitted.
- Super-property reads, writes, and updates are now VM-owned through
  `EnsureSuperReference`, `GetNamedSuperProperty`, `GetComputedSuperProperty`,
  `SetNamedSuperProperty`, `SetComputedSuperProperty`,
  `UpdateNamedSuperProperty`, and `UpdateComputedSuperProperty`.
  `SuperPropertyDependency` remains only as the safety net for super call-target
  preparation outside the first production invocation boundary.
- Sloppy-mode `this` coercion is handled before VM entry: `boundThis` is
  computed via `CoerceThisValueForNonStrict` in `TryInvokeProductionUnifiedBytecode`,
  so the VM's `LoadThis` opcode always receives the correctly coerced value.
- Simple-return arrow functions whose lowered body has no super, unadmitted
  call-target, nested function/class literal, or other unowned dependency may
  route.
  Captured lexical `this` / `new.target`, flat-slot reads, ordinary environment
  identifier reads/calls, captured identifier assignment, compound assignment,
  update, and delete, and named/computed property reads, simple property writes,
  compound/logical property writes, property updates, or property deletes from
  those dynamic identifier bases are VM-owned.
  Dependency-bearing arrows still decline via the `IsArrowFunction`,
  captured-activation, or `_lexicalThisEnvironment is not null` pre-gates.
- Simple-return ordinary function expressions that capture an outer activation
  may route when the body uses the same ordinary environment identifier,
  captured identifier call, assignment, compound assignment, update, delete,
  named/computed property read, simple property write, compound/logical property
  write, property update, or property delete subset. `arguments`, eval,
  with-backed closures, super/private/home-object shapes, block-body mutations,
  and nested function/class literals remain outside this route.
- There is no retained `this` decline in the production activation descriptor;
  future shapes that cannot thread `this` through the VM should introduce a
  concrete gate with a current failing example instead of carrying a placeholder.

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
- Driver-crossing labeled `break`/`continue` is owned by compiler-emitted
  cleanup topology, not a separate label decline bucket. Future unsupported
  loop-control shapes should use the concrete driver-state or plan-shape gate
  that matches the failing topology.
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
- Unsupported binding declaration shapes, unsupported object/array destructuring
  driver instructions, unsupported dynamic lookup, captured activation,
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
- In with-backed programs, `PrepareDynamicIdentifierCallTarget` must resolve an
  active with binding regardless of identifier-cache state. When the binding
  exists, the receiver is the with binding object and the callee is read through
  that captured binding.
- Dynamic identifier support does not make dynamic activation globally
  admissible. Direct eval is admitted only through the one-argument non-spread
  eval-identifier call boundary; unresolved non-with dynamic activation,
  captured dynamic activation, arguments-object dependencies, and async/generator
  functions still decline before VM execution.

## Production Call Invocation Boundary
- Current executable call support covers
  activation-resolved identifier calls, direct named member calls whose
  optional-free named receiver chain is activation-resolved or starts from an
  admitted dynamic identifier base, and direct computed member calls whose
  receiver chain remains inside the shallow computed-call boundary. Arguments
  may be simple literal or slot operands, simple
  named/computed property-read spans (`box.count`, `box[key]`), baseline
  single-hop optional named property-read spans (`box?.value`, emitted as
  `[base, GetNamedPropertyOptional]` so a nullish base yields `undefined`
  without leaving the VM; chained optional reads such as `box?.value?.nested`
  still decline with `OptionalChainDependency`), optional-start-then-plain named
  read chain spans (`box?.child.value`, `box?.child.nested.deep`, emitted as the
  proven `JumpIfNullishReplaceUndefined(end)` head jump plus plain
  `GetNamedProperty` continuation reads so a nullish head short-circuits the whole
  chain to `undefined`; a second optional hop such as `box?.child?.value` still
  declines with `OptionalChainDependency`), optional-named-then-plain-computed read
  chain spans (`box?.prop[key]`, `box?.prop[a + b]`, `box?.a.b[key]`, emitted as the
  proven `JumpIfNullishReplaceUndefined(end)` head jump at the optional named hop,
  the named hop/continuation reads, the computed key span, `GetComputedProperty`, and
  trailing plain `GetNamedProperty` reads so a nullish head short-circuits the whole
  chain to `undefined`; a second optional hop such as `box?.prop?.[key]` still declines
  with `OptionalChainDependency`), or simple
  array/object literal spans (`[a, b]`, `{x: a, y: b}`), including nested
  simple, simple-binary, simple-unary, simple-property-read, simple-control-
  expression, or simple-`typeof` array/object/template literal values or elements
  (`{items: [a, b]}`, `[{x}]`, `{total: a + b}`,
  `{value: -x}`, `{value: box.count}`, `{value: box[key]}`,
  `{value: left && right}`, `{value: condition ? yes : no}`,
  `{[left && right]: value}`, `{[condition ? yes : no]: value}`,
  `{kind: typeof x}`, ``{label: `hello ${name}`}``) and object literal spread entries
  whose spread source is a simple operand (`{...source}`) or admitted
  simple-control expression (`{...(left && right)}`,
  `{...(condition ? left : right)}`, `[...(left && right)]`,
  `[...(condition ? left : right)]`), with no name inference in that restricted
  simple-span form (gh2705, ADR 0290). General object method/accessor literal
  construction is VM-owned outside that restricted span. Computed member keys
  must also be simple literal or slot operands. For accepted member receiver chains, the call
  receiver is the final resolved receiver object, not the root object.
- Synchronous spread calls are admitted (gh2676): `f(...args)`, `f(...a, ...b)`,
  `obj.method(...args)`, and mixed `f(a, ...b, c)`. Each argument (positional or
  spread) lowers to one value-producing load; the `CallInvocationBoundary`
  operand low 16 bits hold the pushed argument value count and the high bits hold
  a spread-mask reference (`spreadMaskIndex + 1`, where `0` means no spread). The
  VM flattens spread iterables left-to-right at the boundary via the shared
  `TypedAstEvaluator.EnumerateSpread` helper, preserving iteration order and
  side-effects, then invokes through the existing callable helpers with
  receiver-as-`this`. Optional identifier spread calls such as `fn?.(...args)`
  skip argument lowering when `fn` is nullish and otherwise use the same
  spread-mask boundary. Optional-start computed member spread calls such as
  `a?.box[key](...args)` use the same spread-mask boundary after the optional
  receiver has been resolved. Direct eval is admitted only for the one-argument
  non-spread eval-identifier shape; multi-argument/spread direct eval and super
  spread stay declined.
- Synchronous construct calls are admitted (gh2690 plus the construct-boundary
  widening): `new F(...)`, `new F(...args)`, `new box.Ctor(...)`, and
  `new box[key](...)` when the constructor/key/argument subexpressions are
  already production-safe. Construct results may feed trailing plain named
  property reads such as `new F(...args).x`. The constructor value and each
  logical argument are pushed left-to-right by their own loads (no
  receiver/`this`), then a single `ConstructInvocationBoundary` opcode invokes
  `[[Construct]]` with the constructor itself as `new.target`, mirroring the
  spec-conformant construct reference helper. Its operand uses the same
  low-16-bit argument count plus high-bit spread-mask reference encoding as
  `CallInvocationBoundary`; the VM flattens spread iterables at the construct
  boundary before calling `ReflectHelper.Construct`. A non-constructor target
  throws `TypeError` at the boundary.
- Derived-constructor `super(...)` calls are admitted through
  `SuperConstructInvocationBoundary`, including spread arguments such as
  `super(...args)` and simple property-read argument spans such as
  `super(items.length)` or `super(items["length"])`. The VM resolves the dynamic
  super constructor from the current super binding, lowers each admitted
  argument in source order, flattens spread iterables at the boundary when
  needed, invokes `[[Construct]]` with the caller's `new.target`, initializes
  `this`, and preserves the existing double-super-call `ReferenceError`.
- Accepted identifier-call programs use `PrepareIdentifierCallTarget`,
  `PrepareIdentifierOptionalCallTarget`, `PrepareDynamicIdentifierCallTarget`,
  or `PrepareDynamicIdentifierOptionalCallTarget` followed by
  `CallInvocationBoundary`; the VM resolves the callable from unified
  bytecode-owned slot state or the active dynamic environment and invokes it
  through existing callable invocation helpers with the active
  `EvaluationContext` and caller `JsEnvironment` when the callee needs
  environment-aware or debug-aware invocation state. The optional variants pack
  a nullish short-circuit jump target and jump past argument lowering and the
  call boundary when the callee is nullish. Identifier-call arguments can be
  activation-slot or admitted dynamic identifier operands, including
  simple-binary spans, simple named/computed property-read spans, and simple
  array/object/template literal spans containing those operands.
- Accepted named member-call programs use `PrepareNamedCallTarget` followed by
  `CallInvocationBoundary`; the VM keeps the final receiver on the stack, loads
  the named callee from that receiver, and invokes through the existing
  receiver-as-`this` stack contract. Member-call arguments can use the same
  activation-slot or admitted dynamic identifier operands, simple-binary spans,
  simple named/computed property-read spans, and simple array/object/template
  literal spans as
  identifier calls when the receiver/key boundary is otherwise admitted.
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
- Ordinary non-optional identifier/member/super call-target opcodes now lower
  through the general expression loop. Direct eval and optional-call shapes
  still use the first-boundary call-target helper because they carry
  boundary-local directness metadata or packed nullish short-circuit jump
  targets.
- Accepted optional call programs either use a jump-owned optional-start prefix
  followed by an ordinary call-target preparation opcode (`a?.b.c(args)`,
  `a.x?.b.c(args)`, `a?.b[k](args)`) or use
  `PrepareIdentifierOptionalCallTarget`,
  `PrepareDynamicIdentifierOptionalCallTarget`,
  `PrepareNamedOptionalCallTarget`, or `PrepareComputedOptionalCallTarget`
  followed by `CallInvocationBoundary`.
  The optional preparation opcodes pack a call-target constant index (low 16
  bits) and a nullish short-circuit jump target (high 16 bits) into a single
  operand. The VM checks the receiver or callee for nullish before argument
  evaluation proceeds; if nullish, the stack top becomes `Undefined` and
  execution jumps past the call boundary. Admitted callee/receiver-optional
  patterns include activation-resolved and captured/dynamic callee-optional
  identifier calls (`fn?.(args)`), receiver-optional named/computed calls
  (`box?.read(args)`, `box?.[key](args)`), and callee-optional named/computed
  calls (`box.read?.(args)`, `box[key]?.(args)`). The
  `IsOptionalReceiverCheck` flag in
  `UnifiedBytecodeCallTarget` distinguishes the two named optional variants;
  receiver-optional computed calls use the jump-owned prefix, while
  callee-optional computed calls use `PrepareComputedOptionalCallTarget`.
  (Optional member calls admitted as of gh2689; ADR 0289.)
- Direct eval programs use `PrepareIdentifierCallTarget` followed by
  `CallInvocationBoundary` with the direct-eval operand flag. The admitted shape
  is exactly one non-spread eval-identifier argument; the VM invokes the eval host
  with the caller environment and syncs mutated caller slots back into unified
  bytecode-owned slots before execution continues.
- Private-adjacent member targets outside the admitted direct named method-call
  shape, arguments-object dependencies, unsupported non-with dynamic lookup,
  deeper computed-member call receiver chains, complex receiver/key shapes,
  spread-onto-optional calls, and receiver-binding-sensitive adjacent families
  still decline before VM execution. (Synchronous spread calls are admitted as
  of gh2676; synchronous
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
  `IteratorMoveNext`, and `IteratorClose` when the iterator kind is `Sync` and
  the iterable source is lowered to exactly one expression bytecode payload.
  That includes resumable async `for (x of await p)` sources, which compile the
  awaited source payload followed by `AwaitValue` and `IteratorInit`. Slice A
  (#2678) also admits a TDZ head environment (`for (const x of …)` /
  `for (let x of …)`): the compiler emits a
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
  flat slots. The same opcode family is admitted by the resumable VM, so
  generator and async bodies can suspend before or after a lowered array
  destructuring sequence while keeping the destructuring iterator state
  VM-owned. Normal and abrupt cleanup close the destructuring iterator from
  VM-owned driver state.
- Accepted object destructuring driver shapes include
  `ObjectDestructuringInit`, `ObjectDestructuringProperty`,
  `ObjectDestructuringRest`, and `ObjectDestructuringClose` when the source is
  lowered to expression bytecode, all property keys are static identifiers (no
  computed keys), there are no defaults or nested patterns, and every property
  and rest target resolves to a unified flat slot. The VM coerces the source
  with `ToObject`, reads properties in source order (observable getter side
  effects preserved), and a trailing rest target collects the remaining own
  enumerable keys minus the consumed ones. The same opcode family is admitted by
  the resumable VM for generator and async bodies that suspend before or after
  the lowered object destructuring sequence. Abrupt completion (a non-coercible
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
- Accepted resumable bodies may now compute NON-SUSPENDING value-tier
  operations between suspension points: property reads (`GetNamedProperty`,
  `GetComputedProperty` with its `RequireObjectCoercible` + `ResolvePropertyKey`
  prelude), `typeof` (`TypeOf`, `TypeOfIdentifier`), and the unary operators
  `UnaryPlus`, `UnaryMinus`, `UnaryLogicalNot`, `UnaryBitwiseNot`, `UnaryVoid`.
  These opcodes are ported into the `ExecuteResumable` switch against its local
  state machine and removed from the `TryFindUnsupportedResumableOpcode`
  unsupported set, so a generator/async whose body reads `o.a`/`o[k]` or applies
  unary/`typeof` between `yield`/`await` is admitted by `EvaluateResumable`
  (proof: `UnifiedBytecodeResumableValueTierTests`). Optional/short-circuit read
  variants (`GetNamedPropertyOptional`, `GetComputedPropertyOptional`) stay
  declined because the resumable state has no `stackShortCircuitFlags` array to
  persist short-circuit flags across suspension; `TypeOfDynamicIdentifier`,
  property updates/deletes remain follow-ups (property writes are now admitted —
  see the property-write entry below).
- Accepted resumable bodies may now DISPATCH synchronous calls between suspension
  points: non-optional `f()`, `o.m()`, and `o[k]()`. The
  `PrepareIdentifierCallTarget`, `PrepareNamedCallTarget`,
  `PrepareComputedCallTarget`, and `CallInvocationBoundary` opcodes are ported
  into the `ExecuteResumable` switch and removed from
  `TryFindUnsupportedResumableOpcode`. The dispatch calls the SAME
  `ExecutePreparedCall` helper the sync production route uses. The enclosing
  lexical environment (the generator/async closure) is threaded once onto
  `UnifiedBytecodeResumeState.CallingEnvironment` at construction so it survives
  suspension and feeds caller-context-sensitive callees; `slotEnvironments` is
  passed as `null` because resumable-eligible programs decline captured/dynamic
  activation and therefore have no environment-backed slots. A called function
  may itself be a generator/async, throw, or recurse: from the resumable frame
  the call is synchronous (the callee's own suspension is its own separate
  resumable frame), a throwing callee surfaces as a thrown step (matching the
  resumable `Binary`/`GetNamedProperty` throw handling), and a generator that
  drives another generator's `.next()` keeps both frames independent (proof:
  `UnifiedBytecodeResumableCallDispatchTests`). Direct eval, `super`/construct
  boundaries, and `TypeOfDynamicIdentifier`/`DeleteDynamicIdentifier` stay
  declined because the resumable state carries no dynamic-environment write
  plumbing. (Optional chains and optional calls are now admitted — see the
  optional-chain entry below.)
- Accepted resumable bodies may now use OPTIONAL CHAINS (`o?.a`, `o?.[k]`) and
  OPTIONAL CALLS (`o?.m()`, `f?.()`) between suspension points. The
  `GetNamedPropertyOptional`, `JumpIfNullishReplaceUndefined`,
  `JumpIfShortCircuited`, `PrepareIdentifierOptionalCallTarget`, and
  `PrepareNamedOptionalCallTarget` opcodes are ported into the `ExecuteResumable`
  switch and removed from `TryFindUnsupportedResumableOpcode`. The two prior
  resumable slices deferred these with the SAME reason: the resumable state had
  no equivalent of the sync VM's stack-parallel `stackShortCircuitFlags` array,
  so a short-circuit flag set before a suspension could not survive the resume.
  This slice adds `UnifiedBytecodeResumeState.OperandStackShortCircuitFlags`
  (allocated only when `program.RequiresShortCircuitStackFlags`, matching the
  sync `Execute` allocation), index-aligned with the operand stack. Because both
  `OperandStack` and the flag column are stable backing arrays referenced by the
  static loop — and a `yield`/`await` suspend only saves/restores `StackPointer`
  — the live flag window stays aligned with the live operand window across
  suspend AND resume. The resumable `GetNamedProperty`/`GetComputedProperty` and
  the named/computed call-target prepares now honor the flag exactly as the sync
  VM does, and `CallInvocationBoundary` clears it on the call-result slot so a
  non-short-circuited result never inherits a stale flag. Most admitted shapes
  short-circuit purely via the `JumpIfNullishReplaceUndefined` jump (their
  programs do not set `RequiresShortCircuitStackFlags`, so no flag column is
  allocated); the flag column is the correctness backstop for any flag-using
  shape. Proofs (`UnifiedBytecodeResumableOptionalChainTests`) cover the
  adversarial cases the contract demands: a nullish head whose short-circuit
  STRADDLES a `yield`/`await`, and a non-nullish optional read after a resume
  that must NOT inherit a prior chain's short-circuit. Computed optional calls
  (`o?.[k]()`, lowered with `PrepareComputedOptionalCallTarget`) remain declined
  because the resumable compiler declines that shape at compile time, so the
  opcode never reaches the resumable path; `super`/construct optional boundaries
  remain declined as before.
- Accepted resumable bodies may now resolve FREE/DYNAMIC IDENTIFIERS between
  suspension points: a free variable READ (`yield outerVar`, lowered to
  `LoadDynamicIdentifier`) and a free function CALL target (`yield helper(x)`
  where `helper` is module/script-level or a captured outer binding, lowered to
  `PrepareDynamicIdentifierCallTarget`). `EvaluateResumable` sets
  `allowsDynamicIdentifiers = true` in `TryFindResumablePlanDecline`, compiles
  with `allowsOrdinaryDynamicIdentifiers: true`, and the two opcodes are added to
  the `TryFindUnsupportedResumableOpcode` allowlist (that allowlist is the gate —
  free WRITES/updates `StoreDynamicIdentifier`/`ResolveDynamicIdentifierReference`/
  `UpdateDynamicIdentifier`, `TypeOfDynamicIdentifier`/`DeleteDynamicIdentifier`,
  and `eval`/`with` stay off it and therefore decline). The `ExecuteResumable`
  handlers resolve BY NAME against the LIVE closure environment threaded onto
  `UnifiedBytecodeResumeState.CallingEnvironment` (the same field #3108 threads
  for call dispatch), reusing the sync `GetDynamicIdentifierValue` /
  `PrepareDynamicIdentifierCallTarget` helpers. Because the environment is a live
  reference, a resumed step observes the CURRENT binding: a binding a sibling
  closure mutates while the frame is suspended, a global reassigned between
  yields, and a free callee rebound between yields all resolve to the new value,
  and an uninitialized free binding still throws ReferenceError (proof:
  `UnifiedBytecodeResumableDynamicIdentifierTests`). ARCHITECTURAL CARVE-OUT: a
  `yield* <iterable>` whose iterable resolves through a free/dynamic identifier
  (`yield* spyIterable`, `yield* makeIterator()`) is kept on the IR runner by a
  targeted `TryFindResumablePlanDecline` guard
  (`YieldStarIterableHasFreeIdentifierDependency`); the resumable `yield*`
  delegation protocol was only validated against slot-resolved iterables, so
  admitting a dynamic iterable would regress delegation semantics (8
  `Generator_YieldStar*` tests regressed without the guard and re-green with it).
  A binding captured from an *enclosing function* scope (resolves to that
  activation's slot, `ScopeId>=0`) is a distinct tier that remains declined.
- Accepted resumable bodies may now perform PROPERTY WRITES between suspension
  points: `o.x = v`, `o[k] = v`, and `this.x = v`. The `SetNamedProperty` and
  `SetComputedProperty` opcodes are ported into the `ExecuteResumable` switch and
  added to the `TryFindUnsupportedResumableOpcode` allowlist. The assignment value
  may itself suspend (`o.x = yield 1`); the base — and, for the computed form, the
  key — sit on the stable `UnifiedBytecodeResumeState.OperandStack` across the
  suspension and are restored on resume, the same mechanism the property READS
  rely on. Strict-only behavior (a write to a non-writable property throwing a
  `TypeError`, the sloppy form being silently ignored) is decided deep inside
  `JsOps`/`PropertyHandle` from `context.CurrentScope.IsStrict`; because a
  resumable step runs under the resume caller's scope rather than the body's, the
  body's own strictness is threaded onto a new `UnifiedBytecodeResumeState.IsStrict`
  (set in `SyncGeneratorInvoker`/`AsyncFunctionInvoker` from
  `function.Body.IsStrict || closure.IsStrict || isLexicallyStrict`, the same value
  the sync VM threads into `Execute`) and applied via a single `ScopeKind.Function`
  scope frame pushed for the duration of each `ExecuteResumable` step (popped on
  every exit, so the scope stack stays balanced across `yield`/`await`). Super-property
  writes and free dynamic writes stay off the allowlist and therefore decline (proof:
  `UnifiedBytecodeResumablePropertyWriteTests`, including the strict-throws /
  sloppy-ignored adversarial pair and a NEGATIVE pin that a free dynamic update
  `freeGlobal++` still declines).
- Accepted resumable bodies may now perform PROPERTY UPDATES and DELETES between
  suspension points: `o.x++`/`o[k]--` (prefix and postfix) and `delete o.x`/`delete o[k]`.
  The `UpdateNamedProperty`/`UpdateComputedProperty`/`DeleteNamedProperty`/
  `DeleteComputedProperty` opcodes are ported into the `ExecuteResumable` switch and
  added to the `TryFindUnsupportedResumableOpcode` allowlist. Like the property
  writes, these opcodes operate purely on the operand stack — the base (and, for the
  computed form, the key) sit on the stable `UnifiedBytecodeResumeState.OperandStack`
  across any suspension in a sibling sub-expression and are restored on resume — and
  the opcodes themselves cannot suspend (no `AwaitedProgram`), so they always run to
  completion inside one resumable step. The handlers reuse the sync VM's
  `UpdatePropertyValue`/`DeleteNamedProperty`/`DeleteComputedProperty` helpers under
  the body's own strictness (`state.IsStrict`, applied via the same per-step
  `ScopeKind.Function` scope frame the property writes use), so a strict update of a
  read-only property and a strict delete of a non-configurable property both throw and
  translate to the resumable Throw step, while the sloppy forms are no-ops; the
  computed update preserves the null/undefined-base `TypeError` guard before any read.
  Super-property updates/deletes and free dynamic updates/deletes
  (`UpdateDynamicIdentifier`, `DeleteDynamicIdentifier`) stay off the allowlist and
  therefore decline (proof: `UnifiedBytecodeResumablePropertyUpdateDeleteTests`,
  including postfix-vs-prefix value semantics, strict read-only/non-configurable
  throws, a computed-update-on-null `TypeError`, and async update/delete across an
  await).
- Accepted resumable bodies may now materialize REGEX LITERALS between suspension
  points: `/pat/flags`. The `LoadRegexLiteral` opcode is ported into the
  `ExecuteResumable` switch and added to the `TryFindUnsupportedResumableOpcode`
  allowlist. It is the literal twin of the sync handler: read the interned pattern
  string (`program.StringConstants[DecodeRegexLiteralPatternOperand(operand)]`) and
  the encoded flags byte (`DecodeRegexLiteralFlagsOperand(operand)`) from the program,
  then build a FRESH `RegExp` object via `RegExpHelper.CreateRegExpLiteral(...,
  context.RealmState)` and push it with `JsValue.FromObjectUnsafe`. This is the
  cleanest class of resumable admission: a pure constant materialization with no
  `AwaitedProgram` (the opcode cannot itself yield/await) and exactly one value pushed,
  so it always runs to completion inside one resumable step and never touches the
  operand stack across a suspension — no resume-state restoration is involved.
  ECMAScript requires a distinct `RegExp` per evaluation, so the object is constructed
  anew on every step (including each loop turn across yields) rather than cached,
  exactly as the sync VM does, preserving per-evaluation `lastIndex` independence
  (proof: `UnifiedBytecodeResumableRegexLiteralTests`, including the generator/async
  admit gates, a source/flags round-trip with a functional match after a yield, the
  fresh-object-per-evaluation identity guarantee in a loop, an all-flags
  escape-bearing pattern round-trip, and an async body evaluating a regex literal on
  the resumed step after an await).
- `this`-dependent async and generator programs are admitted (resumable-route
  counterpart to the ordinary sync `this` support; see Production This-Binding
  Boundary above and ADR 0283). The strict/sloppy-coerced `boundThis` is
  computed in the async and sync-generator invokers via
  `CoerceThisValueForNonStrict` and stored on `UnifiedBytecodeResumeState` at
  construction so it survives suspension/resume across `yield`/`await`. The
  resumable `LoadThis` opcode pushes `state.ThisValue`. A `this.x` property read
  now routes through the resumable value tier above (`LoadThis` then
  `GetNamedProperty`); only optional/dynamic/write `this`-member forms remain
  outside the resumable opcode set.
- Captured/dynamic activation, arguments objects, `new.target`, optional and
  super/construct call boundaries, dynamic lookup, labels,
  iterator/destructuring drivers, unsupported expression payloads, and unmodeled
  statement families still decline before VM execution. (Non-optional synchronous
  call dispatch is now admitted — see the call-dispatch entry above.)
- Async-generator `yield*`, awaited delegated sources, and unsupported
  delegated expression payloads remain outside production resumable routing
  until their promise/async-iterator settlement semantics are modeled by the VM.
  Focused issue #2955 coverage pins delegated async-generator `.return(value)`
  and `.throw(value)` on the existing IR async-generator path with no
  `unified-bytecode-resumable-*` route log.

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
  `IteratorCloseInstruction` are eligible for the synchronous lowered
  iterator-driver model described above, including the resumable awaited-source
  `for (x of await p)` lane. Async iterator drivers still remain outside the
  admitted boundary.
- `ForInInitInstruction` and `ForInMoveNextInstruction` are eligible for the
  lowered synchronous for-in driver model and the resumable awaited-source
  `for (k in await p)` lane described above. Unsupported for-in driver shapes
  still decline with `ForInDriverStateDependency`.
- `ArrayDestructuringInitInstruction`,
  `ArrayDestructuringElementInstruction`,
  `ArrayDestructuringRestInstruction`, and
  `ArrayDestructuringCloseInstruction` are eligible for the lowered
  array destructuring model described above. Targets resolve to a flat
  activation slot when one exists; at **script scope** the step-wise driver
  **state** symbol (`__arrDestr_iter`) is given a synthetic scratch activation
  slot by `AddSyntheticDestructuringStateSlots`, and a `VariableKind.Var`
  **target** that has no flat slot is stored dynamically by name through the
  driver descriptor's `TargetNameConstantIndex` (the `var` target is already
  hoisted into the materialized script environment before the VM runs).
  Non-`var` (`let`/`const`) script-scope targets, and all other unsupported
  destructuring shapes, still decline with `DestructuringDependency` or
  `UnsupportedPlanShape`.
- `ObjectDestructuringInitInstruction`,
  `ObjectDestructuringPropertyInstruction`,
  `ObjectDestructuringRestInstruction`, and
  `ObjectDestructuringCloseInstruction` are eligible only for the lowered
  direct-slot object destructuring model described above (static keys,
  identifier targets, no defaults, no nested patterns, optional identifier
  rest). At **script scope** the driver **state** symbol (`__objDestr_src`) is
  given a synthetic scratch activation slot, and `VariableKind.Var` **targets**
  with no flat slot store dynamically by name through the descriptor's
  `TargetNameConstantIndex`. Static nested binding declarations with
  identifier/rest targets are admitted through `ApplyDeclarationBindingTarget`.
  Computed/dynamic-name keys, defaults, assignment targets, non-`var` script
  targets, awaited binding values, and `using` declarations
  still decline with `DestructuringDependency` or `UnsupportedPlanShape`. ADR
  [`0284`](adrs/0284-keep-unified-bytecode-object-destructuring-model-first-and-static-key-owned.md)
  records the model-first decision and admit/decline boundary.
- `ExpressionOpKind.ApplyBindingTarget` is eligible for ordinary sync
  production assignment destructuring when the expression compiler can lift the
  lowered `BindingTargetProgram` into the unified program descriptor table.
- `ApplyDeclarationBindingTarget` is eligible for ordinary sync `var` / `let` /
  `const` binding declarations whose lowered `BindingTargetProgram` contains
  only static identifier/array/object/rest targets with no default or computed
  subprograms. This lane preserves existing binding-target semantics as a
  bridge; it does not make unsupported destructuring driver shapes
  production-eligible.
- Decision for this lane: model-first. Any future widening must preserve
  explicit driver-state descriptors and pre-VM declines for shapes that would
  require mixed IR/AST execution.

## Ranked Next Unsupported Buckets (current boundary)
1. Activation model gaps are still larger than any single expression-family
   neighbor. Ordinary sync production routing still pre-gates async-like
   functions, generators, arrows needing lexical-this environments or super
   bindings, closure or dynamic identifier dependencies, or non-simple bodies,
   real `arguments` object and sloppy mapped-arguments dependencies, non-simple
   parameter lists outside the admitted final-rest identifier and simple
   literal-default identifier routes, runtime-dependent default/destructured parameter
   expressions, broader
   class constructor routes beyond simple base constructors and the bounded
   explicit derived `super(...)` path, default derived constructors, and captured
   activation outside the explicit with-backed
   dynamic-name lane. Resumable
   unified bytecode exists, but `EvaluateResumable` admits a smaller
   instruction/opcode subset than ordinary sync production bytecode. That subset
   now includes the NON-SUSPENDING value-computation tier (property reads
   `GetNamedProperty`/`GetComputedProperty`, `typeof`, and the unary operators),
   so generator/async bodies that interleave value-tier reads with
   `yield`/`await` are admitted; the subset further includes synchronous call
   dispatch and now OPTIONAL CHAINS / OPTIONAL CALLS (`o?.a`, `o?.[k]`, `o?.m()`,
   `f?.()`) between suspension points, backed by an
   `OperandStackShortCircuitFlags` column on `UnifiedBytecodeResumeState` that
   persists short-circuit state across suspension. Dynamic-identifier
   `typeof`/`delete`, property writes/updates, computed optional calls
   (`o?.[k]()`), and `super`/construct boundaries inside resumable bodies remain
   unsupported follow-ups.
2. Wider call invocation remains a high-impact unsupported bucket. Synchronous
   spread calls are now admitted (gh2676). Optional calls are now admitted
   (gh2689, ADR 0289): `box?.read(args)`, `box.read?.(args)`, and
   `box[key]?.(args)`. Synchronous construct calls (`new F(...)`, spread
   arguments, member/computed constructor targets, and trailing plain named
   result reads such as `new F(...args).x`) are now admitted (gh2690, ADR 0286
   plus construct-boundary widening). The first bounded super invocation shapes
   are now admitted too (PR #2862, ADR 0307): derived-constructor
   `super(...)`, including spread arguments, plus named/computed super-member
   calls.
   Simple array and object literal arguments (`fn([a, b])`, `fn({x: a})`) are
   now admitted (gh2705, ADR 0290). Optional-start computed member spread calls
   (`a?.box[key](...args)`) are also admitted through the shared spread-mask
   invocation boundary, and named-prefix optional plain calls such as
   `a.x?.box.read(value)` route through the same optional-call machinery after
   the prefix is resolved by owned named-property reads. Simple object literal
   spread entries are now admitted through the `ObjectSpread` opcode when their
   spread source is a simple operand. Direct eval outside the one-argument
   non-spread eval-identifier boundary, private-adjacent member targets outside
   the admitted direct named method-call shape, complex receiver/key shapes,
   literal argument spans with non-simple nested values or elements, and
   receiver-binding-sensitive adjacent families beyond the direct activation-resolved and with-backed
   dynamic-identifier boundaries must still decline before VM execution.
3. Property and assignment widening is no longer a blanket property-read/write
   gap, but several member-expression neighbors remain outside production:
   private receiver-chain/mutation neighbors outside the admitted direct named
   shapes, optional-chain neighbors outside the admitted
   read/call/delete shapes, richer computed-key spans, unsupported RHS spans,
   optional/super/private mutation neighbors, and dynamic lookup outside the
   explicit with-backed lane. Private member deletes are parser early errors,
   not production declines.
4. Driver-state widening is next. Sync-driver TDZ head environments
   (`for (const x of …)` / `for (let k in …)`) are now admitted via the
   `TdzHeadInit` instruction (Slice A, #2678; see ADR 0288). Resumable awaited
   for-of and for-in sources are admitted for the `for (x of await p)` and
   `for (k in await p)` lanes; async iterator drivers remain outside the
   admitted boundary and must decline before VM execution.
5. Destructuring widening is still model-first. Simple array and object
   destructuring driver shapes are admitted (static keys, identifier targets,
   no defaults/nested patterns, optional identifier rest), and expression-level
   assignment destructuring that lowers through `ApplyBindingTarget` is admitted
   through the descriptor-backed binding-target bridge. Generic binding
   declarations, unsupported driver shapes, and targets outside the direct-slot
   or descriptor-backed assignment lanes remain outside the admitted boundary
   (`DestructuringDependency`).
6. Dynamic lookup families remain outside the admitted boundary
   (`DynamicLookupDependency`) except for the ordinary dynamic-name environment
   path, the direct-eval-backed ordinary dynamic-name path, and the explicit
   with-backed dynamic name slice above. Direct eval outside the admitted
   one-argument non-spread eval-identifier boundary, unresolved dynamic
   activation, and unsupported unresolved lookup shapes still decline before VM
   execution.
7. Label-dependent control flow is now admitted (ADR 0285): labeled statements,
   labeled loops, labeled block `break`, and labeled `break`/`continue` route
   through the compiler-owned resolved-target path. Driver-crossing label
   shapes are covered by the compiler-emitted cleanup topology rather than a
   standalone decline bucket.

## Proof Commands
```bash
rtk dotnet test tests/Asynkron.JsEngine.Tests --filter "FullyQualifiedName~ExpressionProgramCoverageMapTests"
rtk dotnet test tests/Asynkron.JsEngine.Tests --filter "FullyQualifiedName~UnifiedBytecodeProductionEligibilityTests"
rtk dotnet test tests/Asynkron.JsEngine.Tests --filter "FullyQualifiedName~UnifiedBytecodePrototypeTests"
rtk dotnet test tests/Asynkron.JsEngine.Tests --filter "FullyQualifiedName~UnifiedBytecodeProductionInvocationTests"
rtk rg "EvaluateExpression\\(|ProfileEvaluateExpression\\(" src/Asynkron.JsEngine/Ast/TypedAstEvaluator.ExecutionPlanRunner*
rtk ./tools/profile forloop --route-hits
rtk ./tools/profile propertyaccess --route-hits
rtk ./tools/profile functioncalls-lite --route-hits
rtk ./tools/profile activation-noargs-lite --route-hits
rtk ./tools/profile forofiteration --route-hits
rtk ./tools/profile forloop --memory
```
