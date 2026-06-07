# Unified Bytecode Expansion Contract

Date: 2026-06-07
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
- Current-main rebaseline: on this worktree (2026-06-06), the focused contract
  drift gate
  `rtk dotnet test tests/Asynkron.JsEngine.Tests --filter "FullyQualifiedName~ExpressionProgramCoverageMapTests&FullyQualifiedName~UnifiedBytecodeExpansionContract"`
  passed. The checked inventories remain current: 6 sync prototype opcode guard
  gaps, 5 resumable opcode allowlist gaps, 0 resumable instruction allowlist
  gaps, A35 split into 5 object-literal member leaves, B24 split into 9
  class-expression leaves, and no general expression lowering gaps.
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
- The sync prototype guard and resumable allowlists are now separately
  drift-checked against the declared opcode and instruction inventories. The
  current non-admitted inventories live under
  `### Sync Prototype Opcode Guard Gaps (current)`,
  `### Resumable Opcode Allowlist Gaps (current)`, and
  `### Resumable Instruction Allowlist Gaps (current)`. Future opcode or IR
  instruction additions must either be admitted by the corresponding gate or be
  named in those sections in the same slice.
- The remaining coarse P0.4 leaves are decomposed under
  `### A35 Object Literal Member Leaves (current)` and
  `### B24 Class Expression Leaves (current)`. A35 is sync-admitted across its
  concrete object-member opcodes; B24 remains real work for full bytecode
  execution because only the constructor/default-constructor class-literal
  subset, the B24b public non-computed instance-field subset without
  activation-capturing field initializers, the B24c public non-computed
  static-field subset, the mixed public non-computed static+instance-field
  subset whose instance initializers do not capture activation slots, and the
  B24g public non-computed instance-accessor subset whose accessor bodies do not
  capture activation slots, including accessor bodies that use `super`, the B24d
  static-block-only class-expression subset whose static blocks either avoid
  closure creation or create activation-capturing function literal closures
  through the materialized body-environment route, and the B24h public static/instance
  computed-field/method/accessor subset whose
  computed names either avoid resumable activation slots or use owned activation
  read/write/update/reference-store/delete operations (with unqualified
  identifier delete preserved as the class-body strict-error path) or immediate
  zero-argument computed-name IIFEs, including IIFEs that create escaping
  activation-capturing closures under the materialized body-environment route,
  and whose public computed instance field initializers may read activation
  slots or create activation-capturing function literals under that same
  materialized body-environment route, and whose public computed static field
  initializers may directly read/write/update activation slots through the
  class-literal slot environment and post-creation slot sync or create
  activation-capturing closures through the materialized body-environment route,
  and whose public computed member bodies and constructor bodies may capture
  activation slots through that same materialized body-environment route,
  and whose public computed member bodies and field initializer programs may
  use `super` when the superclass expression does not read resumable activation
  slots,
  including mixes with non-computed public fields/methods/accessors,
  noncapturing or activation-capturing private instance methods/accessors
  through the materialized body-environment route, non-static/non-computed
  private instance fields, and activation-safe private static fields, including
  direct activation-reading/updating and `super` private static initializers,
  under the same activation-safety rules, and
  eligible static blocks whose bodies route through production unified bytecode,
  including static blocks that create activation-capturing function literal
  closures or nested class declarations through the materialized
  body-environment route, and
  mixed public non-computed static fields plus static methods/accessors when
  field initializers compile as standalone unified bytecode and
  member/constructor bodies do not capture activation slots, are
  resumable-admitted. The VM handler still delegates
  class-definition evaluation to lower-level class machinery that can run
  expression programs and static-block IR plans. Direct activation-resolved
  constructs in computed names are admitted with spread and non-spread
  arguments, while the remaining B24h/B24i shapes keep member-super outside the
  admitted public-computed/public-non-computed subsets, noneligible static-block
  plan state, and activation-slot `extends` shapes declined.
- `UnifiedBytecodeCompiler` now has generated audit coverage for every declared
  IR instruction record. Function-scoped `FunctionDeclarationInstruction`
  entries compile as no-ops after fast activation hoisting installs the callable
  value, and descriptor-backed runtime declarations lower to `DeclareFunction`.
  Block-scoped function declarations behind unsupported lexical environment
  shapes can still decline through the surrounding `PushEnvironment` or call
  boundary, but they are no longer a stale compiler-decline row.
  `ClassDeclarationInstruction` is VM-owned through `DeclareClass`, with
  environment-backed lexical declaration installation and class-value creation
  handled inside the unified VM. The resumable route now admits the direct root
  simple class-declaration subset and activation-safe computed public
  class-declaration subset through the same opcode; `extends`, static blocks,
  and computed/static neighbors outside the public B24h-compatible subset remain
  declined before VM entry as later class-definition-state work. Static synchronous
  `BindingVariableDeclarationInstruction` shapes are now VM-owned through
  `ApplyDeclarationBindingTarget`; sync `using` declarations add an owned
  `RegisterDisposable` step before storage so resources are registered against
  the active environment. Direct function-body sync `using` declarations inside
  resumable generator/async bodies also route through `RegisterDisposable`, with
  disposal finalized when the resumable frame completes or throws. Binding
  defaults, computed binding names, assignment targets, awaited declarations,
  block-scoped resumable `using`, and `await using` declarations still decline
  before VM execution.
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
  Arrows that need a lexical-this environment still decline; arrows created by
  class field initializers can route with a stored super binding when their body
  contains super-property operations.
- Simple base class constructors with public or private instance fields and
  simple derived constructors with public or private instance fields can route through
  production unified bytecode. The production constructor bridge performs the
  existing base pre-body field initialization/private branding before VM entry
  and consumes the derived pending field initializer after `super(...)`.
- Resumable async/generator unified bytecode is narrower than ordinary sync
  production bytecode. The resumable eligibility opcode allow-list is now
  audited against the `ExecuteResumable` switch, so opcodes already implemented
  by the resumable VM cannot remain stale declines. Broad async/generator
  control flow, calls, dynamic lookup, arguments, and awaited iterator sources
  still decline.
- Async generators have a narrow production route through
  `AsyncGeneratorInvoker`: simple-parameter direct-yield `async function*`
  bodies can initialize `UnifiedBytecodeResumeState` and settle
  `Yield`/`Completed`/`Throw`/`PendingAwait` results from
  `UnifiedBytecodeVirtualMachine.ExecuteResumable` without constructing the
  `ExecutionPlanRunner`. Non-awaited async-generator `yield*` over delegated
  async iterables is admitted through the same resumable state: delegated
  `next`/`return`/`throw` results may suspend as `PendingAwait` and resume back
  into the VM-owned `YieldStar` driver. Async-generator `yield* await ...`
  now emits the awaited source followed by `AwaitValue` and the same
  VM-owned `YieldStar` driver.
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
- `RegisterDisposable`
- `DeclareDynamicVar`
- `DeclareDynamicLexical`
- `InitializeDynamicLexical`
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
- `EnsureSuperReference`
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
- `LoadClassLiteral`
- `LoadFunctionLiteral`
- `Yield`
- `StoreResumeValue`
- `AwaitAndDiscard`
- `AwaitValue`
- `AwaitedReturn`
- `YieldStar`
- `ApplyBindingTarget`
- `ApplyDeclarationBindingTarget`
- `EnsureHasName`

### Sync Prototype Opcode Guard Gaps (current)

These are the declared opcodes not explicitly admitted by
`UnifiedBytecodeProductionEligibility.TryFindPrototypeOnlyOpcode` for ordinary
sync production routing. They are resumable-only suspension opcodes; seeing one
in an ordinary sync production program still hits the default prototype guard
and falls back before VM entry.

- `AwaitAndDiscard`
- `AwaitValue`
- `AwaitedReturn`
- `StoreResumeValue`
- `Yield`
- `YieldStar`

### Resumable Opcode Allowlist Gaps (current)

These are the declared `UnifiedBytecodeOpCode` names not admitted by
`TryFindUnsupportedResumableOpcode`. The existing drift gate also verifies that
the admitted subset stays 1:1 with `UnifiedBytecodeVirtualMachine.ExecuteResumable`.

- `DeclareDynamicLexical`
- `DeclareDynamicVar`
- `DeclareFunction`
- `InitializeDynamicLexical`
- `SuperConstructInvocationBoundary`

### Resumable Instruction Allowlist Gaps (current)

These are declared `ExecutionInstruction` records not admitted by
`IsSupportedResumableInstruction`. The current source-derived inventory is
empty; the remaining resumable blockers are opcode/semantic gates rather than
statement-record allowlist gaps.

### A35 Object Literal Member Leaves (current)

The former A35 object-literal coarse row is split by the concrete unified
bytecode member-definition opcodes it exercises. These leaves are currently
sync-admitted and pinned by `A35ComplexObjectLiteralTests`,
`AlreadyRoutingShapePinTests`, and
`UnifiedBytecodeProductionEligibilityTests.Evaluate_ObjectMethodAndAccessorLiteralShapes_AcceptAndVmDefinesProperty`.

- `A35a:DefineComputedObjectProperty`
- `A35b:DefineObjectMethod`
- `A35c:DefineComputedObjectMethod`
- `A35d:DefineObjectAccessor`
- `A35e:DefineComputedObjectAccessor`

### B24 Class Expression Leaves (current)

The former B24 class-expression coarse row is split by class-element semantics.
`LoadClassLiteral` itself has sync VM and compiler coverage, including class
expressions with static blocks, and resumable routing now admits B24a
constructor/default-constructor class literals, the B24b public non-computed
instance-field subset whose field initializers do not capture activation slots,
B24c public non-computed static field class literals, the mixed public
non-computed static+instance-field subset whose instance initializers do not
capture activation slots, narrow B24e private instance-field class literals, and
the narrow B24f private method/accessor class-literal shape, plus the B24g
public non-computed instance-accessor subset whose accessor bodies do not
capture activation slots, including accessor bodies that use `super`, the B24d
static-block-only subset whose static blocks either avoid closure creation or
create activation-capturing function literal closures through the
materialized body-environment route, and the B24h public static/instance
computed-field/method/accessor subset whose computed
name programs either avoid resumable activation slots or use owned activation
read/write/update/reference-store/delete operations, including the strict-error
unqualified identifier delete path and bounded non-spread
activation-target calls whose argument region is already admitted by the
production complex-call-argument walker, plus immediate zero-argument
computed-name IIFEs, including IIFEs that create escaping activation-capturing
closures under the materialized body-environment route, and whose public
computed instance field initializer programs may read activation slots or create
activation-capturing function literals under that same materialized
body-environment route, and whose public computed static field initializer
programs may directly read/write/update activation slots through the
class-literal slot environment and post-creation slot sync or create
activation-capturing closures through the materialized body-environment route,
and whose public computed member bodies and constructor bodies may capture
activation slots through that same materialized body-environment route,
and whose public computed member bodies and field initializer programs may use
`super` when the superclass expression does not read resumable activation slots,
including mixes
with non-computed public fields/methods/accessors, noncapturing or
activation-capturing private instance methods/accessors through the materialized
body-environment route, non-static/non-computed private instance fields, and
activation-safe private static fields, including direct activation-reading/updating
and `super` private static initializers, under the same activation-safety rules,
plus eligible static blocks whose bodies route through production unified
bytecode, including static blocks that create activation-capturing function
literal closures through the materialized body-environment route,
plus public
non-computed static methods/accessors whose bodies do not capture activation
slots and public non-computed static-field class literals with `extends` whose
initializers compile as standalone unified bytecode and create no nested
closure, plus mixed public non-computed static fields and static methods/accessors
when static field initializers compile as standalone unified bytecode and
member/constructor bodies do not capture activation slots. Full
bytecode execution is not done: class-literal creation still calls
class-definition machinery that resolves extends/computed names/field
initializers through expression programs and static blocks through
`ExecutionPlanRunner.RunScript`; `extends` expressions that read resumable
activation slots also remain declined until class-definition evaluation owns
that environment bridge. The remaining B24h and B24i shapes remain declined by
the resumable shape gate for non-production-eligible static-block plans and
class-definition environment state; direct activation-resolved constructs with
spread and non-spread arguments now stay on `LoadClassLiteral`. Static-block
function declarations and nested class declarations whose bodies capture
resumable activation slots are inside the materialized-body-environment route
when the static-block body is otherwise production eligible.

- `B24a:ClassExpressionConstructor`
- `B24b:ClassExpressionInstanceFields`
- `B24c:ClassExpressionStaticFields`
- `B24d:ClassExpressionStaticBlocks`
- `B24e:ClassExpressionPrivateFields`
- `B24f:ClassExpressionPrivateMethods`
- `B24g:ClassExpressionAccessors`
- `B24h:ClassExpressionComputedMembers`
- `B24i:ClassExpressionSuperInMembers`

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

### Current `ExecutionPlanRunner` Owner Inventory

This inventory is the E5 baseline. It names current production reachability so
future slices can retire one owner surface at a time without deleting a live
semantic bridge or mis-parking dynamic residue as ordinary fallback work.

| Owner bucket | Current source surface | Current fallback route | Proof command |
|---|---|---|---|
| Ordinary sync fallback | `TypedAstEvaluator.SyncFunctionInvoker.TryInvokeIrFast` and adjacent constructor/class invocation bridges | Production unified bytecode is attempted before specialized parameter/binary routes and `SyncIrCallTrampoline`; the generic simple-return-expression `ExpressionProgram` branch is retired, and any remaining decline falls to the existing sync IR runner | `rtk rg -n "TryInvokeIrFast|UnifiedBytecodeExpressionProgramExecutor|new ExecutionPlanRunner|SyncIrCallTrampoline" src/Asynkron.JsEngine/Ast/TypedAstEvaluator.SyncFunctionInvoker.cs` |
| Script fallback | `TypedAstEvaluator.ExecutionPlanRunner.Core.RunScript` callers from script evaluation | Top-level scripts try the production selector first when eligible, then fall back to `ExecutionPlanRunner.RunScript` for remaining script safety declines such as unowned lexical/dynamic shapes | `rtk rg -n "ExecutionPlanRunner\\.RunScript|EvaluateScript|RunScript" src/Asynkron.JsEngine/Ast/TypedAstEvaluator.ExecutionPlanRunner.Core.cs src/Asynkron.JsEngine/Ast/Legacy/StatementNodeExtensions.cs` |
| Async function fallback | `TypedAstEvaluator.AsyncFunctionInvoker` | `EvaluateResumable` is attempted for admitted async bodies; declined bodies construct an `ExecutionPlanRunner` through `CreateClassifiedAsyncDeclinedBodyRunner`, while accepted super-property bodies snapshot `ResumableSuperBinding` into the resume state instead of constructing a runner-backed setup environment | `rtk rg -n "EvaluateResumable|CreateClassifiedAsyncDeclinedBodyRunner|TryCreateResumableSuperBinding|ResumableSuperBinding|new ExecutionPlanRunner|GetOrCreateExecutionEnvironmentForInternalUse" src/Asynkron.JsEngine/Ast/TypedAstEvaluator.AsyncFunctionInvoker.cs` |
| Generator fallback | `TypedAstEvaluator.SyncGeneratorInvoker` and `TypedAstEvaluator.GeneratorFunctionBase` | `EvaluateResumable` is attempted for admitted generator bodies; declined bodies use the classified `CreateClassifiedGeneratorDeclinedBodyRunner` boundary, while accepted super-property bodies snapshot `ResumableSuperBinding` into the resume state instead of constructing a runner-backed setup environment | `rtk rg -n "EvaluateResumable|CreateClassifiedGeneratorDeclinedBodyRunner|TryCreateResumableSuperBinding|ResumableSuperBinding|new ExecutionPlanRunner|GetOrCreateExecutionEnvironmentForInternalUse" src/Asynkron.JsEngine/Ast/TypedAstEvaluator.SyncGeneratorInvoker.cs src/Asynkron.JsEngine/Ast/TypedAstEvaluator.GeneratorFunctionBase.cs` |
| Async-generator fallback | `TypedAstEvaluator.AsyncGeneratorInvoker` | `EvaluateResumable` covers admitted async-generator bodies; declined bodies no longer construct `CreateClassifiedAsyncGeneratorDeclinedBodyRunner` and fail explicitly until the VM admits the declined semantics. Accepted super-property bodies snapshot `ResumableSuperBinding` into the resume state, and step-result adaptation is not itself route admission | `rtk dotnet test tests/Asynkron.JsEngine.Tests --filter "FullyQualifiedName~BytecodeProofManifestTests|FullyQualifiedName~UnifiedBytecodeAsyncGeneratorRouteTests"` |
| Class/static-block bridge | `ClassDefinitionExtensions.ExecuteStaticBlock` | Static-block-only class expressions can route through resumable `LoadClassLiteral`; eligible static-block bodies now execute their cached static-block plan through production unified bytecode before falling back to `ExecutionPlanRunner.RunScript`. Declined static-block bodies, including closure-producing blocks, remain a materialized-environment / IR-runner boundary. | `rtk rg -n "ExecuteStaticBlock|TryExecuteStaticBlockViaUnifiedBytecode|ExecutionPlanRunner\\.RunScript" src/Asynkron.JsEngine/Ast/ClassDefinitionExtensions.cs docs/plans/bytecode-burndown-checklist.md` |
| Standalone expression and binding bridges | `UnifiedBytecodeExpressionProgramExecutor`, `TypedAstEvaluator.BindingTargetPrograms`, and `ExecutionPlanRunner` helper entrypoints | `EvaluateStandaloneExpressionProgram`, `EvaluateLoweredExpressionProgram`, `EvaluateDynamicExpressionProgram`, and `ApplyStandaloneBindingTargetProgram` are deleted. Standalone `ExpressionProgram` payloads now execute through `UnifiedBytecodeExpressionProgramExecutor`, which compiles them with `UnifiedBytecodeCompiler.TryCompileStandaloneExpressionProgram` and executes `UnifiedBytecodeVirtualMachine` directly. Legacy dynamic AST expression callers use `UnifiedBytecodeExpressionProgramExecutor.ExecuteDynamic`, which still lowers/caches expression payloads before executing standalone unified bytecode. External lowered binding-target programs now call the static lowered binding-target core directly instead of constructing an `ExecutionPlanRunner`; expression payloads on that external binding path execute through standalone unified bytecode. Runner-internal binding-target calls still pass the active runner and remain part of the E5 runner retirement surface. The profiler-only expression-program loop is also gone: `ProfileRunner` compiles synthetic profile cases to standalone unified bytecode and executes the unified VM directly, while `ProfileEvaluateExpressionProgramLoop` is source-gated as absent. | `rtk rg -n "EvaluateStandaloneExpressionProgram|EvaluateLoweredExpressionProgram|EvaluateDynamicExpressionProgram|ApplyStandaloneBindingTargetProgram|UnifiedBytecodeExpressionProgramExecutor.ExecuteDynamic|ApplyLoweredBindingTargetProgram" src/Asynkron.JsEngine`; `rtk rg -n "ProfileEvaluateExpressionProgramLoop" src/Asynkron.JsEngine/Ast` |
| Dynamic residue boundary | Direct eval, live `with`, eval-injected runtime bindings, and `Function(...)`-produced bodies | Terminal dynamic residue stays out of ordinary E5 fallback retirement until a future issue owns those semantics explicitly | `rtk rg -n "Dynamic Residue|D1|D3|D4|Function\\(" docs/plans/bytecode-burndown-checklist.md docs/rules/unified-bytecode-prototypes.md` |

| Leaf | Owner surface | Current fallback route | Sync / resumable applicability |
|---|---|---|---|
| `A51a` | Invalid target, loop-shaped topology, and remaining loop-control diagnostics; switch-style breakable wrappers are sync-admitted; unsupported-entrypoint checks are defensive backstops for malformed manually-constructed plans, not active valid-plan declines | Existing execution-plan runner | both |
| `A51b` | Activation-slot metadata, slot-layout, declaration / assignment / update storage targets | Existing execution-plan runner | both |
| `A51c` | Catch binding, lexical dynamic declaration, active-with dynamic-name, TDZ-head binding storage | Existing environment-aware execution-plan runner | both |
| `A51d` | Defensive malformed-plan iterator, for-in, `yield*`, resume-target, and driver state-slot diagnostics; generated plans route through current synthetic slot layout | Existing iterator/generator/async IR drivers only for unsupported generated source payloads or malformed manual plan shapes | both; `yield*`/resume-target are resumable-owned |
| `A51e` | Array/object destructuring state slots and simple script declaration targets are compiler-owned for the admitted lane; remaining nested/default/parameter/disposal destructuring stays under R6 / `DestructuringDependency` | Existing destructuring IR helpers for remaining R6 shapes | both |
| `A51f` | Closed umbrella for general expression-loop unsupported op, binding-target expression, dynamic identifier, `arguments`, and private-neighbor gaps; decomposed into A51f1-A51f5 below | Expression-program / execution-plan runner | both |
| `A51g` | Call-target preparation, direct-eval call boundary, member/super/private call-target, invocation-boundary shapes | Existing call/eval/super IR paths | both; dynamic residue remains D1/D4 |
| `A51h` | Array/object/template literal, spread source, computed object key, simple literal-span shapes, and restricted literal/span helper diagnostics for unsupported simple or logical-control operands | Existing expression-program literal/spread evaluation | both |
| `A51i` | Computed/optional/private property-read and receiver-boundary shapes; read-only private receiver-prefix chains such as `receiver.#child.value` and `receiver.#child[key]` are now sync-admitted through owned property-read opcodes; unsupported property-read base diagnostics stay here | Existing expression-program property evaluation for remaining private-adjacent property reads | both |
| `A51j` | Property write, compound/logical write, update, delete, name-inference, key-span, and RHS-span shapes | Existing expression-program mutation evaluation | both |
| `A51k` | Simple binary/unary/control/conditional operand spans; nested admitted literal-value operand spans now route through the production VM, while call/private/dynamic-neighbor operands and unsupported simple-operand helper diagnostics remain outside this lane | Existing expression-program evaluation | both |
| `A51l` | Catch/try/driver cleanup topology diagnostics not otherwise captured by concrete driver rows | Existing execution-plan runner | both |
| `A51m` | Closed measured property-read span rollback diagnostics; source guard pins named, optional named, optional named-chain, and computed measured span rollback templates | Existing property-read expression evaluation for non-admitted shapes | both |
| `B47a` | Resumable-only `yield*` state-slot and synthetic resume-target layout | Existing generator/async execution-plan route | resumable only |

### A51f Expression-Loop Leaves (current)

The A51f umbrella is decomposed rather than closed as a runtime widening. The
current compiler still has source-backed declines in
`UnifiedBytecodeCompiler.TryAppendExpressionProgramOps` and nearby expression
value-load helpers, and eligibility keeps the existing fallback route intact for
non-admitted sync and resumable bodies. A51f1, A51f2, and A51f4 are closed
exceptions: A51f1's default is defensive because every declared
`ExpressionOpKind` has a direct general-loop case, A51f2's remaining diagnostic
is defensive because generated contexts now supply the descriptor builder, and
A51f4's former implicit-`arguments` assignment/call/`typeof` compiler
diagnostics are gone.

- `A51f1:GeneralExpressionLoopUnsupportedOp` - closed source ratchet. Every
  declared `ExpressionOpKind` has a direct case in the general expression loop;
  the remaining default diagnostic is malformed/manual-op protection. Real
  helper-level unsupported simple operand, literal control span, and
  property-read base diagnostics are owned by A51h/A51i/A51k.
- `A51f2:BindingTargetExpressionContext` - generated expression-program
  compiler contexts now supply binding-target constants, including standalone
  expression compilation. `ApplyBindingTarget` therefore compiles to unified
  bytecode for the generated assignment-destructuring lanes; the nullable
  helper guard remains only as malformed/manual-context protection.
- `A51f3:DynamicIdentifierExpressionLoop` - unresolved identifier loads, stores,
  updates, references, calls, deletes, and `typeof` remain gated by active-with or
  materialized-activation dynamic-name ownership.
- `A51f4:ImplicitArgumentsExpressionLoop` - closed. Implicit `arguments`
  assignment references now lower through dynamic identifier references,
  including expression-result assignments and complex call arguments; nested
  implicit `arguments()` call targets inside complex call arguments lower through
  `PrepareDynamicIdentifierCallTarget`; and ordinary `typeof arguments` uses the
  bounded object/dynamic-type path. Parameter or lexical `arguments` bindings
  remain activation-slot cases. Optional-call shapes such as `arguments?.()`
  remain owned by the generic optional-chain/call-target boundary, not this
  arguments-specific compiler leaf.
- `A51f5:PrivateNeighborExpressionLoop` - private named property reads/deletes,
  private receiver-neighbor reads, and private super/member call neighbors stay
  declined until private-name lexical state is owned by the VM route.

### Compiler Decline Reason Templates (current)

This exact template list is drift-checked against non-empty `reason = ...`
assignments in `UnifiedBytecodeCompiler.cs`. Updating, adding, or removing one
of these templates requires updating this contract and the checklist owner leaf
above in the same slice.

- `Activation slot metadata is required.`
- `Array literal RHS span does not match expected boundary.`
- `Awaited declaration target '{awaitedDeclarationTargetSymbol.Name}' is not eligible for unified bytecode storage.`
- `Binary underflow in complex call argument.`
- `Binding-target expressions are not available in this unified bytecode compilation context.`
- `Complex call argument region did not produce the expected operand count.`
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
- `Computed-prefix computed property writes with name inference are not supported.`
- `Computed property reads require RequireObjectCoercible(Depth: 1) in the first production boundary.`
- `Computed property reads require ResolvePropertyKey in the first production boundary.`
- `Computed property writes with name inference are not supported in the general expression loop.`
- `Computed property writes with name inference are not supported.`
- `Computed super call targets require exactly one computed key operand.`
- `Declaration target '{declarationTargetSymbol.Name}' is not eligible for unified bytecode storage.`
- `Direct eval call-target preparation requires an eval identifier target.`
- `Dynamic identifier assignment references are not eligible in complex call arguments.`
- `Dynamic identifier assignment references are not eligible outside an active with environment.`
- `Dynamic identifier reference store without a pending dynamic reference in complex call argument.`
- `Expected ArrayPush or ArraySpread after array element operand.`
- `Expected ArrayPush or ArraySpread after element.`
- `Expected CreateArray at index {startIndex}.`
- `Expected CreateObject at index {startIndex}.`
- `Expected DefineComputedObjectProperty after key, ResolvePropertyKey, and value.`
- `Expected DefineObjectProperty or value operand after first operand.`
- `Expected value operand after ResolvePropertyKey.`
- `Failed to emit measured named property read span.`
- `Failed to emit measured computed property read span.`
- `Failed to emit measured optional named property read span.`
- `Failed to emit measured optional named read chain span.`
- `Identifier '{identifier.Name.Name}' requires dynamic lookup and is not eligible outside an active with environment.`
- `Identifier '{storeIdentifier.Name.Name}' requires dynamic lookup and is not eligible outside an active with environment.`
- `Identifier '{storeIdentifier.Name.Name}' resolves to an activation slot and is not eligible for dynamic unified bytecode assignment references.`
- `Identifier assignment reference '{referenceIdentifier.Name.Name}' requires dynamic lookup and is not eligible in complex call arguments.`
- `Identifier assignment reference '{referenceIdentifier.Name.Name}' requires dynamic lookup and is not eligible outside an active with environment.`
- `Identifier assignment reference '{referenceIdentifier.Name.Name}' resolves only by activation-slot name lookup and is not eligible for slot-reference unified bytecode assignment lowering.`
- `Identifier assignment reference '{storeReferenceIdentifier.Name.Name}' cannot store through a pending slot-reference target using the dynamic-name path.`
- `Identifier assignment reference '{storeReferenceIdentifier.Name.Name}' does not match the pending slot-reference target.`
- `Identifier assignment reference '{storeReferenceIdentifier.Name.Name}' requires dynamic lookup and is not eligible outside an active with environment.`
- `Identifier assignment reference '{storeReferenceIdentifier.Name.Name}' resolves only by activation-slot name lookup and is not eligible for slot-reference unified bytecode assignment lowering.`
- `Identifier assignment references were left pending after unified bytecode expression lowering.`
- `Identifier reference load without a pending reference in complex call argument.`
- `Identifier reference pop without a pending reference in complex call argument.`
- `Identifier reference store target mismatch in complex call argument.`
- `Identifier reference store underflow in complex call argument.`
- `Identifier call target '{callTargetIdentifier.Name.Name}' requires dynamic lookup and is not eligible.`
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
- `Only one-argument non-spread direct eval is supported by the call-target preparation boundary.`
- `Only one-argument explicit-this non-spread direct eval is supported in the general expression loop.`
- `Only optional and simple identifier catch bindings are eligible for unified bytecode compilation.`
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
- `Private named super call target in complex call argument.`
- `Private named super call targets are outside the call-target preparation boundary.`
- `Private named super call targets are outside the general expression loop boundary.`
- `Private names and name inference are not admitted in simple object literals.`
- `Private nested named property receiver reads are not supported.`
- `Private nested named property updates are not supported.`
- `Private nested named property writes are not supported.`
- `ResolvePropertyKey underflow in complex call argument.`
- `Resume target '{resumeSymbol.Name}' is not in the activation slot layout.`
- `Spread sources only admit direct named member calls with simple arguments.`
- `Template literal RHS span does not match expected boundary.`
- `Template literal RHS span does not match expected nested property-write boundary.`
- `TypeOf underflow in complex call argument.`
- `Unary underflow in complex call argument.`
- `Unsupported array destructuring state slot '{arrayDestructuringClose.IteratorSlot.Name}'.`
- `Unsupported array destructuring state slot '{arrayDestructuringElement.IteratorSlot.Name}'.`
- `Unsupported array destructuring state slot '{arrayDestructuringInit.IteratorSlot.Name}'.`
- `Unsupported array destructuring state slot '{arrayDestructuringRest.IteratorSlot.Name}'.`
- `Unsupported assignment target '{assignmentTargetSymbol.Name}'.`
- `Unsupported catch binding slot '{identifier.Name.Name}'.`
- `Unsupported compound assignment target '{compoundTargetSymbol.Name}'.`
- `Unsupported compound computed property binary operator '{binary.Operator}'.`
- `Unsupported compound property binary operator '{binary.Operator}'.`
- `Unsupported computed call target in complex call argument.`
- `Unsupported computed super call target in complex call argument.`
- `Unsupported computed property key op '{operation.Kind}'.`
- `Unsupported computed property key span.`
- `Unsupported computed property read in complex call argument.`
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
- `Unsupported iterator close state slot '{iteratorClose.IteratorSlot.Name}'.`
- `Unsupported iterator state slot '{awaitedIteratorInit.IteratorSlot.Name}'.`
- `Unsupported iterator state slot '{iteratorInit.IteratorSlot.Name}'.`
- `Unsupported logical assignment target '{logicalTargetSymbol.Name}'.`
- `Unsupported update target '{incrementTargetSymbol.Name}'.`
- `Unsupported logical control-expression operand in simple literal span.`
- `Unsupported loop control flow at instruction {instructionIndex}.`
- `Unsupported loop control flow at instruction {sourceInstructionIndex}.`
- `Unsupported member call receiver op '{propertyRead.Kind}'.`
- `Unsupported named call target in complex call argument.`
- `Unsupported named property read in complex call argument.`
- `Unsupported nested call in complex call argument.`
- `Unsupported object destructuring state slot '{objectDestructuringClose.SourceSlot.Name}'.`
- `Unsupported object destructuring state slot '{objectDestructuringInit.SourceSlot.Name}'.`
- `Unsupported object destructuring state slot '{objectDestructuringProperty.SourceSlot.Name}'.`
- `Unsupported object destructuring state slot '{objectDestructuringRest.SourceSlot.Name}'.`
- `Unsupported op '{op.Kind}' in complex call argument.`
- `Unsupported property-read base op '{operation.Kind}'.`
- `Unsupported simple operand op '{operation.Kind}'.`
- `Unsupported typeof identifier '{identifier.Name.Name}'.`
- `Update target '{updateIdentifier.Name.Name}' requires dynamic lookup and is not eligible outside an active with environment.`
- `delete identifier '{deleteIdentifier.Name.Name}' requires dynamic lookup and is not eligible outside an active with environment.`
- `eval call targets are not supported in complex call arguments.`
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
  now covers value-tier reads, synchronous call dispatch, synchronous CONSTRUCT
  dispatch (`new C(args)` via `ConstructInvocationBoundary`, reusing the sync
  `ExecutePreparedConstruct` helper with the constructor itself as `new.target`;
  no dedicated `Prepare*ConstructTarget` opcode exists, so the constructor and its
  arguments are pushed by ordinary value-loading ops and survive suspension on the
  operand stack like the call boundary, B11), optional
  chains/optional calls (`o?.a`, `o?.[k]`, `o?.m()`, `o.m?.()`, `f?.()`,
  `o[k]?.()`, `freeFn?.()` — B15), free/dynamic
  identifier reads and calls, free-identifier `typeof`
  (`typeof freeVar` via `TypeOfDynamicIdentifier` — unbound yields `"undefined"`
  with no throw, B25), free/dynamic-identifier UPDATE (`freeVar++`/`freeVar--`/
  `++freeVar`/`--freeVar` via `UpdateDynamicIdentifier`, B27 — resolves the
  enclosing/global binding against the threaded `CallingEnvironment` and mutates
  the SAME heap slot across each suspension; const-safety enforced by the
  environment, not by absent const-slot metadata) and free/dynamic-identifier
  DELETE (`delete freeVar` via `DeleteDynamicIdentifier`, B28 — self-contained:
  name + environment + isStrict → bool), free/dynamic plain WRITES (`freeVar = v`,
  including `freeVar = yield`, B26 — the pre-resolved `AssignmentReference` is
  stored on `UnifiedBytecodeResumeState` so the RHS cannot change the selected
  target while suspended), PROPERTY WRITES (`o.x = v`, `o[k] = v`, `this.x = v`
  via `SetNamedProperty`/`SetComputedProperty`), SLOT UPDATES and SLOT ASSIGNMENTS
  (`x++`/`x--`/`++x`/`--x` via `UpdateSlot` and `x = v` via `StoreSlot`,
  admitted for parameter, `var`, and lexical `let`/`const` targets; const writes are
  enforced by `UnifiedBytecodeResumeState` const-slot metadata), and PROPERTY
  UPDATES/DELETES (`o.x++`, `o[k]--`, `delete o.x`, `delete o[k]` via
  `UpdateNamedProperty`/`UpdateComputedProperty`/`DeleteNamedProperty`/
  `DeleteComputedProperty`) between suspension points; the OPTIONAL-COMPUTED call
  `o?.[k]()` (a leading optional hop, declined at the shared plan walk as
  `OptionalChainDependency` — distinct from the now-admitted computed-member
  optional call `o[k]?.()`, B15),
  free/global and captured dynamic compound/logical WRITES (`freeGlobal += x`, `freeGlobal &&= x`,
  B29 — the compiler now lowers these through
  `ResolveDynamicIdentifierReference` / `LoadDynamicIdentifierReference` and
  either `StoreDynamicIdentifierReference` or `PopDynamicIdentifierReference`
  for short-circuit cleanup),
  super-property deletes, and
  super-construct boundaries (`SuperConstructInvocationBoundary`, which
  remains outside legal generator/async source shapes and stays declined)
  inside resumable bodies remain outside it.
  - NESTED FUNCTION LITERALS (`var h = function(){...}`) inside generator/async
    bodies are now split by capture analysis. Non-capturing literals route through
    `ExecuteResumable` via `LoadFunctionLiteral` and `EnsureHasName`; object
    method/accessor literals use the same path. Generator, async-function, and
    async-generator nested function literals that capture a root body local also
    route after invocation setup materializes a body environment whose activation
    slots are mirrored from the resume-state flat slots and kept synchronized on
    slot writes. This admits `function* g(){ var n=1; var f=()=>n; yield f();
    n=2; yield f(); }`, so the closure observes `1` then `2` on the resumable
    generator route, and admits async-function / async-generator closures that
    cross `await`, observe post-resume slot writes, and are called directly inside
    the resumable body. Lexical-this/private-name dependent literals now route
    when the resumable invoker proves the context and creates a per-call
    invocation environment for nested arrows to capture. Function declarations
    nested inside function literals route as well: the outer resumable VM creates
    the literal, and the literal's normal invocation path owns declaration
    instantiation. The eligibility analyzer inspects those direct nested
    declarations through the literal AST so an inner declaration that captures an
    outer resumable slot requests the existing materialized body-environment
    proof instead of triggering an opaque B23/B36 decline. B23 is admitted; the
    remaining nested declaration work is in B36's dynamic/eval helper and complex
    class declaration lanes.
  - HOISTED NESTED FUNCTION DECLARATIONS (`function helper(){...}`;
    `FunctionDeclarationInstruction` / `DeclareFunction`) inside a generator/async
    body are now partially admitted (B36): direct root function-scoped
    declarations route when the resumable invoker pre-populates the helper's
    compiled VM flat slot before `ExecuteResumable` starts. Non-capturing
    helpers route in generators, async functions, and async generators. The
    sync-generator, async-function, and async-generator routes also admit helpers
    that capture root body locals after invocation setup materializes the
    resumable body environment and creates the helper against that environment, so
    the helper observes flat-slot mutations across `yield`/resume or
    `await`/resume. Recursive and sibling helper graphs now route through the
    same pre-populated declaration environment: all admitted root helpers are
    created against the shared closure/body environment, then the invoker fills
    each helper's VM flat slot and environment slot before `ExecuteResumable`
    starts, so self/sibling references resolve at call time from that environment.
    A43 admits descriptor-backed block/Annex B declarations on the sync route
    through materialized block environments. Direct root simple class declarations,
    plain class declarations whose superclass expression compiles as standalone
    unified bytecode (including activation-dependent `class Box extends Base {}`
    and activation-safe explicit public derived constructors), and activation-safe
    computed public class declarations now route through `DeclareClass` after the resumable
    invoker materializes the body environment. Static-block-only class declarations
    now route through the same `DeclareClass` path when every static block body is
    production-unified-bytecode eligible, including static blocks that create
    activation-capturing closures assigned to named static properties; the block
    body must log `unified-bytecode-production-fast-path static-block`, not the
    classified static-block IR fallback. Public instance computed-member
    `extends` declarations now route by composing the standalone superclass
    compile check, the B36 constructor predicate, and the B24h computed public
    member gate, including direct activation-call computed names such as
    `[key = read()]()` and immediate computed-name IIFEs that create escaping
    activation-capturing closures under the B24h materialized body-environment
    subset. Public instance super-member and public static super-member
    `extends` declarations now route even when their member bodies capture
    resumable activation slots, public instance super-field declarations route
    even when field initializers capture resumable activation slots, and public
    non-computed static-field `extends` declarations also route when each
    initializer compiles as standalone unified bytecode, including
    closure-producing static fields and static `super` field initializers. Direct
    root class declarations that mix public non-computed static fields with
    eligible static blocks now route as well, preserving static element order,
    descriptor-backed static-block function declarations and nested static-block
    class declarations can close over the materialized resumable body environment.
    Dynamic/eval helpers, non-production
    static-block plans, plus otherwise complex class declaration neighbors
    (computed/private/static shapes outside the public B24h-compatible subset)
    are the remaining separate B36 declaration-instantiation work.
    `DeclareFunction` remains off the resumable opcode allowlist because
    descriptor-backed block/Annex B declarations still need persisted
    materialized block environments across suspension; direct root
    `FunctionDeclarationInstruction` remains conditionally guarded by the
    invoker-proof activation flag and compiles to a no-op after pre-population.
    The boundary is pinned by `UnifiedBytecodeResumableNestedFunctionTests`.
- Captured function scopes outside the simple-return captured-closure route,
  unresolved non-with dynamic activation, arrow lexical `this` / `new.target`,
  and class-constructor activation outside the bounded constructor routes.
- Unbounded real `arguments` object dependency.
- Direct eval outside the one-argument non-spread eval-identifier boundary, and
  declaration-bearing eval literals that can inject runtime bindings into the
  caller activation.
- Out-of-boundary call-target preparation, complex receiver/key/call-target
  shapes, complex call arguments outside admitted simple/binary/unary/`typeof`/
  property-read/control-expression/literal spans, and remaining
  receiver-binding-sensitive adjacent call families.
- Unresolved identifier loads/stores/updates outside ordinary, with-backed,
  non-injecting direct-eval-backed, simple-return captured-closure dynamic-name
  paths, and accepted top-level dynamic-global read paths.
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
  spans, identifier-call and named-property-of-call array-spread sources (A33),
  identifier-call and named-property-of-call OBJECT-spread sources (A34),
  and simple-control-expression spans; richer computed object keys/values
  outside admitted simple, member-call, and simple-control-expression spans;
  object methods/accessors outside restricted simple literal spans.
- Private-name operations outside admitted `#name in obj`, direct private
  reads/writes/updates, direct private compound/logical writes, and direct
  private named method calls.
- Defensive malformed/manual-plan for-in/iterator driver state, `yield*`, and
  resume-target slot diagnostics; generated iterator, for-in, awaited-source,
  async-iterator, and `yield*` plans route through the current synthetic slot
  layout, while missing/mismatched manually constructed state or source payloads
  still decline before VM execution.
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
| `CapturedOrDynamicActivation` | Activation descriptor `HasCapturedOrDynamicActivation`, including captured dynamic activation outside the admitted captured-closure route or dynamic activation outside the admitted with-backed / non-with direct-eval dynamic-name lanes. The captured-closure lane now admits sync and resumable flat, nested, and colliding captured locals. The sync route also admits bounded non-with non-injecting direct-eval activation plus ordinary free dynamic read/store/call when captured dynamic activation and live with-chain dependencies are absent; declaration-free literal eval may read the caller's current activation and bounded implicit `arguments` object. Arguments-object-dependent direct-eval shapes outside that owned bounded route remain declined | Existing sync IR / environment route | Dynamic activation lane | `rtk dotnet test tests/Asynkron.JsEngine.Tests --filter "FullyQualifiedName~UnifiedBytecodeProductionInvocationTests&FullyQualifiedName~DirectEvalThenOrdinaryDynamicReadStoreCall"` |
| `ArgumentsObjectDependency` | Activation descriptor gate for unbounded real `arguments` object dependency; implicit `arguments` reads, expression-result assignments, assignment references inside complex call arguments, updates, deletes, and calls materialize the existing arguments object and use the bounded identifier route, including admitted simple literal-default and final-rest parameter routes, while parameter/lexical `arguments` reads, `typeof`, assignments, updates, deletes, and call targets route as activation slots | Existing sync IR / arguments-object route | Arguments object lane | `rtk dotnet test tests/Asynkron.JsEngine.Tests --filter "FullyQualifiedName~UnifiedBytecodeProductionEligibilityTests&FullyQualifiedName~Evaluate_CallArgumentImplicitArgumentsAssignment"` |
| `ArrowLexicalThisDependency` | Activation descriptor gate for arrow lexical `this` / `new.target` ownership before ordinary sync routing | Existing arrow invocation route | Arrow route lane | `rtk dotnet test tests/Asynkron.JsEngine.Tests --filter "FullyQualifiedName~UnifiedBytecodeProductionEligibilityTests&FullyQualifiedName~Evaluate_OrdinarySyncActivationDescriptorBlockers_DeclineBeforeCompile"` |
| `ClassConstructorActivation` | Activation descriptor gate for class constructor activation outside the admitted base constructor routes with simple identifier parameters, simple literal-default parameters, or final-rest identifier parameters; explicit derived-constructor `super(...)` routes with simple identifier parameters, simple literal-default parameters, final-rest identifier parameters, and post-super `this` body reads/writes; and default-derived constructor `super(...args)` forwarding route. Private-name constructor/class-brand state remains outside the admitted constructor activation lane, including brand-only classes whose constructor bodies contain no private expression op, until the production constructor bridge owns private-name lexical state. | Constructor route | Constructor boundary lane | `rtk dotnet test tests/Asynkron.JsEngine.Tests --filter "FullyQualifiedName~UnifiedBytecodeProductionConstructCallTests&FullyQualifiedName~Constructor"` |
| `CallDependency` | Direct eval outside the one-argument non-spread eval-identifier boundary, declaration-bearing eval literals that can inject runtime bindings, out-of-boundary call-target preparation, and complex call arguments excluding admitted simple/binary template-literal substitutions, simple/binary computed object keys, zero-argument activation-resolved identifier-call computed object keys, simple activation or dynamic identifier-call arguments, simple-binary arguments, nested simple, simple-binary, simple-unary, simple-property-read, simple unary (`fn(-x)`) and typeof-identifier (`fn(typeof x)`) operands, baseline single-hop optional named property-read (`fn(box?.value)`), optional-start named read chains (`fn(box?.child.value)`, `fn(box?.child?.value)`, `fn(box?.value?.nested)`), baseline optional computed property-read (`fn(box?.[key])`), optional-computed read chains (`fn(box?.[key].value)`, `fn(box?.[key]?.value)`, `fn(box?.[key]?.[key])`), optional-named-then-computed read chains (`fn(box?.prop[key])`, `fn(box?.a.b[key])`, `fn(box?.prop?.[key])`), simple-control-expression, and simple-`typeof` array/object/template literal values/elements, dynamic operands inside simple array/object/template call arguments for admitted identifier-call and member-call targets, and embedded named member calls on admitted dynamic identifier bases such as `Math.sqrt(...)` in supported expression spans. A9/A10 tail-call boundary (`ContainsTailPositionIdentifierCallReturn`): a STRICT function with a tail-position `return <identifier call>;` (e.g. `return countdown(n-1)`, including the conditional `return c ? a : countdown(...)` form) declines because the IR runner tail-call-optimizes strict self-recursion (flat native stack via `SyncFunctionInvoker.TryGetLegacySameFunctionTailRestartTarget`) and the production VM does not — routing it to the VM overflows the native stack for deep recursion. Non-strict tail identifier calls are never TCO'd anywhere and stay admitted; non-tail identifier calls (statement calls, call arguments/operands) stay admitted | Existing sync IR call route | Wider call invocation lane | `rtk dotnet test tests/Asynkron.JsEngine.Tests --filter "FullyQualifiedName~UnifiedBytecodeProductionEligibilityTests"`; tail-call lane: `dotnet test tests/Asynkron.JsEngine.Tests/Asynkron.JsEngine.Tests.csproj -c Release --no-build --filter "FullyQualifiedName~TailCallTests" -- xUnit.MaxParallelThreads=1` |
| `DynamicLookupDependency` | Unresolved identifier loads/stores/update outside the admitted ordinary, with-backed, non-injecting direct-eval-backed, simple-return captured-closure dynamic-name paths, and top-level dynamic-global read paths used by accepted script expression spans; plans that need non-injecting direct eval plus post-eval dynamic identifier reads now route when captured activation, with-chain, and arguments-object dependencies are absent, while eval-injected runtime bindings stay declined on the existing IR route. A10 (`HasOrdinaryDynamicCallTargetDependency`): a FREE/global identifier used purely as a CALL TARGET (`function f(){ return helper(4); }`) now enables the ordinary dynamic-name path on the sync route — the callee lowers to `PrepareDynamicIdentifierCallTarget` walking the threaded environment chain (receiver pushed is `undefined`, coerced only by the callee's own strict/sloppy mode; an unbound callee throws `ReferenceError`), mirroring the resumable admission. A9: per-instruction expression programs mean each sequential identifier call (`a(); return b();`) is its own single-call candidate, so identifier calls past the first invocation boundary route once the dynamic-name path is enabled | Existing sync IR / environment lookup route | Dynamic-name lane | `rtk dotnet test tests/Asynkron.JsEngine.Tests --filter "FullyQualifiedName~UnifiedBytecodeProductionEligibilityTests&FullyQualifiedName~Evaluate_DirectEvalDeclarationLiteral_DeclinesEvalInjectedRuntimeBinding"`; A9/A10 lane: `rtk dotnet test tests/Asynkron.JsEngine.Tests --filter "FullyQualifiedName~CallTargetAdmissionTests"` |
| `PropertyReadBoundaryOutOfScope` | Named/computed property reads outside the admitted activation-resolved and read-only dynamic-identifier-base boundaries, including captured closure dynamic bases; read-only private receiver-prefix chains are admitted when the private hop is an ordinary non-optional value read and the remaining chain is named/computed reads (`receiver.#child.value`, `receiver.#child[key]`), while private receiver-prefix calls, mutations, and optional-private neighbors still decline; deep PURE read chains whose base is a simple object/array literal are now admitted (A17/A18, `TryIsFirstBoundaryLiteralBasePropertyReadChainCandidate`, sync route only) — e.g. `({a:{b:{c:1}}}).a.b.c`, `({a:1})['a']['b']`, `[box][0].a.b`, validated by a single stack-discipline walk over the literal-construction + named/computed read vocabulary; any call/write/optional/short-circuit/private/method/accessor hop declines, so a getter in the chain runs exactly once per hop | Existing sync IR property route | Property read widening lane | `rtk dotnet test tests/Asynkron.JsEngine.Tests --filter "FullyQualifiedName~UnifiedBytecodeProductionEligibilityTests&FullyQualifiedName~Evaluate_PrivateReceiverPrefixNamedPropertyRead_AcceptsOwnedPropertyOpcodes|FullyQualifiedName~Evaluate_PrivateReceiverPrefixComputedPropertyRead_AcceptsOwnedPropertyOpcodes|FullyQualifiedName~Evaluate_ComputedPropertyReadOutsideFirstBoundary_DeclinesWithBoundaryCode"`; literal-base lane: `rtk dotnet test tests/Asynkron.JsEngine.Tests --filter "FullyQualifiedName~UnifiedBytecodeLiteralBasePropertyReadTests"` |
| `PropertyWriteDependency` | Property writes and compound/logical property writes outside the admitted direct property-write shapes, supported computed expression-key mutation shapes (including rich binary/unary computed keys such as `box.child[key + 1] = value`, and whole-span control-expression computed keys such as the ternary `box.child[cond ? a : b] = value`, logical-and `obj[a && b] = value`, and nullish-coalesce `obj[a ?? b] = value` — admitted via the shared `IsSupportedComputedPropertyKeySpan`/`TryAppendComputedPropertyKeySpan` control-expression delegation; control-expression keys whose branches need slot-layout/call-target threading, e.g. a member-call branch, remain declined), simple-return dynamic-base named/computed property writes and compound/logical writes, simple nested named receiver assignment shape, nested named receiver plus computed-write shape (`box.child[key] = value`), computed-read prefix plus named-write shape (`box[key].child = value`), computed-read prefix plus computed-write shape (`box[k1].child[k2] = value`), nested named compound-write shape, nested named logical-write shape, nested named receiver plus computed compound-write shape (`box.child[key] += value`), nested named receiver plus computed logical-write shape (`box.child[key] &&= value`, `||=`, `??=`), and computed-read prefix plus computed compound/logical-write shapes (`box[k1].child[k2] += value`, `&&=`, `||=`, `??=`) through `TryIsFirstBoundaryComputedPrefixComputedPropertyWriteCandidate` plus the shared measured computed-read prefix and existing compound/logical suffix emitters; unsafe optional/private/call-adjacent prefixes, unsupported key/RHS spans, and unproven multi-computed receiver prefixes remain declined. Deep PURE write chains whose base is a simple object/array literal and whose terminal store is NAMED are now admitted (A19, `TryIsFirstBoundaryLiteralBasePropertyWriteChainCandidate`, sync route only) — e.g. `({a:{b:0}}).a.b = v`, `({a:{b:{}}}).a.b.c = v`, `[box][0].a = v`, validated by a single stack-discipline walk (the WRITE twin of the A17/A18 read walker) over the literal-construction + named/computed read vocabulary ending in exactly one `SetNamedProperty`; any call/compound/logical/chained-assignment/optional/short-circuit/private/method/accessor hop declines, so a setter in the chain runs exactly once. The `SetComputedProperty` terminal off a literal base (`({a:{}}).a['b'] = v`) stays declined — the compiler cannot lower it off a literal base (`Unsupported computed property key span`) and admitting it is compiler foundation work | Existing sync IR property-write route | Property write widening lane | `rtk dotnet test tests/Asynkron.JsEngine.Tests --filter "FullyQualifiedName~UnifiedBytecodeProductionEligibilityTests&FullyQualifiedName~Evaluate_LogicalAndAssignment_UnsupportedShapes_DeclineWithExplicitCodes"`; computed-prefix write lane: `rtk dotnet test tests/Asynkron.JsEngine.Tests --filter "FullyQualifiedName~UnifiedBytecodeProductionEligibilityTests&(FullyQualifiedName~Evaluate_ComputedPrefixComputedPropertyWrite_AcceptsOwnedPropertyOpcodes|FullyQualifiedName~Evaluate_ComputedPrefixComputedCompoundPropertyWrite_AcceptsOwnedPropertyOpcodes|FullyQualifiedName~Evaluate_ComputedPrefixComputedLogicalPropertyWrite_AcceptsOwnedPropertyOpcodes)"`; literal-base write lane: `rtk dotnet test tests/Asynkron.JsEngine.Tests --filter "FullyQualifiedName~UnifiedBytecodeLiteralBasePropertyWriteTests"` |
| `PropertyUpdateDependency` | Property and identifier update expressions outside the admitted direct update, computed expression-key update, simple-return dynamic-base named/computed property update, simple nested named receiver update boundary, and nested named receiver plus computed-update shape (`box.child[key]++`, `++box.child[key]`, `box.child[key]--`, `--box.child[key]`, including deep prefixes such as `box.child.branch[key]++`, rich binary keys such as `box.child[key + 1]++`, and whole-span control-expression keys such as `box.child[cond ? a : b]++`), and computed receiver-prefix update shapes (A23, `TryIsFirstBoundaryComputedPrefixPropertyUpdateCandidate` + `TryAppendFirstBoundaryComputedPrefixComputedPropertyUpdate`, sync route only) — the computed-update terminal `box[k1].child[k2]++` / `--box[k1].child[k2]` (the prefix is resolved once via the shared computed-read span emitter, then the trailing computed key span drives `UpdateComputedProperty`) and the named-update terminal `box[k1].child++` (owned by the generic `UpdateNamedProperty` per-op path); a compound terminal (`box[k1].child[k2] += v`) lowers to a write pair and stays `PropertyWriteDependency`, and a call inside the prefix declines `CallDependency`) | Existing sync IR update route | Property update lane | `rtk dotnet test tests/Asynkron.JsEngine.Tests --filter "FullyQualifiedName~UnifiedBytecodeProductionEligibilityTests&FullyQualifiedName~Evaluate_NestedNamedComputedPropertyUpdate_AcceptsOwnedPropertyOpcodes"` |
| `DeleteDependency` | `delete` expressions outside the admitted ordinary named/computed property delete lane, simple-return dynamic-base named/computed property delete lane, ordinary dynamic-key computed delete lane, bounded implicit-`arguments` identifier delete lane, and with-backed dynamic-name delete lane | Existing sync IR delete route | Delete semantics lane | `rtk dotnet test tests/Asynkron.JsEngine.Tests --filter "FullyQualifiedName~UnifiedBytecodeProductionEligibilityTests&FullyQualifiedName~Evaluate_NestedComputedPropertyDeleteDynamicKey_AcceptsOrdinaryDynamicNameOpcode"` |
| `SuperPropertyDependency` | Out-of-boundary super call targets; super property reads/writes/updates are admitted by dedicated VM opcodes | Existing class / constructor route for remaining call-target shapes | Super semantics lane | `rtk dotnet test tests/Asynkron.JsEngine.Tests --filter "FullyQualifiedName~UnifiedBytecodeProductionEligibilityTests&FullyQualifiedName~Evaluate_SuperPropertyAccess_AcceptsOwnedOpcodes"` |
| `OptionalChainDependency` | Optional chains outside the admitted optional property-read, optional-call, optional-chain call-argument, and optional named/computed delete boundaries. Current sync delete coverage includes terminal optional named deletes (`delete box?.value?.leaf`), non-terminal optional named deletes (`delete box?.value.leaf`), terminal optional computed deletes (`delete box?.[key]`, `delete box?.child?.[key]`), and optional computed-read receiver plus terminal computed delete (`delete box?.[k1][k2]`). Resumable-only optional-computed-hop calls (`yield o?.[k]()` in a generator) remain documented plan-walk declines | Existing sync IR optional-chain route | Optional-chain widening lane | `rtk dotnet test tests/Asynkron.JsEngine.Tests --filter "FullyQualifiedName~UnifiedBytecodeProductionEligibilityTests&FullyQualifiedName~Evaluate_OptionalChainNamedPrefixPlainCallExpressionPlan_AcceptsExecutableInvocationBoundary"` |
| `ObjectLiteralOrSpreadDependency` | Object/array spread sources outside simple operands, direct named/computed member-call spans, receiver/callee-optional named/computed member-call spans, identifier-call and named-property-of-call array-spread sources (A33: `[...f()]`, `[...gen()]`, `[...f().items]`, `[a, ...f(), c]`), identifier-call and named-property-of-call OBJECT-spread sources (A34: `{...f()}`, `{...gen()}`, `{...f().inner}`, `{a, ...f(), c:3}` — the shared `TryMeasureSimpleSpreadSourceOperandSpan` source measurer, consulted in the object-literal gate only when the property terminates in `ObjectSpread`; getters fire in source order, non-enumerables are skipped, null/undefined is a no-op), and simple-control-expression spans in spread sources, computed object keys, or object property values over activation-slot or admitted dynamic receiver/key/argument operands, and object methods/accessors only when they appear inside restricted simple literal spans (A35: a property VALUE that is a bare-identifier call `{x: g()}`/`{a: g(arg)}` via `TryMeasureSimpleDirectIdentifierCallOperandSpan`, mirroring the already-admitted member-call value `{a: o.m()}`; and a shorthand-method/accessor member followed by a LATER member — most importantly a trailing object-spread `{m(){}, ...o}`/`{get a(){}, ...o}` — via `TryMeasureSimpleObjectLiteralMethodOrAccessorMemberSpan`, which keeps the literal-span measurement from terminating at the method/accessor define; computed key + value subexpressions evaluate left-to-right, preserving PropertyDefinitionEvaluation order) | Existing sync IR literal/spread route for remaining spans | Literal/spread lane | `rtk dotnet test tests/Asynkron.JsEngine.Tests --filter "FullyQualifiedName~UnifiedBytecodeProductionEligibilityTests&FullyQualifiedName~Evaluate_ObjectLiteralUnsupportedFeatures_DeclineWithExplicitCodes"` |
| `PrivateFieldDependency` | Private-name operations outside the admitted routes; `#name in obj`, direct private named reads/writes/updates, direct private named compound/logical writes, and direct private named method calls are VM-owned when the surrounding class method is otherwise production-eligible. Private member deletes are parser early errors before production eligibility. | Existing private-name route for remaining private member access | Private-name lane | `rtk dotnet test tests/Asynkron.JsEngine.Tests --filter "FullyQualifiedName~UnifiedBytecodeProductionEligibilityTests&FullyQualifiedName~Evaluate_PrivateFieldIn_AcceptsAndVmChecksPrivateBrand"` |
| `ForInDriverStateDependency` | Unsupported for-in driver state outside the lowered synchronous source and resumable awaited-source lanes | Existing for-in IR driver route | Driver-state lane | `rtk dotnet test tests/Asynkron.JsEngine.Tests --filter "FullyQualifiedName~UnifiedBytecodeProductionEligibilityTests&FullyQualifiedName~EvaluateResumable_AwaitedForInSource_AdmitsAwaitValueAndForInDriver"` |
| `DestructuringDependency` | Awaited binding values, unsupported destructuring driver shapes, disposal-aware binding targets, and targets outside the admitted driver or descriptor-backed lanes; top-level script `var` / `let` / `const` simple destructuring targets are admitted through driver descriptors, and declaration defaults plus computed binding names route through `ApplyDeclarationBindingTarget` | Existing destructuring IR route for remaining shapes | Destructuring driver lane | `rtk dotnet test tests/Asynkron.JsEngine.Tests --filter "FullyQualifiedName~UnifiedBytecodeProductionEligibilityTests&FullyQualifiedName~Evaluate_DeclarationDestructuringDescriptorShapes_AcceptDescriptorOpcode"` |
| `UnsupportedPlanShape` | Missing activation slot metadata, unsupported instruction families, unsupported compiler shapes, unsupported resumable opcodes, and unknown production opcode defaults. A49 (`Activation slot metadata is required.`): unreachable for any valid compiled function/script plan — `ExecutionPlanBuilder` always populates a non-null `ActivationSlotShape` (and the nested-function restamp carries it forward), so this arm is a pure defensive backstop; trivial plans (top-level expression script, empty `function f(){}`) already route. A51a (`Unsupported entrypoint.`): likewise unreachable for valid compiled function/script/resumable plans because `ExecutionPlanBuilder` records an in-range `EntryPoint`; the compiler, production eligibility, and with-depth analysis keep the check as malformed-plan protection only. A41 is now admitted for explicit slot-resolved CONSUMED identifier assignments: `return (x = 5);`, `var y = (x = 5);`, `g(x = 5)`, and chained `(x = z = 5)` lower `StoreResolvedIdentifier` to `DuplicateTop` + `StoreSlot`, preserving the assignment value and writing the flat slot. The remaining identifier-reference slot boundary is the name-only activation-slot match (`...resolves only by activation-slot name lookup...`), kept declined because dynamic/`with` shadowing cannot be proven from name lookup alone. | Existing execution-plan route | Statement/control-flow ownership lane | `rtk dotnet test tests/Asynkron.JsEngine.Tests --filter "FullyQualifiedName~ExpressionProgramCoverageMapTests&FullyQualifiedName~UnifiedBytecodeExpansionContract_ListsRequiredHeadingsAndCurrentEnums"` |
| `CallInvocationBoundary` | Plan-structural call invocation outside the currently executable call boundary, separate from descriptor-level `CallDependency` | Existing sync IR call route | Wider call invocation lane | `rtk dotnet test tests/Asynkron.JsEngine.Tests --filter "FullyQualifiedName~ExpressionProgramCoverageMapTests&FullyQualifiedName~UnifiedBytecodeExpansionContract_ListsRequiredHeadingsAndCurrentEnums"` |

### Sync Production Pre-Gate Rows

These rows are owned by
`TypedAstEvaluator.SyncFunctionInvoker.CanUseProductionUnifiedBytecodeFastPath`.
They decline before `UnifiedBytecodeProductionEligibility.Evaluate`, so they do
not always have a `UnifiedBytecodeProductionDeclineCode`.

| Pre-gate key | Owning source / current example | Current fallback route | Planned batch / lane | Proof command |
|---|---|---|---|---|
| `pre-gate:IsClassConstructor` | Class constructor invokers outside the admitted base constructor routes with simple identifier parameters, simple literal-default parameters, or final-rest identifier parameters, and outside the explicit derived-constructor `super(...)` routes with the same admitted parameter variants and post-super `this` body reads/writes. Private-name scopes or captured private-name scopes keep both base and derived constructor routes on the existing constructor/class route even when the body itself has no private op. | Constructor route | Constructor boundary lane | `rtk dotnet test tests/Asynkron.JsEngine.Tests --filter "FullyQualifiedName~UnifiedBytecodeProductionInvocationTests"` |
| `pre-gate:IsArrowFunction` | Arrow functions outside the admitted simple-return route whose lowered body proves no super, unadmitted call-target, nested function/class literal, or other unowned dependency. Simple literal-default identifier parameters, including defaults folded to literals before invocation, and final-rest identifier parameters are admitted on the same VM slot-initialization path as ordinary functions. Captured lexical `this` / `new.target`, statically resolved flat-slot reads, ordinary environment identifier reads/calls, captured identifier assignment/compound assignment/update/delete, and named/computed property reads, simple property writes, compound/logical property writes, property updates, or property deletes from those dynamic identifier bases are VM-owned; lexical-this environments remain a separate pre-gate. | Existing arrow invocation route | Arrow route lane | `rtk dotnet test tests/Asynkron.JsEngine.Tests --filter "FullyQualifiedName~UnifiedBytecodeProductionInvocationTests"` |
| `pre-gate:IsAsyncLike` | Async and formerly-async ordinary invokers | Async route | Resumable async/generator lane | `rtk dotnet test tests/Asynkron.JsEngine.Tests --filter "FullyQualifiedName~UnifiedBytecodeProductionEligibilityTests&FullyQualifiedName~Evaluate_AsyncLikeActivation_DeclinesBeforePlanInspection"` |
| `pre-gate:IsGenerator` | Generator functions before ordinary sync production routing | Generator route | Resumable async/generator lane | `rtk dotnet test tests/Asynkron.JsEngine.Tests --filter "FullyQualifiedName~UnifiedBytecodeProductionEligibilityTests&FullyQualifiedName~Evaluate_GeneratorActivation_DeclinesBeforePlanInspection"` |
| `pre-gate:hasParameterExpressions` | Runtime-dependent default parameter expressions outside the admitted simple literal-default identifier route, where literal includes defaults folded to literals before invocation, including its bounded implicit-arguments read/property-read/update/assignment/delete/call-target variant and admitted base/derived-constructor variants, plus destructured parameter expressions; final rest identifier parameters are admitted when the surrounding activation is otherwise production-owned | Existing parameter environment route | Parameter semantics lane | `rtk dotnet test tests/Asynkron.JsEngine.Tests --filter "FullyQualifiedName~UnifiedBytecodeProductionInvocationTests"` |
| `pre-gate:hasOnlySimpleIdentifierParameters` | Non-simple parameter lists outside the admitted final-rest identifier route, including its bounded implicit-arguments read/property-read/update/assignment/delete/call-target variant for ordinary functions and admitted base/derived-constructor variants, and simple literal-default identifier routes | Existing parameter environment route | Parameter semantics lane | `rtk dotnet test tests/Asynkron.JsEngine.Tests --filter "FullyQualifiedName~UnifiedBytecodeProductionInvocationTests"` |
| `pre-gate:boundedImplicitArgumentsDependency` | Functions whose plan-level expression programs contain only bounded implicit `arguments` object dependencies; this now covers reads, property reads such as `arguments.length` and `arguments[0]`, expression-result assignments, assignment references inside complex call arguments, updates, deletes, call targets, and `typeof`. Simple literal-default parameters and ordinary final-rest parameters are admitted when the same implicit-arguments predicate owns the body; shadowing parameter/lexical `arguments` reads, `typeof`, assignments, updates, deletes, and calls are activation-slot traffic | Existing arguments-object route | Arguments object lane | `rtk dotnet test tests/Asynkron.JsEngine.Tests --filter "FullyQualifiedName~UnifiedBytecodeProductionEligibilityTests&FullyQualifiedName~Evaluate_CallArgumentImplicitArgumentsAssignment"` |
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
  reads before the optional jump-to-chain-end boundary. A28: a standalone
  whole-program optional NAMED read chain may also carry MULTIPLE optional hops
  (`a?.b?.c`, `a?.b?.c?.d`, deeper, and prefixed tails such as `a.x?.b?.c` /
  `a.b.c?.d?.e`) — `TryIsFirstBoundaryOptionalNamedChainCandidate` validates the
  whole program (activation-resolved base, an optional run of plain named prefix
  reads, the first optional `?.` hop, then one-or-more short-circuiting named
  hops) and `TryAppendFirstBoundaryOptionalNamedPropertyReadChain` emits ONE
  `JumpIfNullishReplaceUndefined` boundary per optional hop, every jump
  backpatched to the same chain end so a nullish value at ANY hop short-circuits
  the remaining chain to `undefined` while a real-undefined intermediate read by
  a following NON-optional hop (`a?.b?.c.d`) still throws `TypeError`. This
	  multi-hop named read admission also covers call-argument named-chain spans
	  such as `fn(box?.value?.nested)` and `fn(box?.child?.value)`. A31 extends
	  the same call-argument ownership to computed optional continuation spans
	  such as `fn(box?.[key]?.[key])`, `fn(box?.[key]?.value)`, and
	  `fn(box?.prop?.[key])`. A29: the
  COMPUTED analog of the A28 chain —
  a standalone whole-program optional COMPUTED read may carry MULTIPLE optional
  computed hops (`a?.[k]?.[j]`, deeper `a?.[k]?.[j]?.[m]`, named-prefixed
  `a.x?.[k]?.[j]`, and a trailing short-circuiting named tail `a?.[k]?.[j].c`).
  `TryIsFirstBoundaryOptionalComputedPropertyReadChainCandidate` validates the
  whole program (activation-resolved base, an optional run of plain named prefix
  reads, then one-or-more optional computed hops, each
  `JumpIfNullish(ReplaceWithUndefined:true)` + key-span +
  `GetComputedProperty` — the first hop's read is the chain's first boundary with
  `!ShortCircuitOnNullishTarget` and every subsequent hop's read short-circuits;
  the next `GetComputedProperty` delimits each hop's key span and each source
  boundary jump targets the op right after its own `GetComputedProperty`).
  `TryAppendFirstBoundaryOptionalComputedPropertyReadChain` emits ONE
  `JumpIfNullishReplaceUndefined` per hop, every jump backpatched to the same
  chain end, so a nullish value at ANY hop short-circuits the remaining chain to
  `undefined` WITHOUT evaluating later hops' keys, while a real-undefined
  intermediate read by a following NON-optional hop (`a?.[k]?.[j].c`) still throws
  `TypeError`. The short-circuiting-`GetComputedProperty` eligibility case calls
  the same candidate; the admitted opcodes already have sync VM handlers and the
  resumable allowlist (`TryFindUnsupportedResumableOpcode`) is untouched. Nested named receiver
  chains ending in a simple
  computed delete (`delete box.child[key]`, `delete box.child[left + right]`)
  are admitted by `TryIsFirstBoundaryComputedPropertyDeleteCandidate`. A25/A26
  widening (sync route only): DEEP delete chains with a plain COMPUTED read hop
  anywhere before the terminal delete — `delete box[k1][k2]`,
  `delete box.a[k1][k2]`, and `delete box[k1].b` — are now admitted by the
  whole-program walker `TryIsFirstBoundaryDeepPropertyDeleteChainCandidate`. It
  validates the entire program with a single stack-discipline pass (mirroring the
  property-read/write deep-chain walkers) over an activation-resolved (or
  admitted plain dynamic-identifier) base, any mix of plain named reads
  (`GetNamedProperty`) and plain computed reads
  (`key…, RequireObjectCoercible(Depth: 1), ResolvePropertyKey,
  GetComputedProperty`), and a terminal `DeleteNamedProperty` /
  `DeleteComputedProperty`; it requires at least one computed read hop (the
  pure-named deep chain `delete box.a.b.c` is already covered by
  `TryIsFirstBoundaryNamedPropertyDeleteCandidate`). Any optional/short-circuit
  hop or private name in the chain declines, keeping those on their dedicated
  candidates / IR runner. The walker is wired into the `GetNamedProperty`,
  `GetComputedProperty`, `DeleteNamedProperty`, and `DeleteComputedProperty`
  per-op cases after the existing candidates, so it fires only for shapes those
  reject. All admitted opcodes already have sync VM handlers; the resumable
  allowlist (`TryFindUnsupportedResumableOpcode`) is untouched. Optional
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
- Accepted block scopes are slot-layout-owned lexical scopes with flat slot
  mappings. Destructuring-free `let` / `const` declarations compile through
  `SimpleVariableDeclaration` into active-scope flat slots. Captured
  per-iteration loop bindings are admitted only when the compiler can carry the
  per-iteration copy list as descriptor metadata for the mapped slots.
- `UnifiedBytecodeProgram.SlotCount` covers root activation slots and admitted
  block-scope flat slots. Parameter slot metadata preserves formal positions,
  using `-1` for unused or unmapped formals so invocation does not shift later
  arguments.
- `PushEnvironment` initializes ordinary admitted lexical flat slots to
  `JsValue.Uninitialized`; for copy-listed per-iteration slots it snapshots the
  previous value before rebinding and writes that value into the current flat
  slot instead of TDZ-wiping it. In the sync VM, `PushEnvironment` also creates a
  scope environment when materialized bindings are needed. In the resumable VM,
  the newly admitted subset is deliberately flat-slot-only; materialized block
  environments across suspension still decline before VM entry. `PopEnvironment`
  is an owned VM cleanup opcode for the admitted linear shape and disposes
  registered sync `using` resources before restoring the enclosing environment
  on the sync route. Neither opcode uses name fallback or calls back into
  `ExpressionProgram`, `ExecutionPlanRunner`, or AST evaluation.
- Unsupported binding declaration shapes, unsupported object/array destructuring
  driver instructions, unsupported dynamic lookup, `await using`, block-scoped
  resumable `using`, resumable materialized block environments, and scope-entry
  shapes without flat mappings remain pre-VM declines.

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
  declaration-free literal eval-identifier call boundary, and the non-with
  dynamic-name lane is limited to functions without captured dynamic activation
  or live with-chain closure state. The admitted captured-closure route covers
  sync and resumable flat, nested, and colliding captured locals; it does not
  make dynamic activation globally admissible. Declaration-free literal eval
  may read the caller's current activation and bounded implicit `arguments`
  object; arguments-object dependencies outside that owned route, captured
  dynamic activation, with-chain closure residue, and async/generator dynamic
  activation functions still decline before VM execution.

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
  without leaving the VM), optional-start named read chain spans
  (`box?.child.value`, `box?.child.nested.deep`, `box?.value?.nested`,
  `box?.child?.value`, emitted as one proven
	  `JumpIfNullishReplaceUndefined(end)` per optional hop plus owned
	  `GetNamedProperty` continuation reads so a nullish value at any optional named
	  hop short-circuits the whole chain to `undefined`), optional-computed read
	  chain spans (`box?.[key].value`, `box?.[key]?.value`,
	  `box?.[key]?.[key]`, emitted as one boundary jump per optional computed or
	  named continuation hop plus owned `GetComputedProperty`/`GetNamedProperty`
	  reads), optional-named-then-computed read chain spans (`box?.prop[key]`,
	  `box?.prop[a + b]`, `box?.a.b[key]`, `box?.prop?.[key]`, emitted as the
	  proven `JumpIfNullishReplaceUndefined(end)` head jump at the optional named hop,
	  an additional boundary jump for `?.[` continuations, the named
	  hop/continuation reads, the computed key span, `GetComputedProperty`, and
	  trailing `GetNamedProperty` reads so a nullish receiver at any optional hop
	  short-circuits the whole chain to `undefined`), or simple
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
- A33: array-literal spread entries additionally admit a NON-simple spread
  *source* — a bare activation-resolved identifier call (`[...f()]`, `[...gen()]`)
  and a plain named property read off such a call (`[...f().items]`,
  `[...f().a.b]`) — including when mixed with normal elements (`[a, ...f(), c]`).
  This widening is scoped strictly to spread sources: the wider source span is
  consulted only when the element terminates in `ArraySpread`, so an ordinary
  push element (`[f()]`) and a computed read off a call (`[...f()[0]]`) stay
  declined. The `ArraySpread` opcode drives the standard iterator protocol over
  the produced value, throwing a `TypeError` on a non-iterable source and
  propagating any throw raised while producing or iterating it. Member-call
  sources (`[...o.m()]`) were already admitted through the simple-literal-value
  span; A33 closes the identifier-call and call-property-read gaps by extending
  the same simple-array-literal-span escape hatch (already carried by the member
  call-target and `Call` cases) to the `LoadIdentifierCallTarget` and plain
  `GetNamedProperty` cases.
- A34: object-literal spread entries additionally admit a NON-simple spread
  *source* — a bare activation-resolved identifier call (`{...f()}`, `{...gen()}`),
  an admitted member call (`{...o.make()}`), and a plain named property read off
  such a call (`{...f().inner}`, `{...f().a.b}`) — including when mixed with normal
  properties (`{a, ...f(), c: 3}`). The A33 source measurer is renamed
  `TryMeasureSimpleSpreadSourceOperandSpan` and SHARED: it is consulted in the
  object-literal gate only when the property terminates in `ObjectSpread`, so an
  ordinary property value (`{a: f()}`) and a computed read off a call
  (`{...f()[0]}`) stay declined. The `ObjectSpread` opcode copies the source's own
  ENUMERABLE properties — getters fire in source order, non-enumerable properties
  are skipped, and a null/undefined source is a no-op — and propagates any throw
  raised while producing or reading the source, so the source span (built from
  already-admitted VM opcodes) needs no new VM handler. Member-call sources
  (`{...o.m()}`) were already admitted; A34 closes the identifier-call and
  call-property-read gaps by extending the simple-OBJECT-literal-span escape hatch
  to the `LoadIdentifierCallTarget` and plain `GetNamedProperty` cases (the
  `LoadNamedCallTarget` and `Call` cases already carried it).
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
- A12: MEMBER/COMPUTED call-targets past the first invocation boundary —
  chained method/computed calls (`a.b().c()`, `o.m().n()`, `o.a()[k]()`) whose
  final call's TARGET is a member access on an EARLIER call's result — are now
  admitted. The named-member-call eligibility candidate
  (`TryIsGeneralNamedMemberCallExpressionCandidate`) walks the whole program with
  the canonical admitted-argument operand-stack model
  (`TryApplyAdmittedArgumentOpStackDelta`), so it admits named and computed
  receiver chains that contain an inner `Call` boundary in addition to the flat
  member-call shape. The compiler detects these chains
  (`IsGeneralChainedCallProgram`: last op is `Call`, an earlier op is also a
  `Call`, and there are NO genuine optional-chain ops) and declines the
  specialized first-boundary receiver-chain helper cleanly so the general
  expression loop lowers the entire chain in source order: each inner
  `CallInvocationBoundary` leaves its result on the operand stack as the next
  call's receiver, and the final call applies with `this` = that immediate
  receiver object. Left-to-right evaluation of the chain is preserved exactly.
  STRICT self-recursive FLAT member tail calls (`this.step(n-1)`) are NOT a
  chained shape, so A12 does not alter their routing — they recurse on the native
  stack exactly as on baseline (no TCO, identical depth ceiling).
- Ordinary non-optional identifier/member/super call-target opcodes now lower
  through the general expression loop. Direct eval and optional-call shapes
  still use the first-boundary call-target helper because they carry
  boundary-local directness metadata or packed nullish short-circuit jump
  targets.
- Accepted optional call programs either use a jump-owned optional-start prefix
  followed by an ordinary call-target preparation opcode (`a?.b.c(args)`,
  `a.x?.b.c(args)`, `a?.b[k](args)`, and — A30, sync route only —
  `o?.[k](args)` and `a?.b?.[k](args)`) or use
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
- A30 (sync route only): optional-computed-START member calls extend the
  jump-owned optional-start prefix family beyond the optional-NAMED start cases
  above. `o?.[k](args)` (`TryIsFirstBoundaryOptionalComputedStartPlainCallCandidate`
  / `TryAppendOptionalComputedStartPlainCallTarget`) and the double-optional
  `a?.b?.[k](args)` (`TryIsFirstBoundaryOptionalChainComputedReceiverOptionalCallCandidate`
  / `TryAppendOptionalChainComputedReceiverOptionalCallTarget`) are the
  computed-key twins of the already-admitted `a?.b[k](args)` (Case 6) and
  `a?.b?.c(args)` (Case 5). Each optional hop lowers to a
  `JumpIfNullishReplaceUndefined` backpatched to the same chain end, so a nullish
  receiver at ANY hop short-circuits the WHOLE call to `undefined` — the call is
  never made and the computed key is never evaluated past the short-circuit. The
  candidate predicates fully validate the rigid op layout (activation-resolved
  base, the leading `JumpIfNullish(ReplaceWithUndefined)` / optional named hop +
  `JumpIfShortCircuited`, a simple computed key, a non-optional
  `LoadComputedCallTarget`, and simple call arguments) before any operand is
  emitted; all emitted opcodes already have sync VM handlers. This widening is
  SYNC-ONLY: it is gated by the `allowSyncOnlyOptionalComputedStartCalls` flag on
  `TryFindExpressionDecline` (set only on the sync `TryFindPlanDecline` walk), so
  the resumable route still declines these shapes as `OptionalChainDependency` at
  the plan walk and the resumable opcode allowlist
  (`TryFindUnsupportedResumableOpcode`) is untouched. Remaining optional-computed
  call shapes — the callee-optional `o?.[k]?.()` and spread-onto-optional
  (`a?.b?.(...args)`) — still decline.
- Direct eval programs use `PrepareIdentifierCallTarget` followed by
  `CallInvocationBoundary` with the direct-eval operand flag. The admitted shape
  is exactly one non-spread eval-identifier argument; the VM invokes the eval host
  with the caller environment and syncs mutated caller slots back into unified
  bytecode-owned slots before execution continues.
- Private named method calls route both as the primary call expression and as a
  nested complex call argument through `PrepareNamedCallTarget` plus
  `CallInvocationBoundary`, preserving receiver/private-brand semantics through
  the class method's threaded private-name scopes. Private-adjacent member
  targets outside those admitted named method-call shapes, arguments-object
  dependencies, unsupported non-with dynamic lookup,
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
- Accepted iterator driver shapes include `IteratorInit`, `IteratorMoveNext`,
  and `IteratorClose` when the iterator kind is `Sync` or `Await` and the
  iterable source is lowered to exactly one expression bytecode payload. The
  `Await` kind covers `for await...of`: `ExecuteResumable` owns async iterator
  `next`/value awaits and close settlement. The accepted sync lane also includes
  resumable async `for (x of await p)` sources, which compile the awaited source
  payload followed by `AwaitValue` and `IteratorInit`. Slice A
  (#2678) also admits a TDZ head environment (`for (const x of …)` /
  `for (let x of …)`): the compiler emits a
  `TdzHeadInit` instruction that resolves the head bindings to flat slots, and
  the VM marks them `JsValue.Uninitialized` before the source is evaluated so a
  read of the head binding inside the source throws a `ReferenceError`.
- Accepted for-in driver shapes include `ForInInit` and `ForInMoveNext` when
  the object source is lowered to synchronous expression bytecode or an awaited
  source payload followed by `AwaitValue`. That covers the resumable
  `for (k in await p)` lane. Slice A (#2678) also admits a TDZ head environment
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
  descriptor-backed assignment lane, destructuring async-source/awaited-source
  driver shapes, dynamic-name shapes, and targets that cannot resolve to
  unified slots still decline before VM execution. Sync-driver TDZ head
  environments are now admitted (Slice A, #2678); destructuring async-kind and
  awaited-source TDZ heads remain declined.

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
  persist short-circuit flags across suspension. (`TypeOfDynamicIdentifier` is now
  admitted — see the free-`typeof` entry below; property writes/updates/deletes are
  now admitted — see the property entries below.)
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
  that must NOT inherit a prior chain's short-circuit. `super`/construct optional
  boundaries remain declined as before.
- Accepted resumable bodies may now dispatch the two remaining OPTIONAL CALL
  shapes that REACH the opcode path (B15): the computed-member optional call
  `o[k]?.()` (`PrepareComputedOptionalCallTarget`) and the free/dynamic-identifier
  optional call `freeFn?.()` (`PrepareDynamicIdentifierOptionalCallTarget`). Both
  opcodes are removed from `TryFindUnsupportedResumableOpcode` and given
  `ExecuteResumable` handlers that are literal twins of the sync VM's:
  `o[k]?.()` pops the key, loads the method via `GetComputedCallTargetValue`, and
  short-circuits to `undefined` via the chain-end jump if the method is nullish;
  `freeFn?.()` resolves the callee by name via `PrepareDynamicIdentifierCallTarget`
  against the live `UnifiedBytecodeResumeState.CallingEnvironment` (#3108, stable
  across suspension), pushes the `<thisValue, callee>` pair, and short-circuits the
  whole call to `undefined` via the chain-end jump when the resolved callee is
  nullish. The short-circuit is jump-owned (the replaced `undefined` is the final
  call result), so it survives `yield`/`await` because the program counter and the
  operand stack restore from the resume state; dynamic resolution is live, so a
  resumed step observes any reassignment of the free binding made by outer code
  between suspensions. The OPTIONAL-COMPUTED call `o?.[k]()` (a LEADING optional
  hop on the receiver, lowered with a `JumpIfNullish`) is a DIFFERENT shape: the
  shared production-plan walk (`TryFindResumablePlanDecline`) declines it as
  `OptionalChainDependency` ("short-circuiting outside the first production
  property-read boundary") BEFORE the opcode allowlist, so it never reaches the
  resumable path — admitting `PrepareComputedOptionalCallTarget` here does not
  change that. Proofs (`UnifiedBytecodeResumableOptionalCallTests`) cover the gate
  (admits both newly admitted opcodes), end-to-end generator + async fast-path
  routing, a nullish short-circuit that STRADDLES a `yield`, a free-binding
  reassignment observed across a `yield`, a throwing callee surfacing on the
  resumed step (only a NULLISH callee short-circuits), and a pinned `o?.[k]()`
  plan-walk decline.
- Accepted resumable bodies may now query `typeof` of a FREE/DYNAMIC IDENTIFIER
  between suspension points (B25): `typeof freeVar` where `freeVar` is a
  module/script-level binding, a captured outer binding, or an UNBOUND free name,
  lowered to `TypeOfDynamicIdentifier`. The opcode is added to the
  `TryFindUnsupportedResumableOpcode` allowlist and given one `ExecuteResumable`
  handler that is the LITERAL TWIN of the sync `Execute` path: it resolves the name
  against `RequireDynamicEnvironment(state.CallingEnvironment)` — the SAME live
  closure environment (#3108) the admitted free dynamic reads / call targets /
  optional-call targets already use, captured at construction and stable across
  `yield`/`await` — and reuses the static `TypeOfDynamicIdentifier` helper. The
  foundation was already in place (the calling environment is threaded; the
  compiler already lowers a free `typeof` to `TypeOfDynamicIdentifier` under
  `allowsOrdinaryDynamicIdentifiers: true`; the expression-decline walk already
  admits a free `TypeOfIdentifier` operand when `allowsDynamicIdentifiers`), so the
  opcode allowlist was the only gate. The defining invariant: `typeof` NEVER throws
  ReferenceError — the helper swallows the unbound-binding throw and returns
  `"undefined"`, so an UNBOUND free name yields `"undefined"` (no throw, unlike a
  bare READ which would throw) and a BOUND name yields its runtime type; because
  resolution is live, each resumed step observes the CURRENT binding (a retyped or
  late-bound global flips type across the resume). The `ShouldStopEvaluation` guard
  in the handler is purely the defensive non-ReferenceError-throw path (e.g. a
  thrown getter on the global object), surfacing as the resumable Throw step. Free
  dynamic WRITES/updates and `DeleteDynamicIdentifier` stay declined (no resumable
  handler). Proofs (`UnifiedBytecodeResumableTypeOfDynamicTests`) cover the gate
  (admits `TypeOfDynamicIdentifier`), the unbound-no-throw `"undefined"` result and
  a defined type straddling a `yield`, a global RETYPED while suspended (live
  `number|string`), a free name BOUND while suspended (`undefined|function`), a
  generator NESTED IN A CONSTRUCTOR resolving the free/global name through its OWN
  threaded env (not a per-activation value), and the async-across-`await` case.
- Accepted resumable bodies may now resolve FREE/DYNAMIC IDENTIFIERS between
  suspension points: a free variable READ (`yield outerVar`, lowered to
  `LoadDynamicIdentifier`) and a free function CALL target (`yield helper(x)`
  where `helper` is module/script-level or a captured outer binding, lowered to
  `PrepareDynamicIdentifierCallTarget`). `EvaluateResumable` sets
  `allowsDynamicIdentifiers = true` in `TryFindResumablePlanDecline`, compiles
  with `allowsOrdinaryDynamicIdentifiers: true`, and the two opcodes are added to
  the `TryFindUnsupportedResumableOpcode` allowlist. Free plain WRITES
  (`freeGlobal = yield`) now route through the pre-resolved
  `ResolveDynamicIdentifierReference` → `StoreDynamicIdentifierReference` sequence:
  the pending `AssignmentReference` lives on `UnifiedBytecodeResumeState`, so the
  store target selected before the RHS cannot change while suspended. Dynamic
  compound/logical writes, dynamic declaration opcodes, and `eval`/`with`
  remain off the route. The `ExecuteResumable`
  handlers resolve BY NAME against the LIVE closure environment threaded onto
  `UnifiedBytecodeResumeState.CallingEnvironment` (the same field #3108 threads
  for call dispatch), reusing the sync `GetDynamicIdentifierValue` /
  `PrepareDynamicIdentifierCallTarget` helpers. Because the environment is a live
  reference, a resumed step observes the CURRENT binding: a binding a sibling
  closure mutates while the frame is suspended, a global reassigned between
  yields, and a free callee rebound between yields all resolve to the new value,
  and an uninitialized free binding still throws ReferenceError (proof:
  `UnifiedBytecodeResumableDynamicIdentifierTests`). Sync generator
  `yield* <iterable>` now also routes when the iterable resolves through a
  free/dynamic identifier or free call target (`yield* spyIterable`,
  `yield* makeIterator()`). The VM-owned `YieldStar` path now matches the IR
  runner for delegated first-entry `next(undefined)`, original iterator-result
  preservation, and promise-like delegated `throw`/`return` results, so the old
  targeted free/dynamic-yield-star decline guard has been removed (proof:
  `UnifiedBytecodeProductionInvocationTests` yield-star B38 coverage and the
  generator yield-star pack). A binding captured from an *enclosing function*
  scope (resolves to that activation's slot, `ScopeId>=0`) is a distinct tier
  that remains declined.
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
  writes stay declined; free/captured dynamic compound/logical writes are admitted by B29 (proof:
  `UnifiedBytecodeResumablePropertyWriteTests`, including the strict-throws /
  sloppy-ignored adversarial pair, plus `UnifiedBytecodeResumableFreeIdentifierMutationTests`
  for the B26 plain-write route and B29 compound/logical route).
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
- Accepted resumable bodies may now evaluate a PRIVATE-FIELD-IN test (`#x in obj`)
  between suspension points (burndown B18). Two changes are required and BOTH ship
  together. First, the `PrivateFieldIn` opcode is ported into the `ExecuteResumable`
  switch and added to the `TryFindUnsupportedResumableOpcode` allowlist; the handler is
  the literal twin of the sync VM's: it reads the object operand off the top of the
  stack, and if it is not a `JsValueKind.Object` throws the same `TypeError` ("Cannot
  use 'in' operator to search for a private field in a non-object") as the resumable
  Throw step (`state.IsCompleted = true; return UnifiedBytecodeStepResult.Throw(context.FlowValue)`);
  otherwise it replaces the top with `JsValue.True`/`JsValue.False` from the shared
  `HasPrivateField` helper, which resolves the private name via
  `context.ResolvePrivateNameKey` / `context.RealmState` and checks both an own private
  field and a matching private brand. Second — and this is the load-bearing part — the
  body's lexically-active private-name scopes (the class brand scope plus any enclosing
  captured scopes) are threaded onto `UnifiedBytecodeResumeState.PrivateNameScopes`
  (built by `UnifiedBytecodeResumeState.CombinePrivateNameScopes`, enclosing scopes
  first and the own class scope innermost, matching `SyncFunctionInvoker`) and
  re-entered onto the live per-step context at the top of `ExecuteResumable` via
  `context.EnterPrivateNameScopes`. The sync VM gets these scopes for free because the
  regular function-invocation path enters them around the body, but each resumable step
  runs on a FRESH per-step context, so without re-entry `ResolvePrivateNameKey` returns
  null and `HasPrivateField` wrongly reports the field absent. All three resumable
  invokers (sync generator, async function, async generator) populate the scopes, so the
  opcode is correct for every resumable kind. The scopes are a read-only lexical fact of
  the frame, identical on every resume, pushed and popped within the single synchronous
  step traversal so the private-name scope stack stays balanced across yield/await.
  `PrivateFieldIn` carries no `AwaitedProgram`, cannot itself yield/await, and pushes
  exactly one value, so it always runs to completion inside one resumable step. The
  object operand can come from a suspending sub-expression (`#x in (yield o)`); it sits
  on `UnifiedBytecodeResumeState.OperandStack` (the stable backing store) and is
  restored on resume, exactly like every other admitted unary. BOUNDARY: the private
  NAME read/write/update opcodes (`receiver.#value`, `receiver.#value = v`,
  `receiver.#value++`) are a separate slice and keep their existing route; this slice
  admits only the `in`-test opcode (proof: the eligibility admit gate, an end-to-end
  generator method that observes `#value in holder` true (via brand) and `#value in {}`
  false across a yield, the adversarial generator that throws a catchable `TypeError`
  when the `in`-test is resumed against a primitive, and an async method that observes
  the same true/false split across an `await`).
- Accepted resumable bodies may now read the `new.target` meta-property between
  suspension points (burndown B19). The `LoadNewTarget` opcode is ported into the
  `ExecuteResumable` switch and added to the `TryFindUnsupportedResumableOpcode`
  allowlist. The resumable handler is the literal twin of the sync VM's and the
  tier-2 IR runner's: a single chain lookup of `Symbol.NewTarget` against
  `UnifiedBytecodeResumeState.CallingEnvironment` (the closure captured at
  construction and stable across `yield`/`await`), falling back to
  `JsValue.Undefined`. A generator or async function is never a constructor
  (`new g()` throws), so its own function environment binds `new.target` to
  `undefined` and the lookup returns exactly that; because the calling environment
  is stable across suspension, the value observed after a resume matches the value
  at body entry. `LoadNewTarget` carries no `AwaitedProgram`, cannot itself
  yield/await, and pushes exactly one value, so it always runs to completion inside
  one resumable step and never touches the operand stack across a suspension — no
  resume-state restoration is involved. BOUNDARY: async ARROWS that lexically
  inherit `this`/`new.target` are out of scope — they decline at the activation
  gate (`ArrowLexicalThisDependency`) independent of this opcode and keep their
  existing route (proof: `UnifiedBytecodeResumableNewTargetTests`, including the
  generator/async admit gates, the boundary arrow-decline gate, an end-to-end
  `undefined` read across a yield, a `typeof new.target` after a yield, an async
  `new.target` after an await, and the non-regressing async-arrow lexical
  inheritance on its existing route).
- Accepted resumable bodies may now perform the per-hole `String(value)` coercion an
  UNTAGGED template literal emits (`` `v${x}` ``) between suspension points (burndown
  B37). The `ToString` opcode is ported into the `ExecuteResumable` switch and added to
  the `TryFindUnsupportedResumableOpcode` allowlist. The resumable handler is the literal
  twin of the sync VM's, reusing `JsOps.ToJsString`: it replaces the operand-stack top in
  place. The value to coerce sits on `UnifiedBytecodeResumeState.OperandStack` and is
  restored across any suspension in a sibling sub-expression (`` `v${yield 1}` ``), exactly
  like the admitted unaries; the opcode carries no `AwaitedProgram` and cannot itself
  suspend, so it runs to completion inside one resumable step. A throwing coercion (a
  `Symbol`, or a throwing `toString`/`Symbol.toPrimitive`) surfaces as the resumable Throw
  step (`state.IsCompleted = true; return UnifiedBytecodeStepResult.Throw(context.FlowValue)`).
  TAGGED templates (`tag`a${x}b``, burndown B21) are now admitted for ordinary
  identifier/member call targets on both sync and resumable routes. Bare identifier tags
  lower through `LoadIdentifierCallTarget` so the VM call boundary sees the normal
  `<this, callee, args...>` stack contract; `LoadTemplateObject` is modeled as a
  value-producing call argument, emitted by the generic expression compiler, and handled
  by `ExecuteResumable` through the same descriptor/callsite identity cache as the sync
  VM. Super/private/exotic tagged-template call-target variants still remain bounded by
  their existing super/private/call-target gates.
  The B37 name-inference opcode `EnsureHasName` is now admitted for resumable function
  literals: `let f = function(){}` / object method/accessor literals co-emit
  `LoadFunctionLiteral`, and both opcodes are handled directly by `ExecuteResumable`.
  `ThrowReferenceError` is also admitted at the opcode/handler level: the handler
  materializes the ReferenceError value and routes it through resumable
  catch/finally abrupt-completion handling. Its only source producer
  (`delete super[...]`) remains an unsupported delete-super shape, so this is a
  synthetic opcode inventory cleanup rather than a new source-shape admission. An
  uninitialized-binding TDZ read is already surfaced by the existing resumable
  `LoadSlot` handler (no new opcode) (proof:
  `UnifiedBytecodeResumableTemplateToStringTests`, plus
  `UnifiedBytecodeResumableNestedFunctionTests` for `EnsureHasName`, and
  `UnifiedBytecodeResumableValueTierTests` for `ThrowReferenceError`).
- Accepted resumable bodies may now read the `import.meta` meta-property between suspension
  points (burndown B20). The `LoadImportMeta` opcode is ported into the `ExecuteResumable`
  switch and added to the `TryFindUnsupportedResumableOpcode` allowlist. The resumable
  handler resolves the single `Symbol.ImportMeta` binding against the live closure
  environment threaded onto `UnifiedBytecodeResumeState.CallingEnvironment` (the captured
  MODULE environment, stable across `yield`/`await`), so the SAME per-module `import.meta`
  object is observed on every step including after a suspension, satisfying the
  per-module-stability requirement. `import.meta` is ONLY bound in a module environment
  (`EnsureModuleImportMeta`); outside a module the binding is absent and the handler surfaces
  the same `ReferenceError` as the sync VM via the resumable Throw step (the resumable loop
  carries no `ThrowSignal` catch, so the handler resolves the binding directly and calls
  `context.SetThrow` rather than throwing). The opcode pushes exactly one value, carries no
  `AwaitedProgram`, and cannot itself suspend (proof:
  `UnifiedBytecodeResumableImportMetaTests`, including the async/generator admit gates, an
  end-to-end `typeof import.meta` after an await in module context, the SAME-object identity
  across an await, and a generator that observes the stable object across a yield).
- Accepted resumable bodies may now materialize OBJECT and ARRAY LITERALS between
  suspension points (burndown B16/B17): `{a, b: v, [k]: v, ...spread}` and
  `[a, , b, ...spread]`. Twelve opcodes are ported into the `ExecuteResumable`
  switch and added to the `TryFindUnsupportedResumableOpcode` allowlist —
  `CreateObject`, `DefineObjectProperty`, `DefineComputedObjectProperty`,
  `DefineObjectMethod`, `DefineComputedObjectMethod`, `DefineObjectAccessor`,
  `DefineComputedObjectAccessor`, `ObjectSpread`, `CreateArray`, `ArrayPush`,
  `ArrayPushHole`, and `ArraySpread`. Each literal is built bottom-up on the operand
  stack: a single `Create{Object,Array}` pushes a FRESH receiver (a new `JsObject`
  wired to `context.RealmState.ObjectPrototype`, or `new JsArray(context.RealmState)`)
  via the flag-aware `PushResumableValue` helper, then the Define*/ArrayPush*/*Spread
  opcodes mutate it in place, popping their argument(s) while leaving the receiver on
  the stack. A sub-expression can suspend (`{a: yield 1}`, `yield [yield 1]`); the
  partially-built receiver plus any already-evaluated keys/values below the suspension
  sit on `UnifiedBytecodeResumeState.OperandStack` (the stable backing store) and are
  restored on resume, the same mechanism the admitted property writes rely on. The
  handlers reuse the sync VM's `DefineObjectLiteralProperty`,
  `DefineComputedObjectLiteralProperty`, `DefineObjectLiteralMethod`,
  `DefineObjectLiteralAccessor`, and `ApplyObjectLiteralSpread`, plus
  `TypedAstEvaluator.EnumerateSpread`, verbatim, so computed-key `ToPropertyKey`
  coercion, name inference, own-enumerable spread copy with getter order, and
  array-hole semantics are identical; the opcodes that can throw (computed-key
  coercion, a spread getter, a non-iterable array spread) surface the throw as the
  resumable Throw step (`state.IsCompleted = true; return
  UnifiedBytecodeStepResult.Throw(context.FlowValue)`). ECMAScript requires a fresh
  object/array per evaluation, so `Create*` allocates anew on every step (including
  each loop turn across yields) rather than caching. Object METHODS (`{ m(){} }`)
  and GET/SET accessors (`{ get x(){} }`) now route when their function literals are
  non-capturing: the value lowers to `LoadFunctionLiteral`, name inference lowers to
  `EnsureHasName` when needed, and the `Define{Computed}ObjectMethod` /
  `Define{Computed}ObjectAccessor` handlers attach the callable to the fresh object.
  Capturing generator/async/async-generator method/accessor literals can route
  through the B23 materialized-body-environment slice when they capture root body
  locals, and lexical/private-context captures are admitted when the resumable
  invoker proves and materializes the per-call context. Nested declarations inside
  those function literals use the literal's normal invocation-time declaration
  instantiation path and are no longer a B23 decline.
  Separately,
  a `var x = <literal>` whose literal contains no suspension still declines at the
  resumable var-declaration lowering boundary (`SimpleVariableDeclarationInstruction`
  / template-literal span), independent of this opcode work (proof:
  `UnifiedBytecodeResumableLiteralTests`, including the object/computed/spread/array
  admit gates, the method/accessor route gate, end-to-end object/array
  builds across a yield, computed-key + shorthand, an object spread with observed
  getter order and a skipped non-enumerable, array holes + spread, fresh-instance
  identity per loop turn for both object and array, a computed-key coercion throw, a
  non-iterable array-spread throw, and async object/array literals materialized after
  an await).
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
- Async-generator `yield*` now routes after B39 and the awaited-source follow-up
  because the VM owns delegated async iterator `.next(value)`, `.return(value)`,
  and `.throw(value)` settlement through the resumable async-generator
  `PendingAwait` bridge. The awaited delegated source form (`yield* await ...`)
  emits `<awaited source> -> AwaitValue -> YieldStar`, so source-await
  settlement and delegated-result settlement stay separate in the resumable
  state. Unsupported delegated expression payloads remain outside production
  resumable routing until their own lowering is modeled by the VM.

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
  `IteratorCloseInstruction` are eligible for the lowered iterator-driver model
  described above, including sync drivers, async-kind `for await...of` drivers,
  and the resumable awaited-source `for (x of await p)` lane.
- `ForInInitInstruction` and `ForInMoveNextInstruction` are eligible for the
  lowered synchronous for-in driver model and the resumable awaited-source
  `for (k in await p)` lane described above. Unsupported source payload shapes
  still decline with `ForInDriverStateDependency`, while malformed state-slot
  payloads stay source-guarded compiler diagnostics under A51d.
- `ArrayDestructuringInitInstruction`,
  `ArrayDestructuringElementInstruction`,
  `ArrayDestructuringRestInstruction`, and
  `ArrayDestructuringCloseInstruction` are eligible for the lowered
  array destructuring model described above. Targets resolve to a flat
  activation slot when one exists; at **script scope** the step-wise driver
  **state** symbol (`__arrDestr_iter`) is given a synthetic scratch activation
  slot by `AddSyntheticDestructuringStateSlots`. Dynamic script targets store
  by name through the driver descriptor's `TargetNameConstantIndex` and
  `TargetVariableKind`: `var` targets use the ordinary dynamic assignment path,
  while `let` / `const` targets use the VM dynamic lexical initialize path
  against the hoist-time TDZ binding. Other unsupported destructuring shapes
  still decline with `DestructuringDependency` or `UnsupportedPlanShape`. This
  closes the A51e compiler leaf for simple script declaration destructuring; it
  does not admit nested/default/parameter/disposal destructuring.
- `ObjectDestructuringInitInstruction`,
  `ObjectDestructuringPropertyInstruction`,
  `ObjectDestructuringRestInstruction`, and
  `ObjectDestructuringCloseInstruction` are eligible only for the lowered
  direct-slot object destructuring model described above (static keys,
  identifier targets, no defaults, no nested patterns, optional identifier
  rest). At **script scope** the driver **state** symbol (`__objDestr_src`) is
  given a synthetic scratch activation slot, and dynamic `var` / `let` /
  `const` **targets** with no flat slot store by name through the descriptor's
  `TargetNameConstantIndex` plus `TargetVariableKind`. Static nested binding
  declarations with identifier/rest targets are admitted through
  `ApplyDeclarationBindingTarget`. Computed/dynamic-name keys, defaults,
  assignment targets, awaited binding values, and `await using` declarations
  still decline with `DestructuringDependency` or `UnsupportedPlanShape`. ADR
  [`0284`](adrs/0284-keep-unified-bytecode-object-destructuring-model-first-and-static-key-owned.md)
  records the model-first decision and admit/decline boundary.
- `ExpressionOpKind.ApplyBindingTarget` is eligible for ordinary sync
  production assignment destructuring when the expression compiler can lift the
  lowered `BindingTargetProgram` into the unified program descriptor table.
  Generated compiler contexts, including standalone expression compilation, now
  pass that descriptor table through to the general expression loop.
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
   persists short-circuit state across suspension. The computed-member optional
   call `o[k]?.()` and the free/dynamic-identifier optional call `freeFn?.()` are
   now admitted too (B15). Dynamic-identifier `typeof`/`delete`, property
   writes/updates, the OPTIONAL-COMPUTED call `o?.[k]()` (leading hop, declined at
   the plan walk), and `super`/construct boundaries inside resumable bodies remain
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
   `for (k in await p)` lanes; async-kind `for await...of` iterator drivers are
   also admitted. The remaining driver work is residual slot/topology and
   unsupported driver-shape coverage, not the base async iterator kind.
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
   path, the non-injecting direct-eval-backed ordinary dynamic-name path, and
   the explicit with-backed dynamic name slice above. Direct eval outside the
   admitted one-argument non-spread eval-identifier boundary, declaration-bearing
   eval literals that can inject runtime bindings, unresolved dynamic activation,
   and unsupported unresolved lookup shapes still decline before VM execution.
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

## Current Final-Proof Evidence

2026-06-06 local proof run:

- Contract and unified proof packs: `ExpressionProgramCoverageMapTests` 14/14,
  `UnifiedBytecodeProductionEligibilityTests` 586/586,
  `UnifiedBytecodePrototypeTests` 73/73, and
  `UnifiedBytecodeProductionInvocationTests` 557/557 passed. The runner seam
  scan
  `rtk rg "EvaluateExpression\\(|ProfileEvaluateExpression\\(" src/Asynkron.JsEngine/Ast/TypedAstEvaluator.ExecutionPlanRunner*`
  returned no matches.
- Route-hit probes confirmed current production VM entry:
  `forloop` 40, `propertyaccess` 20, `functioncalls-lite` 1,600,002,
  `activation-noargs-lite` 600,002, and `forofiteration` 2,000
  `unified-bytecode-production-fast-path` hits.
- `rtk ./tools/profile forloop --memory` reported `Total allocated 6.93 MB`.
- `rtk ./tools/run-test262-regressions.sh --list` reported
  `full (523 entries)`, `annexb (15 entries)`, `array-prototype (31 entries)`,
  `gh1832-private-accessor-logical-assignment (7 entries)`, `intl (82 entries)`,
  `language (76 entries)`, `proxy (3 entries)`, `regexp (115 entries)`, and
  `temporal (208 entries)`.
- `rtk ./tools/run-test262-regressions.sh` selected 1,910 generated cases:
  1,906 passed and 4 failed. The residual failures were the strict and
  non-strict rows for
  `intl402/DateTimeFormat/prototype/formatRangeToParts/temporal-objects-resolved-time-zone.js`
  and
  `intl402/DateTimeFormat/prototype/formatToParts/temporal-objects-resolved-time-zone.js`.
  These Test262 Intl residuals do not add a discard-specific production
  unified-bytecode decline.
