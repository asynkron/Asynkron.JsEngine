# Unified Bytecode Prototypes

When extending the unified bytecode prototype, keep it IR-owned, internal, and
all-or-nothing until a separate routing issue proves production readiness.

## Rules

1. Use `ExecutionPlan` as the prototype compiler input. Do not create a
   parallel AST-to-unified-bytecode compiler for shapes that the current IR
   already lowers and annotates.
2. Keep eligibility at compile time, including function kind. Unsupported
   statement shapes, expression ops, identifiers, async/generator functions,
   local/declaration forms outside the exact accepted slice, control flow
   outside the accepted branch-plus-canonical-loop-back-edge slice, or dynamic
   shapes must return an unsupported reason before VM execution.
   Do not infer sync-only eligibility from `ExecutionPlan` shape alone.
3. Do not add fallback inside `UnifiedBytecodeVirtualMachine` to
   `ExpressionProgram` evaluation, AST evaluation, or `ExecutionPlanRunner`.
   VM execution should only execute bytecode the unified compiler emitted.
4. Do not route normal production execution through the unified VM in a
   prototype-expansion issue. Runtime routing needs its own issue and proof
   pack.
5. When expanding linear declaration/return packs, flatten only the supported
   `ExpressionProgram` operations into unified instructions. Identifier reads
   should become `LoadSlot`, literals should become program-owned
   `LoadLiteral` entries, and binary operators should stay limited to the
   explicitly proven numeric VM surface. Do not introduce an
   `EvalExpressionProgram` opcode or a runtime callback into the existing
   expression interpreter.
6. For each accepted shape, add focused tests for the emitted unified opcode
   stream, a minimal execution result, and at least one nearby unsupported
   shape that declines cleanly. When an accepted body shape can also appear in
   async or generator functions, include function-kind negative tests.
7. Keep JavaScript semantic claims narrow. A prototype op such as numeric
   `Add` proves only the tested VM behavior; full JavaScript operator coercion
   requires an explicit migration and parity proof.
8. When expanding across branch/control flow, keep accepted CFG ownership
   compiler-side and explicit. Branch shapes plus one canonical
   condition-first loop back-edge IR shape are accepted; all other
   loop/control-flow families must be rejected before VM execution. Compile with
   an IR-instruction-index to unified-bytecode-PC map, patch forward branch/jump
   operands after targets are emitted, and reject unsupported branch payloads or
   non-canonical loop shapes before execution. Do not treat this bounded loop
   support as broad loop support or as permission to call back into existing
   evaluators.
9. When adding or using production-routing eligibility, keep it decline-first
   and narrower than the prototype compiler until runtime proof widens it. Use
   `ExecutionPlan` plus explicit activation metadata, return stable decline
   codes/reasons before VM execution, and accept only the exact production
   opcode subset that has been proven. The current production subset includes
   branch joins, direct branches, joined-local updates, canonical
   condition-first loop backedges, simple do-while consequent backedges, and
   proven unlabeled loop-control target jumps including direct break/continue
   and for-style update continue targets, all through the existing
   compiler-owned shapes; do not add source-syntax exceptions or a
   selector-side second CFG recognizer. `Binary` is production-eligible only
   for the explicitly proven operator subset: arithmetic (`+`, `-`, `*`, `/`,
   `%`, `**`), equality/comparison (`==`, `!=`, `===`, `!==`, `<`, `<=`, `>`,
   `>=`), bitwise/shift (`&`, `|`, `^`, `<<`, `>>`, `>>>`), and
   relational/object tests (`in`, `instanceof`); operators outside that subset
   such as `&&`, `||`, and `??` remain outside the production subset and
   must execute through the existing `JsValue` operator helpers with an
   `EvaluationContext`, not direct numeric extraction. Any new production
   Binary operator must update the selector, unified compiler allowlist, and VM
   semantics in the same slice, with positive selector/route proof and a nearby
   unsupported operator decline/no-route proof. Unsupported Binary operators
   must decline with `UnsupportedPlanShape` and operator-specific diagnostics,
   and labels, unproven or labeled loop-control shapes, calls, dynamic lookup,
   noncanonical loops, and unsupported payloads must decline before VM
   execution. WHY: issue
   `planitem-planmanual1780240661926543000-burn-down-unified-bytecode-production-decl-4d76606d60`
   / PR #2857 retired the stale `PrototypeOnlyBinaryOpcode`,
   `PrototypeOnlyJumpOpcode`, and `PrototypeOnlyJumpIfFalseOpcode` decline
   taxonomy after the proven jump subset had already moved into production.
   Keeping unsupported binary operators under `UnsupportedPlanShape` avoids
   preserving historical prototype-only names as active production outcomes
   while retaining operator-specific diagnostics.
   Declaration instructions also need statement-instruction declines, not only
   activation-descriptor pre-gates. Nested `FunctionDeclarationInstruction` and
   `ClassDeclarationInstruction` can appear in an otherwise route-shaped plan;
   production eligibility must reject them before compilation/VM execution,
   assign stable decline codes, update the checked expansion-contract ledger,
   and prove both eligibility decline and public invocation no-route behavior.
   WHY: issue
   `planitem-planmanual1780240661926543000-burn-down-unified-bytecode-production-decl-d337053574`
   / PR #2951 found that declaration instructions needed explicit production
   declines so runtime hoisting and lexical class declaration semantics stayed
   on the existing IR routes until the VM owns them.
   Switch-style `BreakableKind.HandlesCompletionInternally` wrappers may route
   through ordinary sync production unified bytecode when the existing lowered
   plan proves compiler-owned numeric break targets and the rest of the opcode
   subset is already admitted. Do not preserve a construct-kind-only compiler
   rejection after the route-relevant control flow is reduced to patched jumps,
   and do not turn that sync admission into broad resumable switch support.
   Resumable switch bodies still need adjacent pre-VM decline proof until the
   resumable instruction and environment model owns the lowered shape. WHY:
   Faktorial issue
   `planitem-planmanual1780639098493226000-full-unified-bytecode-execution-burndown-b-1f1b588fd3`
   / PR #3252 removed the stale compiler-only switch-wrapper rejection,
   admitted the ordinary sync route, and pinned a generator switch body as an
   eligibility decline. Related ADR:
   `docs/adrs/0340-admit-sync-switch-breakable-wrappers-through-compiler-owned-targets.md`.
9a. Keep active-with depth traversal separate from zero-depth dynamic-name
    exception-region discovery. `UnifiedBytecodeWithDepthAnalysis` may follow
    `EnterTryInstruction` handler/finally entries by default when successor
    depth is greater than zero so active `with` boundaries are preserved across
    catch/finally. Zero-depth handler/finally traversal must remain an explicit
    opt-in for callers with a separate reachability question, and the ordinary
    dynamic-name production gate may use that opt-in only to discover free call
    targets. Do not let catch-only free reads, stores, catch binding access,
    lexical dynamic declarations, or TDZ-head storage become evidence that the
    whole body can route through the ordinary dynamic-name production path.
    Future widening in this area needs both positive route proof for admitted
    exception-region shapes and nearby no-route/regression proof for A51c-style
    scope/environment gaps. WHY: Faktorial issue
    `planitem-planmanual1780639098493226000-full-unified-bytecode-execution-burndown-b-f2e74379de`
    / PR #3253 first exposed the active-with try/catch/finally reachability gap,
    then repaired a quality-gate regression by splitting the zero-depth
    finally/catch free-callee scan from the main with-depth and plan-shape scan.
    Related ADR:
    `docs/adrs/0341-keep-with-depth-and-zero-depth-dynamic-name-scans-separate.md`.
9b. When admitting sync `using` declarations to production unified bytecode,
    keep resource disposal wired to every VM-owned terminal completion lane.
    Function-body top-level `using` declarations register resources against the
    active function environment, not a block environment, so direct return,
    direct throw, normal function completion, pending return through
    `CompleteFinally`, and pending throw through `CompleteFinally` must all run
    the same active function-environment disposal hook before leaving the VM.
    Do not assume a finally body or `PopEnvironment` will clean function-scope
    resources, and do not repair a missing disposal edge by falling back to
    `ExecutionPlanRunner`, `ExpressionProgram`, or AST evaluation. Future
    widening must include route-hit regressions for return and throw through
    `finally`, plus direct completion/block cleanup neighbors; `await using`
    remains declined until async-dispose settlement is VM-owned. WHY: Faktorial
    issue
    `planitem-planmanual1780639098493226000-full-unified-bytecode-execution-burndown-b-935f087566`
    / PR #3254 first admitted sync `using` declarations, then the repair commit
    `50a9a8513` found that `CompleteFinally` skipped
    `DisposeActiveFunctionEnvironmentResources` for pending return/throw
    completions. Related ADR:
    `docs/adrs/0342-keep-unified-bytecode-finally-completion-resource-disposal-owned.md`.
9c. Keep dynamic `Function` / `new Function` produced bodies quarantined from
    sync production unified bytecode until generated-body semantics are admitted
    by an explicit future route-widening slice. Mark the produced
    `FunctionExpression` at the constructor parse boundary and decline that
    marked body before production route selection; do not replace this with
    caller-local source-text checks, generic dynamic-shape predicates, or VM
    fallback into `ExecutionPlanRunner`, `ExpressionProgram`, or AST
    evaluation. Quarantine proof must pair the generated body's no-route signal
    with an adjacent ordinary function route-hit so the guard cannot silently
    become a broad call-site decline. WHY: Faktorial issue
    `planitem-planmanual1780639098493226000-full-unified-bytecode-execution-burndown-b-11ca930244`
    / PR #3262 closed D4 by adding an explicit produced-body origin marker and
    declining only that body before the sync production unified-bytecode route.
    Related ADR:
    `docs/adrs/0343-keep-dynamic-function-produced-bodies-quarantined-from-production-bytecode.md`.
9d. Treat script-route union gates as shared-production-gate proof, not as a
    license to duplicate every A/B source row at top level. A closure slice
    should prove representative composed scripts that exercise admitted
    property reads/calls, control flow, dynamic global reads/calls,
    completion-slot execution, and script-scope dynamic-target forms such as
    `var` destructuring through `EvaluateScript`, then pin public execution
    with a `unified-bytecode-production-fast-path script` route-hit. Keep
    script-specific safety declines, such as top-level lexical destructuring
    whose TDZ/lexical-environment semantics are not VM-owned, explicit in the
    non-residue ratchet. Keep eval-injected script bindings in dynamic residue.
    Do not silently close a script union gate by inheriting function/resumable
    proof only, and do not broaden dynamic-residue exceptions into ordinary
    script admission. WHY: Faktorial issue
    `planitem-planmanual1780639098493226000-full-unified-bytecode-execution-burndown-b-f1e9d4f56f`
    / PR #3269 closed C3 with representative script eligibility rows, a
    composed top-level runtime route test, and ratchet rows that distinguish the
    remaining lexical-destructuring safety decline from eval-injected dynamic
    residue.
10. When invoking production unified bytecode from sync calls, keep the bridge
    slot-layout owned and fast-path ordered. The production unified route runs
    before direct specialized simple-return binary/chain shortcuts and the
    broader `SyncIrCallTrampoline` so accepted branch, join, and canonical-loop
    shapes are not swallowed by older fallback paths. The simple-return
    shortcuts remain fallback paths only for shapes that do not pass production
    bytecode eligibility, and any generic simple IR activation shape that is
    not owned by the expression-program bridge must explicitly decline to the
    outer invocation fallback. Populate an invocation-local slot span from
    `ActivationSlotShape` by
    filling `undefined` and writing parameters through `ParameterSlotIndices`;
    do not create a `JsEnvironment`, call `ExecutionPlanRunner`, or add VM
    fallback for accepted programs. Prove selected routing, faster-route
    preservation, and nearby declines through public invocation tests plus the
    activation proof pack. Also keep the ordinary-sync route order itself
    source-gated inside `TryInvokeIrFast<TArgs>(...)`: accepted production
    unified bytecode first, specialized binary and binary-chain routes next,
    `SyncIrCallTrampoline` after those fallbacks, and generic runner-owned
    activation shapes as an explicit no-route decline. WHY: issue
    `planitem-planmanual1779965179415360000-batch-1-receiver-aware-call-execution-boun-8f4d6fd0f4`
    / PR #2623 found the production code already had the intended order, but
    route logs alone did not guard the whole fallback chain from drifting back
    toward older ADR 0204/0208 wording. If a future slice changes priority
    again, make that explicit and prove the older route remains covered.
10a. When admitting class constructors to production unified bytecode, keep the
     route body-shaped, not just descriptor-shaped. Base constructors may route
     only when the invocation bridge creates the constructed `this`, supplies the
     base constructor environment, and the body otherwise satisfies the ordinary
     production plan shape; simple identifier parameters, simple literal-default
     parameters, and final-rest identifier parameters are the admitted parameter
     variants. Explicit derived constructors may route when the derived
     environment bridge owns the same admitted parameter variants and the body is
     otherwise on the proven `super(...)` route, including simple named/computed
     property-read argument spans such as `super(items.length)` or
     `super(items["length"])` and separately proven
     post-`super()` `this`/field/private-brand shapes. Runtime-dependent default
     parameter expressions, destructured parameters, observable `arguments`, and
     unowned super property access must stay on the existing constructor path
     until the VM owns those semantics for that shape. Prove route hits for base
     constructor `this` initialization, base constructor object returns,
     `super(value)` / `super()`, simple property-read `super(...)` arguments,
     admitted constructor parameter variants, and nearby no-route behavior for
     runtime defaults and destructuring.
10b. Treat `arguments` as an arguments-object dependency only after binding
     resolution proves it is not an ordinary activation slot. A parameter named
     `arguments` or a lexical body binding named `arguments` is regular slot
     traffic for production unified bytecode reads, `typeof`, updates, and call targets; the real implicit
     arguments object and writes/deletes of the
     implicit arguments binding still decline before VM execution. Keep the
     invocation descriptor, expression selector, and compiler in agreement so a
     name-only `operation.IsArguments` check cannot block shadowed bindings or
     accidentally admit the real arguments object.
10c. When admitting arrow functions to production unified bytecode, reuse the
     lowered-plan dependency proof used by simple IR activation. Only arrows
     whose simple return program contains no lexical `this`, `new.target`, or
     super operation, no closure-variable or dynamic identifier dependency, and
     no nested function/class literal may clear the arrow and captured-activation
     blockers. Dependency-bearing arrows, parameter expressions, non-simple
     parameters, private scopes, and dynamic lookup stay on existing routes until
     the VM and invocation bridge own those semantics directly.
10d. When admitting async generators to production unified bytecode, keep the
     route owned by `UnifiedBytecodeResumeState` and the existing async-generator
     settlement contract. Simple-parameter direct-yield `async function*` bodies
     may route through `UnifiedBytecodeVirtualMachine.ExecuteResumable` when
     `EvaluateResumable` accepts the lowered plan, and `AsyncGeneratorInvoker`
     must map `Yield`, `Completed`, `Throw`, and `PendingAwait` back through the
     same promise settlement path used by the IR runner. Non-awaited
     async-generator `yield*` may also route once the VM owns delegated async
     iterator `.next(value)`, `.return(value)`, and `.throw(value)` settlement
     through that same `PendingAwait` bridge. Awaited delegated sources may route
     when the source expression lowers to `AwaitValue` before the existing
     `YieldStar` driver, so `yield* await ...` must use the same resumable
     async-generator settlement path instead of the IR runner. Non-simple
     parameter lists must stay on the IR runner until the VM owns their eager
     parameter-initialization effects before iterator creation. Do not treat
     direct-yield or delegated `yield*` admission as broad async-generator
     support or add VM fallback into
     `ExecutionPlanRunner`, `ExpressionProgram`, or AST evaluation. WHY: issue
     #3135 / PR #3142 added the first async-generator resumable route and kept
     delegated async-generator `yield*` declined until a later slice owned
     delegation semantics. Faktorial issue
     `planitem-planmanual1780639098493226000-full-unified-bytecode-execution-burndown-b-1747a4b32a`
     / PR #3221 then admitted the non-awaited async-generator `yield*` lane by
     settling delegated async iterator `next`/`return`/`throw` results through
     the existing resumable `PendingAwait` async-generator bridge. Faktorial
     issue
     `planitem-planmanual1780639098493226000-full-unified-bytecode-execution-burndown-b-c98550dd55`
     then admitted the awaited-source lane by compiling the source expression
     through `AwaitValue` before `YieldStar`.
10e. When admitting resumable generator or async shapes that contain nested
     function literals or `try/finally` cleanup, prove the surrounding
     suspension context, not only the direct opcode allowlist. A nested function
     literal whose lowered body needs arrow lexical `this`, `new.target`,
     `super`, or private-name context must decline until the resumable route
     materializes that closure context. A nested function literal that captures
     root body locals may route only when the relevant invocation path
     materializes a body `JsEnvironment`, mirrors resume-state flat slots into
     it, and synchronizes slot writes back into that environment across
     suspension. Treat this as generator-owned until async and async-generator
     invocation/settlement paths prove the same environment lifetime; do not
     infer async captured-literal safety from the sync-generator bridge. A
     pending `finally` body that writes a captured or free binding must also
     decline for generator early-close (`.return()` / `.throw()`) until the VM
     owns that cleanup execution. Do not infer resumable safety from "no
     ordinary activation-slot capture" alone: lexical/private context,
     materialized body-environment lifetime, and pending-finally cleanup are
     separate semantic dependencies. Future widening must include adjacent
     no-route tests for these hazards, not only accepted route tests. WHY:
     issue #3172 / PR #3179 repaired red `main` after the resumable route
     admitted generator shapes that were correct on ordinary `.next()`
     completion but wrong for pending-finally early close or nested arrow
     private-field access. Issue `autrun-dj0vu3jrima8-13c399233d` / PR #3178
     preserved the same boundary for nearby resumable-generator widening.
     Faktorial issue
     `planitem-planmanual1780639098493226000-full-unified-bytecode-execution-burndown-b-0768364f94`
     / PR #3234 admitted the B23 sync-generator captured-root-local subset only
     after adding a materialized resumable body environment plus slot
     synchronization; async captured literals and lexical/private closure
     contexts remain declined. Faktorial issue #gh3238 / PR #3240 then admitted
     the narrow B36 sync-generator direct root hoisted-helper subset that
     captures root body locals by creating the helper against the materialized
     body environment and pre-populating the compiled VM flat slot before
     `ExecuteResumable`; async/async-generator captured helpers,
     recursive/sibling helper graphs, dynamic/eval helpers, block/Annex B
     declarations, and class declarations remain declined. Related ADRs:
     `docs/adrs/0323-keep-resumable-unified-bytecode-context-sensitive-cleanup-declined.md`,
     `docs/adrs/0324-keep-resumable-generator-context-cleanup-declines-explicit.md`,
     `docs/adrs/0333-admit-generator-captured-function-literals-through-materialized-resumable-body-environment.md`,
     and
     `docs/adrs/0335-admit-generator-captured-hoisted-helpers-through-materialized-body-environment.md`
10f. When admitting class literals inside resumable unified bytecode, classify
     each class element by the class-definition state it needs during creation,
     not by the presence of `LoadClassLiteral` alone. Public member functions
     and public instance field initializers that contain `super` may route only
     when `LoadClassLiteral` can reuse the captured
     `UnifiedBytecodeResumeState.CallingEnvironment` and the existing
     class-definition program cache owns the member/initializer execution.
     Public non-computed instance fields may route only when their lowered
     field initializer programs do not read resumable activation slots; an
     initializer that captures a body binding still needs a future materialized
     body-environment route. Public non-computed static fields may route when
     their initializers are immediate value expressions owned by a temporary
     class-definition environment bridged from the resume state's flat slots.
     Static field initializers that create closure-bearing functions, methods,
     accessors, object literals, or nested class bodies with activation captures
     must still decline until the resumable route owns a materialized
     body-environment route for those created closures.
     Extends expressions that read resumable activation slots, computed member
     names, static elements, broad static-field creation, and private
     method/accessor bodies that capture activation slots must remain explicit
     pre-VM declines until the resumable route owns that class-definition
     environment state directly. Future B24 widening needs both positive
     route/runtime proof for the admitted element family and nearby no-route
     proof for the still-unowned class-definition families.
     WHY: issue
     `planitem-planmanual1780639098493226000-full-unified-bytecode-execution-burndown-b-f3d5eef198`
     / PR #3205 admitted public class-literal member `super` and public
     instance-field initializer `super` by renaming the B24a-only gate into a
     B24 class-literal shape gate, detecting actual `SuperExpression` use, and
     keeping activation-slot-reading `extends`, computed/static/private
     families declined. The durable lesson is that `super` in class members is
     routeable when existing class-definition machinery and the captured calling
     environment already own lookup, but the route must not infer broad
     class-expression safety from that narrow ownership proof.
     Issue
     `planitem-planmanual1780639098493226000-full-unified-bytecode-execution-burndown-b-8327a9bee2`
     / PR #3202 admitted the B24b public non-computed instance-field subset by
     inspecting class-definition field initializer programs and declining
     activation-slot captures until the resumable route owns a materialized
     body environment. Related ADR:
     `docs/adrs/0328-admit-resumable-class-literal-public-instance-fields-with-activation-safe-initializers.md`.
     Issue
     `planitem-planmanual1780639098493226000-full-unified-bytecode-execution-burndown-b-7d0f3d6a80`
     / PR #3194 then admitted the narrow B24f private method/accessor shape.
     The durable lesson is the same classification discipline: private member
     creation can reuse shared class creation only when the constructor and
     private member bodies do not capture resumable activation slots; captured
     bodies still need a future materialized body-environment route. Related
     ADR: `docs/adrs/0327-admit-resumable-class-literal-private-members-through-shared-class-creation.md`.
     Issue
     `planitem-planmanual1780639098493226000-full-unified-bytecode-execution-burndown-b-61145a55dd`
     / PR #3197 admitted the B24c public static-field subset by creating a
     resumable class-definition environment from flat slots when static field
     initializer value expressions need activation bindings, while keeping
     closure-valued static initializers and neighboring static element families
     declined. The durable lesson is that static field value evaluation and
     closure creation are different ownership problems: the former can be
     bridged for class creation, while the latter still needs a materialized
     body environment that outlives class creation. Related ADR:
     `docs/adrs/0329-admit-resumable-class-literal-static-fields-with-owned-environments.md`.
10g. When admitting iterator init driver shapes, keep source-payload shape and
     iterator driver kind as separate concepts. An iterator driver must carry
     exactly one source payload (`IterableProgram` or `AwaitedProgram`), and a
     sync driver with an awaited source is still synchronous driver state after
     the resumable route owns the await step. `IteratorDriverKind.Await` is the
     async iterator protocol boundary; B41 admits the simple resumable VM subset
     only after protocol state, async next-result/value settlement, close
     settlement, and driver cleanup through return/throw/break/continue are
     VM-owned. Future proof packs should test the helper directly for sync
     iterable admit, sync awaited-source admit, async-kind admit, and
     missing/dual source declines, because async-like activation pre-gates can
     otherwise hide the iterator-kind boundary. Keep the resumable opcode
     allowlist, `ExecuteResumable` switch, and expansion-contract gap ledger in
     sync: admitting `Break`, `Continue`, or `JumpWithDriverCleanup` is valid
     only when the VM owns the matching cleanup topology and settlement path.
     WHY: issue
     `planitem-planmanual1780639098493226000-full-unified-bytecode-execution-burndown-b-7e33a72606`
     / PR #3217 hardened `IsSupportedIteratorInit` after earlier ADR 0288
     wording made awaited source and async iterator kind look like one boundary.
     Issue
     `planitem-planmanual1780639098493226000-full-unified-bytecode-execution-burndown-b-b462ae896c`
     / PR #3220 then admitted the B41 async iterator subset by adding resumable
     async iterator `next`/yielded-value awaits, async `return()` close
     settlement, and the control-cleanup opcode allowlist alignment.
     Related ADR:
     `docs/adrs/0330-keep-iterator-init-async-kind-and-awaited-source-gates-separate.md`.
10h. When admitting captured per-iteration `let` / `const` loop bindings
     through ordinary sync production `PushEnvironment`, keep the
     CreatePerIterationEnvironment copy semantics descriptor-owned. A copied
     per-iteration binding needs a flat slot, an explicit
     `PerIterationCopySlotIndices` descriptor entry, a VM snapshot before scope
     rebinding, and a write into the fresh scope environment after rebinding.
     Do not fix this family by treating all lexical slots as ordinary TDZ entry
     slots, by relying on dynamic lexical mirroring, or by inferring Annex B
     block-function or resumable `PushEnvironment` safety from the sync A44
     route. Positive tests must assert both captured closure values and
     production routing, with adjacent boundaries kept explicit. WHY: issue
     `planitem-planmanual1780639098493226000-full-unified-bytecode-execution-burndown-b-c99c23d77d`
     / PR #3231 admitted A44 only after slot stamping, compiler descriptors,
     and VM scope-entry handling agreed on per-iteration copy slots; the
     earlier forced route left closure-visible bindings in TDZ. Related ADR:
     `docs/adrs/0334-admit-captured-per-iteration-bindings-through-push-environment-copy-slots.md`.
10i. When admitting descriptor-backed block-scoped function declarations on the
     ordinary sync production route, treat Annex B blocked-name setup as part
     of fast activation, not as an optional IR-runner side effect.
     `PushEnvironment` and `DeclareFunction` may own the block environment and
     runtime declaration update, but the function var environment must already
     carry the same blocked-name set as the existing non-VM activation paths.
     Pair positive route proof for sloppy block-function updates with adjacent
     blocked-name and strict no-leak proofs. WHY: issue
     `planitem-planmanual1780639098493226000-full-unified-bytecode-execution-burndown-b-cbbdaf84ff`
     / PR #3233 admitted A43, then the Annex B `let f = 123` blocked-name shape
     showed that production unified fast activation had skipped the shared
     blocked-name setup. Related ADR:
     `docs/adrs/0337-keep-annex-b-blocked-names-shared-for-unified-fast-activation.md`.
10j. Keep accepted production unified-bytecode route source gates broad and
     exception-driven. Execution sections that claim an accepted unified route
     should reject any remaining `ExecutionPlanRunner`, `ExpressionProgram`,
     AST evaluator, script-runner, or async-step delegation marker after
     removing only narrowly classified type names or setup-only runner
     environment creation needed before `UnifiedBytecodeResumeState` exists.
     Do not weaken the accepted-path guard with marker-specific exceptions for
     fallback runner calls, comments, or broad expression-program text; classify
     the exact source section instead and keep the production execution section
     VM-owned. WHY: issue
     `planitem-planitem-planmanual1780639098493226000-full-unified-bytecode-execution-b-71d456f552`
     / PR #3273 repaired a review blocker where a source gate had become too
     narrow to catch accepted-route delegation drift. The repair restored broad
     forbidden-token checks while explicitly classifying async-generator
     result/kind type names and setup-only runner environment creation.
10k. When retiring ordinary sync runner residue, convert the source gate from
     a classified fallback allowance to a tombstone. `TryInvokeIrFast<TArgs>(...)`
     must not construct `ExecutionPlanRunner`, call `.RunSync(`, or silently
     recreate a deleted helper such as `InvokeOrdinarySyncRunnerResidue(...)`.
     Unsupported ordinary sync shapes can still fall through to the outer
     `InvokeWithContextSlow` runner, but that fallback boundary must stay
     outside the fast-route method and remain named as a classified fallback.
     If a future slice intentionally restores an ordinary-sync runner bridge,
     add or update a source gate that classifies the exact method and fallback
     reason instead of weakening the tombstone. WHY: Faktorial issue
     `planitem-planitem-planmanual1780639098493226000-full-unified-bytecode-execution-b-eff2688ad3`
     / PR #3278 removed the last ordinary sync `TryInvokeIrFast` runner
     residue after the delivery branch initially had zero net diff versus
     `origin/main`; the durable guard is the negative source gate that blocks
     `RunSync` and the deleted residue helper from re-entering the fast route.
11. When updating docs, ADRs, roadmap text, or evidence reports for unified
    bytecode production routing, treat ADR 0253 as the current loop-control
    production widening layered on ADR 0210, and keep ADR 0204/#2227
    direct-branch wording historical unless a newer accepted ADR supersedes it.
    The docs must state the no-mixed-execution rule, list the exact eligible
    opcode/control-flow/operator families, keep unsupported shapes as pre-VM
    declines, and describe Batch 5 memory/profile evidence as allocation
    stability only unless a separate before/after proof justifies a
    performance-improvement claim.
11a. When citing benchmark or profile rows as unified-bytecode routing evidence,
     report the route-hit count from `rtk ./tools/profile <profile> --route-hits`
     alongside timing or allocation rows. Timing and allocation rows only prove
     performance or stability for the measured workload; they do not prove that
     the production unified-bytecode VM was reached. A profile with zero
     `unified-bytecode-production-fast-path` hits must be treated as fallback
     evidence, not production unified-bytecode coverage. Keep route-hit-only
     runs separate from external-profiler CPU/memory runs so observability does
     not perturb profiler-backed measurements. WHY: issue
     `planitem-planmanual1780240661926543000-burn-down-unified-bytecode-production-decl-7b8017c72a`
     / PR #2962 added `ProfileRunner --route-hits` and direct
     `tools/profile --route-hits` after the final production-decline proof pass
     needed to distinguish generic manifest profile rows from actual
     `unified-bytecode-production-fast-path` execution. Related ADR:
     `docs/adrs/0320-keep-unified-bytecode-route-hit-evidence-explicit.md`.
11b. When adding, removing, or editing a non-empty
     `UnifiedBytecodeCompiler.TryCompile` `reason = ...` assignment, update the
     `Compiler Decline Reason Templates (current)` inventory in
     `docs/unified-bytecode-expansion-contract.md` and the matching checklist
     owner leaf in `docs/plans/bytecode-burndown-checklist.md` in the same
     delivery. Keep failed compiler attempts wrapped as
     `UnsupportedPlanShape` unless the slice intentionally changes the
     route-facing production decline contract; compiler diagnostic strings are
     burn-down owner evidence, not automatic enum members. Preserve dynamic
     residue separately from compiler-owned non-dynamic route gaps. WHY: issue
     #3134 / PR #3138 decomposed the stale A51/B47/E2 compiler umbrella into
     A51a-A51m plus B47a and added a source gate over compiler reason
     templates. Review also found stale checklist counters after the first
     update, so future edits must update the contract, owner leaves, and counts
     together. Related ADR:
     `docs/adrs/0322-keep-unified-bytecode-compiler-decline-inventory-source-guarded.md`.
11c. When refreshing route-hit tables or decline/gap inventories, re-run the
     documented workload and contract-audit commands on the current
     `origin/main` baseline after any source-affecting rebase before updating
     `docs/bytecode-progress.md`,
     `docs/unified-bytecode-expansion-contract.md`, or
     `docs/plans/bytecode-burndown-checklist.md`. Treat specific route-hit
     counts and checklist totals as time-sensitive evidence, not durable
     architecture facts. Report the baseline commit or timestamp, changed rows,
     aggregate route-hit delta when a table is being rebaselined, and the exact
     contract/checklist inventory counts used for the edit. WHY: issue
     `planitem-planmanual1780639098493226000-full-unified-bytecode-execution-burndown-b-003b4908a5`
     / PR #3182 found the `aa93c7112` bytecode-progress snapshot stale after
     subsequent source changes; rerunning the route-hit and
     `ExpressionProgramCoverageMapTests` audits on current main changed several
     documented rows and corrected the A/B/C checklist counts without changing
     runtime behavior.
12. When defining property-read production eligibility, keep candidate
    recognition separate from VM acceptance until the same slice adds compiler
    opcodes, VM semantics, route-priority proof, and negative no-route tests.
    Direct named candidates are activation-resolved base reads followed by one
    or more non-optional, non-private `GetNamedProperty` operations; this
    supersedes the older exact two-hop limit from ADR 0222. Direct computed
    candidates must preserve the exact
    ordinary-read lowering sequence:
    `RequireObjectCoercible(Depth: 1)`, then `ResolvePropertyKey`, then
    non-optional `GetComputedProperty`, with only production-safe base/key
    loads before it. Recognized candidates that lack VM support must decline as
    `PropertyReadCandidateRequiresVmSupport`, not compile or run. Adjacent
    families such as calls/constructs, member call targets, writes, updates,
    delete, `super`, `this`, optional chains, object literal/spread, dynamic
    lookup, and out-of-boundary computed reads need stable pre-VM decline codes
    plus concrete source-example tests. Also scan all expression-program-bearing
    instructions, including evaluate-and-discard and throw payloads, before
    generic compiler fallback so property-read hazards cannot hide outside
    return expressions. Computed-read negative coverage must include unsupported
    key payloads, not only unsupported bases or final `GetComputedProperty`
    shapes. Keep concrete examples such as `box[{ value: 1 }]` and
    `box[{ ...source }]` declining as `ObjectLiteralOrSpreadDependency` so
    key-payload hazards stay visible before generic property-read boundary
    declines. WHY: issue
    `planitem-planmanual1780240661926543000-burn-down-unified-bytecode-production-decl-e62d6987e4`
    / PR #2912 admitted simple object-literal spread entries, but review found
    that the same `ObjectSpread` op can appear inside an ordinary computed
    property-key payload. The repair kept computed-key spread payloads out of
    production routing by sharing the computed-read key-payload bounds helper
    and requiring object spread to belong to a measured simple object-literal
    span.
13. When making property-read candidates executable in production unified
    bytecode, keep the read semantics VM-owned and fallback-free. Named keys
    belong in `UnifiedBytecodeProgram.StringConstants` and must execute through
    an owned `GetNamedProperty` opcode. Computed reads must emit the exact
    ordinary-read sequence `RequireObjectCoercible(Depth: 1)`,
    `ResolvePropertyKey`, then `GetComputedProperty`, and the VM must use the
    existing `JsOps` property-key and property-lookup helpers with the active
    `EvaluationContext`. Do not satisfy property-read execution by calling back
    into `ExpressionProgram`, `ExecutionPlanRunner`, or AST evaluation. Future
    optional-chain, member-call, write/update, `super`, dynamic lookup, or
    richer computed-key support needs its own selector/compiler/VM/proof slice
    instead of widening this first executable boundary.
14. When adding compiler helpers that probe an `ExpressionProgram` shape and
    append unified instructions, keep the helper side-effect-free until the
    full shape is accepted. Either prevalidate the whole operation sequence
    before mutating shared instruction/string/literal builders, or emit into
    scratch builders and commit atomically. A helper that returns `false`,
    with or without a decline reason, must not leave partial stack instructions
    or constants behind for the next fallback path. Pair accepted examples with
    adjacent unsupported examples so partial-emission stack drift is caught by
    the focused proof pack. WHY: issue
    `planitem-planmanual1780198120145433000-widen-unified-bytecode-production-conditio-0aa2351edc`
    / PR #2812 found that
    `TryAppendFirstBoundaryNamedLogicalPropertySet` could append base/property
    setup before later RHS or neighbor-shape rejection. The accepted repair
    staged unified instructions plus literal and string constants in scratch
    builders, then replaced the shared builders only after the complete direct
    named logical-assignment shape was accepted.
15. When proving no-route behavior for unsupported property-read-adjacent
    shapes through public invocation logs, assert absence of
    `unified-bytecode-production-fast-path` for the exact function or method
    body that contains the unsupported expression. If the source uses a wrapper
    function to call a class method or nested helper, give the owning method a
    unique name and target that name in the negative assertion. A wrapper-level
    no-route assertion only proves the wrapper was not routed; it does not prove
    the unsupported callee body stayed out of production unified bytecode.
16. When adding property write/update production routing, guard private-name
    strings before treating named property opcodes as ordinary property access.
    Expression bytecode can represent private member reads, writes, and updates
    as named property operations carrying a private-name string; selector scans
    must decline those with `PrivateFieldDependency`, and compiler shape probes
    must reject them before appending `GetNamedProperty`, `SetNamedProperty`,
    or `UpdateNamedProperty` opcodes or string constants. Pair every accepted
    ordinary named write/update widening with negative private read/write/update
    tests. WHY: issue
    `planitem-planmanual1779887420937175000-batch-1-boundary-and-baseline-gate-define-5cc93efb5a`
    / PR #2379 initially widened ordinary property writes/updates, and review
    found that private-field named-property op shapes were not guarded; the
    build-back fix added selector and compiler guards plus focused
    `receiver.#field`, `receiver.#field = value`, and `receiver.#field++`
    declines.
17. When proving strict-mode property writes or updates on the production
    unified bytecode path, carry lexical strictness into the VM explicitly and
    prove that the strict body actually logs
    `unified-bytecode-production-fast-path`. The production bridge does not
    create the normal function `JsEnvironment`, so property handle resolution
    must not rely only on `context.CurrentScope.IsStrict`. Directive prologue
    support in the compiler must stay no-op and narrow: only string-literal
    `EvaluateAndDiscard` instructions may be skipped as directive prologue
    no-ops. Non-directive `EvaluateAndDiscard` is now governed by rule 27 /
    ADR 0252: compile the supported expression program and append `Pop`;
    decline only when the underlying operation family is not yet
    selector/compiler/VM-owned. For computed write proofs, keep the admitted write function
    call-free and place unrelated RHS side effects at the call site if needed,
    then assert evaluation order and route logging on the admitted function.
    WHY: issue
    `planitem-planmanual1779887420937175000-batch-1-boundary-and-baseline-gate-batch-2-d45261c97e`
    / PR #2396 initially added property-write proof coverage, but review found
    one computed-write proof did not exercise the admitted fast path and the
    strict arm was not proven to route. The build-back fix added directive
    string-literal discard support plus explicit VM strictness threading so
    strict failed writes throw through the owned unified path.
18. When hardening property-write production boundaries, keep dynamic value
    dependencies and computed-key expressions with unowned payloads as pre-VM
    declines until the same slice owns selector,
    compiler, VM, and route-proof semantics for those shapes. Pair each
    eligibility decline with public invocation fallback/no-route proof for the
    exact function body. WHY: issue
    `planitem-planmanual1779887420937175000-batch-1-boundary-and-baseline-gate-batch-3-d4c5a8e668`
    / PR #2415 added focused logical write, dynamic RHS write, and computed key
    expression write coverage after the ordinary property-write boundary was
    admitted. Without those neighboring declines, future property-set widening
    can accidentally route dynamic lookup or computed-key
    expression payloads through a VM path that does not yet own those semantics.
19. When admitting direct compound property writes into production unified
    bytecode, preserve the reference operands with dedicated get-for-set opcodes
    instead of adding generic stack duplicate/swap opcodes or VM fallback. Named
    compound writes must keep the receiver live for `SetNamedProperty`, and
    computed compound writes must keep both the receiver and the already-resolved
    key live for `SetComputedProperty`. Keep the selector and compiler matched
    to exact operation sequences, and leave nested member chains, richer
    computed keys, optional chains, `super`, private fields,
    `delete`, calls, destructuring, and dynamic lookup as pre-VM declines until
    a later slice owns their full proof. The admitted compound-assignment
    operators are the 12 arithmetic and bitwise operators (`+=`, `-=`, `*=`,
    `/=`, `%=`, `**=`, `&=`, `|=`, `^=`, `<<=`, `>>=`, `>>>=`). In this
    compound-write slice, the three logical operators (`&&=`, `||=`, `??=`)
    decline as `PropertyWriteDependency` because their conditional
    short-circuit semantics require a branch opcode that the compound get-for-set
    model does not provide. Direct member logical assignment routing is owned by
    the dedicated logical-assignment rules below, not by this compound-write
    rule. WHY: issue
    `planitem-planmanual1779887420937175000-batch-1-boundary-and-baseline-gate-batch-4-f0057ffdc4`
    / PR #2426 widened direct named/computed compound writes by adding
    `GetNamedPropertyForCompoundSet` and `GetComputedPropertyForCompoundSet`.
    Those opcodes intentionally avoid treating compound writes as permission for
    a generic expression-stack VM or broad property-write routing. Issue
    `planitem-planmanual1780157100924814000-baseline-batch-4-compound-property-writes-8f79995e52`
    / PR #2755 added parametric Theory coverage over all 12 admitted operators
    and confirmed `&&=`, `||=`, and `??=` as explicit pre-VM declines.
20. When adding a broad production proof pack for an already-admitted unified
    bytecode family, prove the accepted and rejected boundaries separately. For
    accepted source shapes, assert selector eligibility, `None` decline code,
    required owned opcodes, and an allowed-opcode subset per case so a future
    compiler widening cannot smuggle unowned operations into the route. For
    invocation coverage, assert `unified-bytecode-production-fast-path` on the
    exact newly covered function variants and assert no-route fallback for
    adjacent unsupported bodies such as discarded writes/updates, nested member
    chains, complex compound writes, and destructuring writes. When an admitted
    family is operator-parameterized (such as compound property writes), use
    `TheoryData` over the complete admitted operator set rather than a single
    representative; a single-operator proof gives a false sense of coverage and
    would not catch a future operator incorrectly excluded from the admitted
    list or an incorrect decline for a logical-assignment operator that should
    stay declined. WHY: issue
    `planitem-planmanual1779887420937175000-batch-1-boundary-and-baseline-gate-batch-5-59d012f0b5`
    / PR #2438 added the post-boundary baseline proof pack for property
    write/update routing. The useful lesson was that behavior-only tests were
    not enough; accepted mutation shapes also needed explicit owned-opcode
    whitelist proof, while unsupported neighbors still needed public no-route
    proof. Issue
    `planitem-planmanual1780157100924814000-baseline-batch-4-compound-property-writes-8f79995e52`
    / PR #2755 demonstrated the operator-parametric pattern: a
    `CompoundNamedPropertyWriteOperators` `TheoryData` property enumerates all
    12 admitted compound-write operators, and two Theory methods prove both
    named and computed paths accept each one.
21. When admitting nested named receiver writes or updates into production
    unified bytecode, keep the route owned by existing property opcodes and
    prove the extra receiver stack pressure explicitly. Simple chains such as
    `box.child.value = y`, `++box.child.count`, `box.child.value += y`, and
    `box.child.value &&= y` may route when the root is activation-resolved,
    every intermediate step is a non-optional non-private `GetNamedProperty`,
    the final operation is an owned property write, update, compound-write, or
    logical-write shape, and the RHS is still a simple production-owned
    payload. Direct computed assignments, compound writes, logical writes, and
    updates may also route with a supported computed-key expression span and
    simple RHS, for example `box[key + suffix] = y`,
    `box[key + suffix] += y`, `box[key + suffix] &&= y`, and
    `box[key + suffix]++`. Do not infer that optional chains, `super`, private
    names, calls, dynamic lookup, or unsupported RHS/key spans are covered by
    the same widening. If preserving an
    intermediate receiver means the compiled unified-bytecode program needs a
    deeper stack than `ExpressionProgram.MaxStackDepth` reports, raise the
    compiled stack-depth calculation and add a focused stack-depth proof instead
    of adding VM fallback to `ExpressionProgram`, `ExecutionPlanRunner`, or AST
    evaluation. WHY: issue
    `planitem-planmanual1780240661926543000-burn-down-unified-bytecode-production-decl-76ae54eb13`
    / PR #2897 admitted simple nested named property assignment/update through
    existing `GetNamedProperty`, `SetNamedProperty`, and `UpdateNamedProperty`
    opcodes, while keeping nested compound/logical and richer computed families
    declined. The delivery also added a compiled stack-depth floor because the
    lowered unified-bytecode receiver-preservation stack can exceed the source
    `ExpressionProgram` stack metadata.
22. Before widening parallel unified-bytecode lanes, start from
    `docs/unified-bytecode-expansion-contract.md` and keep contract, roadmap,
    and ADR/rule surfaces synchronized in the same slice when shared boundary
    text changes. The contract must separate current support from
    reserved/planned lanes, keep the no-mixed-execution rule explicit, and keep
    next unsupported buckets explicit (wider call families; unsupported
    driver-state subshapes: async iterator drivers, awaited iterator/for-in
    sources, descriptor-ineligible destructuring targets, dynamic-name
    destructuring targets, and non-slot/unified-slot failures that still decline
    before VM execution; dynamic lookup) until dedicated ownership slices land.
    Expression-level `ApplyBindingTarget` assignment destructuring is no longer
    a blanket unsupported bucket when it can use the descriptor-backed bridge
    from ADR 0318; unsupported binding declarations and driver shapes still
    decline before VM execution. Label-dependent
    control flow is no longer an unsupported bucket: ADR 0285 / issue #2679
    admitted it (see rule #36), and driver-crossing labeled-abrupt shapes are
    owned by compiler-emitted cleanup topology rather than a label-specific
    decline bucket. Keep the drift guard in
    `ExpressionProgramCoverageMapTests` covering required headings plus current
    `UnifiedBytecodeOpCode` and `UnifiedBytecodeProductionDeclineCode` names.
    Treat newly VM-executed literal-construction opcodes such as `CreateArray`,
    `ArrayPush`, `CreateObject`, and `DefineObjectProperty` as current contract
    inventory in the same delivery slice; do not defer them to a learn-stage
    docs pass. WHY:
    issue
    `planitem-planmanual1779943568009120000-batch-1-shared-bytecode-surface-and-parall-25de646b9f`
    / PR #2466 established the shared contract before parallel lane work so
    future agents do not re-discover owner surfaces, imply planned-lane runtime
    support, or land selector/compiler/VM changes without matching proof
    commands. Issue
    `planitem-planmanual1779943568009120000-batch-1-shared-bytecode-surface-and-parall-9f49fefe3d`
    / PR #2476 then failed the quality gate after a literal-construction lane
    added current unified opcodes without the matching contract inventory; the
    build-back repair added the missing opcode names before the delivery PR was
    merged. Issue
    `planitem-planmanual1779943568009120000-batch-1-shared-bytecode-surface-and-parall-b7fbd79613`
    / PR #2474 repeated the same risk for primitive opcodes (`TypeOf`,
    `TypeOfIdentifier`, unary operators, `ToString`, and `Pop`), confirming the
    contract inventory is a delivery gate for every VM-executed opcode lane.
    Issue
    `planitem-planmanual1779943568009120000-batch-1-shared-bytecode-surface-and-parall-651d15496c`
    / PR #2508 closed the Batch 5 documentation surface by synchronizing the
    expansion contract, roadmap, ADR 0256, and this rule around explicit next
    unsupported buckets. The durable lesson is that shared boundary wording and
    unsupported-bucket guidance are delivery-slice artifacts, not later cleanup.
    Issue #2574 / PR #2584 then removed stale generic driver-state bucket
    wording from this rule after the roadmap/contract wording had become
    explicit, confirming that adjacent rule text is part of the same
    synchronization boundary.
23. When admitting activation-value loads into production unified bytecode,
    keep them call-time owned by the sync invocation bridge. `LoadThis` and
    `LoadNewTarget` may execute only as owned VM opcodes supplied with
    invocation-local values from `SyncFunctionInvoker`; sloppy `this` must reuse
    the existing non-strict receiver coercion helper before VM execution, and
    ordinary-call `new.target` stays within the existing undefined-newTarget
    production gate. Do not create a `JsEnvironment`, call back into
    `ExpressionProgram`, or infer that `arguments`, arrows, classes,
    constructors, captured/dynamic activation, home/super/private state, or
    non-undefined construct targets are covered by this lane. Keep selector,
    compiler, VM, public route proof, and
    `docs/unified-bytecode-expansion-contract.md` opcode inventory aligned in
    the same slice. WHY: issue
    `planitem-planmanual1779943568009120000-batch-1-shared-bytecode-surface-and-parall-9db8c7189a`
    / PR #2473 admitted `this` and ordinary-call `new.target` through
    production unified bytecode, and the quality gate caught a missing
    `LoadThis` / `LoadNewTarget` expansion-contract inventory update before the
    lane was complete.
24. When admitting primitive unary, conversion, discard, or strict equality
    operations into production unified bytecode, keep the semantics VM-owned and
    activation/TDZ-aware. The compiler may flatten only supported
    `ExpressionProgram` operations into owned opcodes such as `TypeOf`,
    `TypeOfIdentifier`, unary plus/minus/logical-not/bitwise-not/void,
    `ToString`, `Pop`, and explicitly proven strict equality operators. The VM
    must reuse the same runtime helpers as expression-program execution
    (`JsOps.ToNumber`, `TypedAstEvaluator.NegateValue`,
    `TypedAstEvaluator.BitwiseNot`, `JsOps.ToJsString`, and
    `JsOps.StrictEquals`) and check `context.ShouldStopEvaluation` after
    coercive helper calls. `TypeOfIdentifier` may route only for
    activation-resolved names; unresolved `typeof` identifiers still decline as
    dynamic lookup. Production invocation must mark root lexical activation
    slots as `JsValue.Uninitialized` from `ActivationSlotShape` before
    parameter population, and VM `LoadSlot` / `TypeOfIdentifier` must preserve
    TDZ `ReferenceError` behavior with slot names when available.
    `EvaluateAndDiscard` support must compile the supported expression first
    and then append `Pop`, not skip side effects or abrupt completions. WHY:
    issue
    `planitem-planmanual1779943568009120000-batch-1-shared-bytecode-surface-and-parall-b7fbd79613`
    / PR #2474 widened the primitive operator lane, then needed build-back
    repairs for expansion-contract opcode inventory and production lexical TDZ
    reads. The durable lesson is that broad primitive support is safe only when
    selector, compiler, VM semantics, public route proof, dynamic declines,
    TDZ setup, and contract docs move together.
25. When adding unified-bytecode call-target preparation, keep preparation
    bytecode-owned but non-executable for eval/construct and other
    unproven call families until a separate invocation slice owns
    receiver binding, direct-eval, construct/super, optional-call, and spread
    semantics end to end. The #2495 identifier-call slice and #2530 named
    member-call slice plus the #2531 computed member-call slice and the #2538
    receiver-aware integration slice are the narrow exceptions:
    activation-resolved identifier calls, direct named member calls with
    activation-resolved optional-free named receiver chains, and direct
    computed member calls with shallow activation-resolved receiver chains may
    execute through the VM-owned
    `CallInvocationBoundary` when their arguments are simple literal/slot
    operands, span-measured simple named/computed property reads, or
    span-measured simple array/object literals (since gh2705, ADR 0290), and
    computed member keys are also simple literal/slot operands.
    Issue #2676 / PR #2685 widened all admitted shapes to accept **spread
    arguments** (`f(...args)`, `obj.m(...args)`, mixed positional+spread
    `f(a, ...b, c)`): the `CallInvocationBoundary` operand is extended with a
    spread-mask reference (high bits = `SpreadMaskConstantIndex + 1`, zero
    means no spread); the compiler packs the mask and the VM flattens via
    `TypedAstEvaluator.EnumerateSpread` left-to-right before invoking.
    Spread is now a fully owned admitted shape; no new opcode was added and
    no AST/`ExecutionPlanRunner` fallback is used in the spread path.
    Executable identifier calls must still carry the caller
    `EvaluationContext` and active `JsEnvironment` into existing callable
    invocation helpers. Executable named member calls must keep the receiver on
    the stack, load the named callee from that receiver, and preserve the final
    resolved receiver as `this` for the invocation boundary; for accepted nested
    receiver chains such as `root.child.read()`, the final receiver is
    `root.child`, not `root`. Executable computed member calls must keep the
    final receiver on the stack, consume the computed key through the
    context-aware property lookup path, preserve key-conversion and nullish
    receiver ordering, and preserve that receiver as `this` for the invocation
    boundary. If an
    accepted program enters a block lexical scope before invoking the callable,
    the VM must maintain environment owners for the active slots so
    environment-aware and debug-aware callables observe the same scope chain as
    the existing expression-call path. Pair the route proof with regression
    coverage for parameter-passed and block-scoped debug-aware callables for
    identifier calls, final-receiver/`this` preservation for direct named
    member calls, optional/super call declines, and computed-key conversion
    side effects plus nullish receiver and non-callable callee errors for
    computed member calls;
    do not rely only on ordinary JavaScript return values. It is valid for the
    compiler to emit typed `UnifiedBytecodeCallTarget` records and
    `Prepare*CallTarget` opcodes for activation-resolved identifier/member
    calls (including spread-argument forms admitted by #2676), but all
    unproven production call routing must
    decline at `CallInvocationBoundary` and the VM must not call back into
    `ExpressionProgram`, `ExecutionPlanRunner`, AST evaluation, or a generic
    host-call fallback. Update
    `docs/unified-bytecode-expansion-contract.md` in the same slice for every
    new prep opcode or decline code. WHY: issue
    `planitem-planmanual1779943568009120000-batch-1-shared-bytecode-surface-and-parall-161f73f52d`
    / PR #2479 added the first shared call-target preparation lane, and the
    learn-stage drift guard immediately failed because the expansion contract
    missed `PrepareIdentifierCallTarget`; the durable lesson is that call prep
    can become a reusable bytecode surface only while invocation remains an
    explicit, documented production decline. Issue #2495 / PR #2501 then made
    the first identifier-call slice executable; review found the initial VM
    path invoked parameter-passed `__debug` without the caller environment or
    context, so the accepted repair threads the invocation environment into the
    VM and proves both parameter and block lexical debug-aware calls. Issue
    #2530 / PR #2534 then made the direct named member-call slice executable;
    the durable lesson is that `PrepareNamedCallTarget` may execute only when
    the VM preserves the receiver/callee stack contract and tests prove
    receiver-as-`this` behavior while computed, eval, super/private,
    optional, dynamic, and other unproven call families still decline. Issue
    #2531 / PR #2535 then made the direct computed member-call slice
    executable; the durable lesson is that `PrepareComputedCallTarget` may
    execute only when the VM preserves receiver-as-`this`, computed key
    conversion/order, nullish receiver errors, and non-callable callee errors
    while eval, construct/super, optional, private/super, dynamic,
    complex computed-key, and other unproven call families still decline.
    Issue
    `planitem-planmanual1779965179415360000-batch-1-receiver-aware-call-execution-boun-cffd4a813a`
    / PR #2538 integrated the named/computed member-call work after those
    slices landed separately. The durable lesson is that receiver-aware
    production calls must bind `this` to the final receiver in the accepted
    chain, keep computed nullish-receiver-before-key-coercion ordering, and
    prove optional and super call families still decline before VM execution.
    Issue
    `planitem-planmanual1779965179415360000-batch-1-receiver-aware-call-execution-boun-3cea46640b`
    / PR #2609 then widened named member-call receivers to arbitrary
    optional-free named-chain depth while deliberately keeping deeper named
    receivers followed by computed call targets declined. The durable lesson is
    that named-chain depth can widen with existing `GetNamedProperty` and
    `PrepareNamedCallTarget` opcodes, but deeper computed-member call neighbors
    require their own proof slice. Issue #2676 / PR #2685 then admitted
    **spread arguments** across all admitted call shapes by extending the
    `CallInvocationBoundary` operand with a packed spread-mask index (high
    bits = `SpreadMaskConstantIndex + 1`, zero means no spread). The durable
    lesson is that a new argument-passing form can extend the existing call
    boundary operand encoding rather than requiring a new opcode, provided the
    spread flattening reuses a shared helper (`EnumerateSpread`) and the no-mixed-
    execution rule is respected — no AST/IR fallback for the spread path.
    Optional calls, super calls, and direct eval continue to decline at their
    existing guards and are not affected by the spread admission. Non-spread
    plain-constructor calls (`new F(...)`) were admitted separately in issue
    #2690 / PR #2697 (rule #37); spread-onto-construct is now owned by the
    construct boundary rather than by call-boundary admission.
    Issue #2705 / PR #2719 widened admitted call argument expressions to
    include **span-measured simple array and object literals** (`fn([a, b])`,
    `fn({x: 1})`, `fn(a, [b, c])`). The `HasSimpleCallArguments` 1-op-per-
    argument pre-check (`callIndex - argsStartIndex == call.ArgumentCount`) is
    replaced with a span-walk that consumes each logical argument as either a
    single simple op or a measured literal span, verifying the logical argument
    count against `call.ArgumentCount` at the end (ADR 0290). The durable
    **backpatch lesson**: `PrepareNamedOptionalCallTarget` and
    `PrepareComputedOptionalCallTarget` encode the nullish short-circuit jump
    target in the upper 16 bits of the operand. Before this slice the target
    was precomputed ahead of argument emission as a fixed-offset formula
    (`unified.Count + ArgumentCount + 2`). With variable-length argument spans
    the post-argument PC is unknowable before emission; the fix is to emit the
    prepare opcode with a zero upper-half placeholder, compile arguments, then
    backpatch `unified[prepareIndex]` with
    `callTargetConstantIndex | (unified.Count << 16)`. Any future argument-form
    widening that admits variable-op spans must apply the same backpatch pattern
    for all optional call prepare opcodes; a fixed-offset formula will silently
    produce the wrong jump target when the actual argument span length differs
    from `ArgumentCount`.
    The super/call argument property-read slices showed that property reads
    inside invocation boundaries must be owned by the argument span walker, not
    treated as standalone first-boundary property reads. When admitting a
    property-read value position such as `fn(box.value)`, `fn(box["value"])`,
    `obj.m(box.value)`, or `super(items.length)`, update both the selector span
    walker and the compiler argument emitter so the read is consumed as one
    logical argument before `CallInvocationBoundary` or
    `SuperConstructInvocationBoundary`. The VM must still execute the existing
    `GetNamedProperty`/`GetComputedProperty` opcodes directly; do not add a
    callback to expression bytecode or the IR runner.
    Issue #2741 / PR #2745 extended span-measured arguments further to include
    **simple untagged template literals** (`` fn(`hello ${name}`) ``,
    `` fn(`static`) ``): `TryMeasureSimpleTemplateLiteralSpan` recognizes the
    compiler-emitted `LoadLiteral("") seed + text-part cycles +
    substitution-part cycles` shape. Because the seed `LoadLiteral("")` is
    syntactically identical to a plain string literal, the admission branch uses
    `spanLen > 1` to distinguish a real multi-op template span from a standalone
    string; without this check a bare `""` literal would incorrectly enter the
    template span path. Unlike array and object span admission, template literal
    wiring must also update the **named-write, compound-write, and binary-read
    RHS admission sites** (`TryIsFirstBoundaryPropertyWriteCandidate`,
    `TryIsFirstBoundaryNamedCompoundPropertyWriteCandidate`,
    `TryIsFirstBoundaryPropertyReadBinaryExpressionCandidate`) — not just
    `HasSimpleCallArguments`. Any future span helper for a multi-op value
    expression shape must be wired into all value-position admission sites in
    the same delivery slice. No backpatch is required for optional call prepare
    opcodes when the new shape is purely in argument value position. Issue
    #2807 / PR #2808 later admitted ordinary identifier/member tagged-template
    calls by lowering bare identifier tags through `LoadIdentifierCallTarget`,
    treating `LoadTemplateObject` as a call argument, and handling it in
    resumable execution through the same template-object cache as the sync VM.
    Keep super/private/exotic tagged-template targets outside that admission
    until they have dedicated selector/compiler/VM proof. Complex substitutions
    (`` `${a + b}` ``) decline because `a + b` is not a simple operand
    (ADR 0292).
26. When encountering stateful for-in or array-destructuring driver
    instructions in production unified bytecode eligibility, decline before VM
    execution until a full driver-state model is owned. `ForInInitInstruction`
    and `ForInMoveNextInstruction` must decline as
    `ForInDriverStateDependency`; array-destructuring init/element/rest/close
    instructions must decline as `DestructuringDependency`. Do not add partial
    driver-step opcodes, VM callbacks, or `ExpressionProgram` /
    `ExecutionPlanRunner` fallback to make one step executable before selector,
    compiler, VM, state lifecycle, close/abrupt behavior, positive route proof,
    adjacent no-route proof, and expansion-contract inventory all move
    together. WHY: issue
    `planitem-planmanual1779943568009120000-batch-1-shared-bytecode-surface-and-parall-447731fb5a`
    / PR #2486 found that the unified VM has no owned iterator/destructuring
    driver-state model yet. The delivery added explicit decline codes/tests and
    documented the model-first boundary instead of widening opcodes or VM paths
    opportunistically.
27. When admitting completion and expression-statement behavior into production
    unified bytecode, keep completion effects VM-owned and decline-first.
    Empty or implicit returns should compile to `ReturnUndefined`; non-awaited
    `ThrowInstruction` payloads should compile to owned expression operations
    followed by `Throw`; and VM `Throw` must set `EvaluationContext` throw
    state so caller-side JavaScript `try`/`catch` remains observable. For
    `EvaluateAndDiscard`, compile only supported owned expression operations
    first and then append `Pop`; do not skip side effects, swallow abrupt
    completions, add an eval/fallback opcode, or route through
    `ExpressionProgram`, `ExecutionPlanRunner`, or AST evaluation. Removing a
    discarded-expression decline is valid only when the underlying operation
    family is already production-owned by selector, compiler, VM, and route
    proof. Keep adjacent call, dynamic lookup, optional-chain, `super`, delete,
    destructuring, for-in driver, and unowned computed-key families as pre-VM
    declines until a separate slice owns them end to end. WHY: issue
    `planitem-planmanual1779943568009120000-batch-1-shared-bytecode-surface-and-parall-eb801dff13`
    / PR #2488 admitted `ReturnUndefined`, `Throw`, and discarded property
    write/update side effects through production unified bytecode. The useful
    conflict-resolution lesson was to preserve sibling explicit for-in and
    destructuring model-first declines while removing only the redundant
    discarded property write/update veto that current owned VM semantics had
    superseded.
28. When admitting loop-control shapes to production unified bytecode, keep
    target semantics compiler-owned and label decline explicit. Supported
    unlabeled `BreakInstruction` and `ContinueInstruction` cases may compile
    only as resolved `Jump` targets through the same IR-instruction to
    bytecode-PC map used for ordinary jumps. Prove forward breaks, continue
    backedges, for-style update continue targets, and do-while branch
    consequent backedges with selector eligibility and public route-log tests.
    Labeled breakable control flow is now admitted (ADR 0285 / issue #2679, rule
    #36), and no label-specific decline bucket remains. Keep unsupported complex
    loop/control-flow shapes as pre-VM declines through the concrete
    driver-state or plan-shape gate that matches the failing topology.
    After widening compile support, update prototype expectations that used to
    assert old decline behavior so `make quality` catches drift before merge.
    WHY: issue
    `planitem-planmanual1779943568009120000-batch-1-shared-bytecode-surface-and-parall-9d6cd3060b`
    / PR #2489 widened production loop-control support. The
    conflict-resolution stage had to preserve main's for-in/destructuring
    declines while repairing stale prototype tests that still expected
    for-loop post-update shapes to fail.
29. When admitting block lexical scopes to production unified bytecode, make the
    accepted program own the full flat-slot span, not just root activation
    slots. Complete root activation mappings even when non-root flat mappings
    are present, derive `UnifiedBytecodeProgram.SlotCount` from every flat-slot
    id, preserve `ParameterSlotIndices` positions with `-1` sentinels for
    unused formals, and resolve scoped `let` / `const` declarations through the
    active scope flat mapping. `PushEnvironment` may stamp lexical TDZ slots as
    `JsValue.Uninitialized` and `PopEnvironment` may remain an owned VM
    cleanup opcode for the admitted linear shape, but neither opcode may create
    a `JsEnvironment`, do name fallback, or call back into
    `ExecutionPlanRunner`, `ExpressionProgram`, or AST evaluation. Keep
    `BindingVariableDeclaration`, destructuring, `with`, direct eval / dynamic
    lookup, captured activation, per-iteration bindings, and `using`
    declarations declined until a later slice owns those semantics end to end.
    WHY: issue
    `planitem-planmanual1779943568009120000-batch-1-shared-bytecode-surface-and-parall-2f1dcc1cd5`
    / PR #2490 widened the block lexical-scope lane, and the quality gate found
    that partial root remapping plus non-root flat mappings broke unrelated
    parameter-population paths. The accepted repair made slot layout
    program-wide and positional instead of activation-only or name-based.
30. After parallel unified-bytecode lanes have individually widened production
    support, prove they compose as one accepted production boundary before
    treating the batch as coherent. The integrated selector proof should combine
    only already-owned families, assert `None` decline code, assert required
    owned opcodes, and assert absence of non-executable call-target preparation
    or invocation-boundary opcodes. The matching public invocation proof should
    assert `unified-bytecode-production-fast-path` on the same function and
    expected JavaScript result. For admitted ordinary sync plans, keep
    production unified bytecode ahead of direct specialized simple-return binary
    and binary-chain shortcuts; those shortcuts are fallback paths only for
    shapes that do not pass production bytecode eligibility. Do not add VM
    fallback or broaden adjacent unowned families to make an integrated test
    pass. WHY: issue
    `planitem-planmanual1779943568009120000-batch-1-shared-bytecode-surface-and-parall-fc03ae9db9`
    / PR #2503 closed the production-routing integration slice with a guard-only
    proof pack. The durable lesson was that per-lane acceptance is not enough:
    completed lanes must compose inside one VM-owned program while route
    priority explicitly protects the bytecode-first production boundary.
31. Keep production unified-bytecode no-mixed-execution source gates both
    method-boundary-scoped and VM-scoped. The sync-invoker source gate should
    scan only `TryInvokeProductionUnifiedBytecode<TArgs>(...)`, because the
    surrounding invoker file legitimately owns fallback paths. The same test
    must also scan `UnifiedBytecodeVirtualMachine.cs` and forbid
    `ExecutionPlanRunner`, `ExpressionProgram`, `EvaluateExpression(`,
    `ProfileEvaluateExpression(`, and `EvaluateDynamicExpressionProgram(`.
    A gate that omits `ExpressionProgram` from the VM source can pass while
    leaving the mixed-expression bridge open. Repository-root discovery for
    source gates must work in normal checkouts and linked worktrees: accept a
    `.git` directory, a `.git` file, or the solution marker together with the
    expected `src/Asynkron.JsEngine` tree. WHY: issue
    `planitem-planmanual1779943568009120000-batch-1-shared-bytecode-surface-and-parall-a88c0a9ba1`
    / PR #2509 hardened the accepted-path proof after review caught two
    guardrail holes: linked-worktree root discovery and a missing VM-source
    `ExpressionProgram` assertion.
32. Before creating or rewriting follow-on Faktorial plans for unified
    bytecode, rebase lane order against current local `main`,
    `docs/unified-bytecode-expansion-contract.md`, relevant ADR/rule
    boundaries, and recent merged production proof. Do not carry a stale first
    slice forward just because an earlier plan or ADR named it. If the planned
    first slice is now current support, name it as baseline/already landed,
    move the first still-unsupported family to Batch 1, and update the ADR and
    Faktorial plan body together. WHY: issue
    `planitem-planmanual1779943568009120000-batch-1-shared-bytecode-surface-and-parall-833518a41c`
    / PR #2515 found the follow-on plan still started at direct identifier
    calls even though issue #2495 / PR #2501 had made no-spread
    activation-resolved identifier calls executable. The accepted correction
    rewrote ADR 0261 and plan `planmanual1779961785446650000` so named member
    calls became the first remaining lane, computed member calls second, and
    constructor/super, spread/direct eval, dynamic lookup,
    iterator/destructuring, and label families stayed deferred. Issue #2530 /
    PR #2534 has since landed named member calls, issue #2531 / PR #2535 has
    since landed direct computed member calls, issue #2679 / PR #2683 has
    since admitted labeled control flow (rule #36), and issue #2690 / PR #2697
    has since admitted non-spread plain-constructor calls (rule #37). Future
    plan edits must treat super calls, direct eval, dynamic lookup, and
    iterator/destructuring as remaining lanes unless current `main` proves an
    even newer slice has landed.
33. When preserving or widening with-backed dynamic names on the production
    unified bytecode route, keep the accepted program activation-hoist aligned,
    receiver-owned, and explicitly descriptor-gated for ordinary environment
    operations. The sync bridge must define function-scoped var bindings in the
    fast activation environment before VM execution so nested callees called
    from inside an outer `with` still see their own hoisted var names as
    `undefined` before any initializer runs. VM
    `PrepareDynamicIdentifierCallTarget` must resolve active with bindings
    regardless of identifier-cache state and must push the with binding object
    as the receiver when the identifier comes from that object. Outside active
    `with`, admit only the exact ordinary-environment dynamic-name opcode
    family that the bridge explicitly enables:
    `LoadDynamicIdentifier`, `StoreDynamicIdentifier`,
    `UpdateDynamicIdentifier`, `TypeOfDynamicIdentifier`,
    `DeleteDynamicIdentifier`, `ResolveDynamicIdentifierReference`,
    `LoadDynamicIdentifierReference`, and
    `StoreDynamicIdentifierReference`. Direct eval source execution and any
    dynamic lookup that depends on eval-created declarations, captured or
    materialized activation, arguments objects, or async/generator machinery
    remain pre-VM declines. Pair retained changes with the focused
    `Statements_with` Test262 row, public production invocation tests for both
    hoisted-var shadowing and with-object receiver binding, and a no-route
    proof that direct-eval-created declaration lookup still falls back. WHY:
    issue #2564 / PR #2571 fixed `S12.10_A1.11_T5` after the with-backed
    production route failed to create a nested function's hoisted local `value`
    binding before dynamic lookup and dynamic identifier call preparation still
    depended on the identifier-cache path. Issue
    `planitem-planmanual1780240661926543000-burn-down-unified-bytecode-production-decl-409e9e2030`
    / PR #2872 then widened the same family to ordinary dynamic-name
    environment operations while keeping direct-eval declaration materialization
    out of the production route.
34. When admitting `try/catch/finally` exception regions to production unified
    bytecode, keep exception routing and abrupt-completion propagation
    descriptor-backed and VM-owned. `EnterTry`, `EnterCatch`, `LeaveTry`, and
    `EndFinally` must compile to owned opcodes with `UnifiedBytecodeTryDescriptor`
    and `UnifiedBytecodeCatchDescriptor` payloads; the VM must own `TryFrame`,
    `PendingCompletion`, catch binding activation/inactivation, and finally
    replacement semantics without calling back into `ExecutionPlanRunner`,
    `ExpressionProgram`, or AST evaluation. For loop control through finally,
    compare compiled driver descriptor topology and mapped break/continue
    targets; do not decide whether to schedule an outer synthetic for-of finally
    from currently active driver-state slots alone, because an inner iterator can
    already be closed when its pending break reaches an outer frame. When
    `HandleContextThrow` resumes execution in the same VM instance, clear the
    operand stack back to the handler-owned baseline before continuing; a
    handled throw from call/construct/super preparation or cleanup must not
    leak receiver/callee/argument temporaries into the resumed path. Pair the
    route proof with catch binding leak/direct-read regressions, return/throw
    replacement through finally, break/continue through finally, nested for-of
    inner-break cleanup ordering, handled invocation-boundary throws, and
    unsupported async/generator/dynamic declines. WHY: issue
    `planitem-planmanual1779965179415360000-batch-1-receiver-aware-call-execution-boun-0bfc08d573`
    / PR #2591 admitted ordinary synchronous exception regions, then build-back
    fixes exposed catch-slot lifetime, operand-stack cleanup, pending body-throw
    preservation, and nested driver-cleanup topology as the durable guardrails.
    Issue
    `planitem-planmanual1780240661926543000-burn-down-unified-bytecode-production-decl-409e9e2030`
    / PR #2872 later hit the same contract from a handled call-boundary throw:
    the repair reset the unified-bytecode stack after `HandleContextThrow` so
    `ThrowBugTests.AssertThrowsPattern_ShouldCatchErrorObject` no longer
    overflowed the fixed operand stack.
    For the **resumable** route, do not admit `EnterCatch` / `PopEnvironment`
    by allowlist alone. The resume state must own active try frames, catch-used
    state, thrown values, pending finally completion, and inactive catch-binding
    slots across yield/await boundaries. Try-body and catch-body suspension may
    route when that state is represented on `UnifiedBytecodeResumeState`;
    suspending `finally` cleanup and destructuring catch bindings must remain
    pre-VM declines until their cleanup/binding state is owned. WHY: issue
    `planitem-planmanual1780639098493226000-full-unified-bytecode-execution-burndown-b-ba3b90f110`
    / PR #3226 admitted simple/optional resumable try-catch by replacing the
    old descriptor-index/resume-target arrays with resumable try frames and by
    persisting inactive catch-binding slots, while keeping suspending finally
    and destructuring catch bindings out of the route. Related ADR:
    `docs/adrs/0332-admit-resumable-try-catch-with-owned-frame-state.md`.
    Red-main follow-up issue #3228 / PR #3232 refined that contract: compiled
    `PopEnvironment` must preserve the source `ScopeId` and mark catch slots
    inactive only when the popped scope matches the active catch descriptor;
    slot writes that initialize the catch binding must clear the inactive flag;
    and throws produced while resuming iterator/delegation helper steps must be
    routed through `TryHandleResumableAbruptCompletion` before the VM returns a
    throw step to the generator caller. A resumable try/catch fix is incomplete
    if it proves only direct `throw` statements and not helper-produced throws
    plus catch-scope cleanup across suspension.

35. When removing a pre-gate from `CanUseProductionUnifiedBytecodeFastPath`,
    verify that the plan-level decline taxonomy in
    `UnifiedBytecodeProductionEligibility.TryFindExpressionDecline` already
    covers every opcode family the pre-gate was blocking; do not remove a gate
    whose declined case has no corresponding plan-level boundary. The
    `_homeObject is not null` pre-gate is the concrete reference: all super-
    property opcode families (`GetNamedSuperProperty`, `GetComputedSuperProperty`,
    `SetNamedSuperProperty`, `SetComputedSuperProperty`,
    `UpdateNamedSuperProperty`, `UpdateComputedSuperProperty`,
    `EnsureSuperReference`) were already declined by `SuperPropertyDependency`,
    making the gate redundant. The remaining pre-gates
    (`_lexicalThisEnvironment is not null`, `_superConstructor is not null`,
    `_superPrototype is not null`) correspond to capability gaps the plan-level
    taxonomy does not fully cover and must not be removed without a matching
    plan-level decline or proven VM support. When admitting `this`-using class
    or object-literal methods through the fast path, note that the property-
    write boundary still applies: simple `this.prop = slot/constant` assignments
    are within boundary, but compound read-modify-write patterns such as
    `this.prop = this.prop + n` are declined by the existing
    `PropertyWriteDependency` rule and must not be used as fast-path invocation
    proof in tests. When writing invocation proof tests for class methods or
    object-literal methods, assert `func=<anonymous>` in the production log — not
    the JavaScript method name — because class method AST nodes carry no `Name`
    field; asserting the JS identifier will silently pass the wrong log line or
    fail on the correct one. WHY: issue #2633 / PR #2643 found that removing the
    `_homeObject` pre-gate was safe exactly because `SuperPropertyDependency`
    already covered every super opcode family; the build-review process then
    surfaced the `func=<anonymous>` naming trap and the `PropertyWriteDependency`
    boundary as two further durable guardrails for future `this`-using method
    admissions.

36. When admitting `this`-dependent async/generator functions to the
    **resumable** unified bytecode route, clear both gates and own the `this`
    lifetime on resume state. The resumable route declines `this` in two
    independent places, and both must move together: remove the
    `HasThisDependency` decline from
    `UnifiedBytecodeProductionEligibility.EvaluateResumable`, AND add
    `UnifiedBytecodeOpCode.LoadThis` to the resumable-supported opcode set in
    `TryFindUnsupportedResumableOpcode`. (The async invoker sets
    `HasThisDependency`, so the eligibility decline blocks async methods with a
    receiver; the sync generator invoker never sets it, so the opcode allowlist
    is the only gate keeping `this`-using generators safe — clearing one without
    the other leaves a half-open boundary.) `this` must live on the long-lived
    `UnifiedBytecodeResumeState` (captured at construction, alongside slots /
    operand stack / program counter), NOT as a per-step VM parameter like the
    non-resumable `Execute`, because it has to survive suspension/resume across
    `yield`/`await`. Add a `case LoadThis:` to `ExecuteResumable` that pushes
    `state.ThisValue`, mirroring the non-resumable `Execute` `LoadThis`. Coerce
    in the invoker before VM entry via the shared static
    `CoerceThisValueForNonStrict` (promoted out of `SyncFunctionInvoker`) so
    strict/sloppy `this` is byte-for-byte identical to the sync route, and the
    resumable `LoadThis` always loads the pre-coerced value. Keep
    `LoadNewTarget` out of the resumable allowlist and keep new.target,
    captured/dynamic activation, arguments-object, call, dynamic-lookup, and
    async-generator shapes declining before VM execution. Only bare `this`
    flowing through resumable-supported opcodes (`LoadThis`, `Yield`, `Binary`,
    `Return`) is admitted; `this.x` property reads and `typeof this` stay
    outside the resumable opcode set and decline independently. Prove
    strict/sloppy primitive `this` fidelity, `this` after both `yield` and
    `await` suspension, and adjacent new.target / arguments-object declines
    (async + generator). WHY: issue #2675 / PR #2680 widened the resumable route
    as the counterpart to the sync `this` support (rule #34, #2633/#2643). The
    durable lesson is that the resumable route's two-gate structure
    (eligibility-decline + opcode-allowlist) and the resume-state `this`
    lifetime are different from the sync route's single pre-gate; a future
    resumable widening (e.g. `this.x` reads, new.target) must clear both gates
    and pick the resume-state lifetime, not the per-step VM-parameter lifetime.

36a. When admitting `super` property access to the **resumable** unified
     bytecode route, move the activation metadata, opcode allowlist, VM
     handlers, and proof pack together. A class method's home-object/super
     activation facts must be captured on `UnifiedBytecodeResumeState` and
     reused on each `ExecuteResumable` step; do not reconstruct them through a
     per-step sync-invocation shortcut and do not add a resumable VM fallback.
     For async generators, distinguish slot storage from the method
     environment used by `super` lookup: simple slots may remain on the
     synthetic resumable environment, but `UnifiedBytecodeResumeState.CallingEnvironment`
     must point at the runner-owned method environment when the accepted
     program contains resumable super operations.
     Add or remove the super-property opcode family as a coupled set:
     `EnsureSuperReference`, `GetNamedSuperProperty`,
     `GetComputedSuperProperty`, `SetNamedSuperProperty`,
     `SetComputedSuperProperty`, `UpdateNamedSuperProperty`, and
     `UpdateComputedSuperProperty` must be covered by the resumable opcode
     allowlist, matching `ExecuteResumable` handlers, expansion-contract
     inventories, and focused route tests. Keep unowned super invocation,
     construct, private/exotic neighbors, and delete-super shapes as pre-VM
     declines until those semantics are VM-owned. For class-method route-log
     tests, assert the resumable route marker and activation shape without
     relying on the JavaScript method name; class methods may log as
     `func=<anonymous>`. WHY: issue
     `planitem-planmanual1780639098493226000-full-unified-bytecode-execution-burndown-b-5219b4f05e`
     / PR #3187 found that async-generator resumable super reads needed the
     runner-owned method environment threaded into
     `UnifiedBytecodeResumeState.CallingEnvironment`; the resumable slot
     environment alone was not the right home-object/super binding source.
     Issue
     `planitem-planmanual1780639098493226000-full-unified-bytecode-execution-burndown-b-a28e4dff02`
     / PR #3188 admitted resumable super property reads/writes/updates by
     wiring the activation state, opcode allowlist, and VM handlers together,
     while the final B14 proof pass (`49bfd4169`) added super compound/update
     coverage and aligned route-log assertions with the existing anonymous
     class-method behavior. Related ADR:
     `docs/adrs/0325-admit-resumable-super-property-access-through-owned-resume-state.md`.

37. When admitting label-dependent control flow to production unified bytecode,
    treat labels as compiler-owned targets, not a source-syntax permission.
    The old label-specific eligibility decline was removed together with the
    compiler's labeled-breakable-enter gate (`IsSupportedBreakableEnter` plus
    `HasLoopContinueTarget` for labeled loop continue/break metadata). A labeled
    construct routes whenever its *unlabeled* IR topology would route — the
    canonical-loop topology checks (condition-first backedge, for-style update
    continue, do-while consequent, single-pass driver loops) still gate which
    shapes are eligible. Labels resolve to numeric targets in the plan builder
    (`ControlFlowEmitter`), so the compiler already sees a fully resolved jump
    target through `TryAppendResolvedJump` regardless of label presence; the VM
    needs no new opcode. The correctness boundary is **driver cleanup**: the VM
    closes only the single driver whose descriptor `BreakTarget` equals the
    abrupt jump target (`CleanupDriverStatesForBreakTarget`). A labeled
    `break`/`continue` that exits nested iterator/for-in driver loops must be
    handled by compiler-owned cleanup topology, not a source label decline. Do
    **not** substitute a program-counter ordering heuristic for multi-driver
    cleanup: it was prototyped and empirically rejected here
    because the compiler's lazy target compilation does not guarantee an inner
    loop's exit PC precedes its enclosing loop's exit PC, so PC-ordering closed
    the wrong driver set (outer closed, inner leaked). Multi-driver labeled
    cleanup is the next loop-control frontier and needs nesting metadata on
    driver descriptors, not a PC heuristic. Move stale prototype decline
    expectations in the same delivery (labeled while/non-loop `TryCompile` cases
    now assert success; the `UnsupportedControlFlowFunctions` labeled entry moves
    to `SupportedLoopControlFunctions`) so `make quality` stays meaningful, and
    sync the expansion contract bucket #5 + Production Loop-Control Boundary and
    roadmap in the same slice (rule #21). Pair proof: selector acceptance for
    labeled loop / labeled break-out-of-for-of / labeled single-loop continue,
    labeled block/loop route-log invocation proofs, driver-close-on-labeled-break
    proof, and negative driver-crossing decline proofs for both labeled `break`
    and labeled `continue`. WHY: issue #2679 / PR #2683 cleared the contract's
    "Ranked Next Unsupported Buckets" #5. The durable lesson is twofold: (1)
    label decline was a source-syntax veto layered over an already-resolved
    numeric-target path, so admitting it is mostly removing two gates plus one
    narrow soundness decline; (2) the single-level VM cleanup model — not the
    label itself — is the real boundary, and a PC-based multi-driver shortcut is
    unsound under lazy target compilation.

    Follow-up issue
    `planitem-planmanual1780240661926543000-burn-down-unified-bytecode-production-decl-9e9f5025f7`
    / PR #2914 superseded this rule's single-level cleanup boundary for
    synchronous nested iterator/for-in drivers by adding descriptor-backed
    `MoveNextTarget` topology. Keep this paragraph as the historical ADR 0285
    boundary and apply rule #53 for the current nested-driver cleanup model.

38. When admitting synchronous construct and super invocation to production
    unified bytecode, keep constructor and super ownership split and prove the
    activation contract in the same slice. `ConstructInvocationBoundary` stays
    receiver-free and mirrors the spec-conformant construct reference helper
    (`ExecuteProgramConstruct`): the constructor is pushed as a plain value
    load, `[[Construct]]` runs with the constructor as both the target and
    `new.target`, spread flattening stays inside the boundary, and member or
    computed constructor targets may reuse already owned property-read opcodes
    without binding a receiver as `this`. Super invocation is a narrower,
    separately owned lane: only non-spread derived-constructor `super(...)`
    plus named/computed super-member calls may route, and only when
    `SyncFunctionInvoker` can create the required call environment and super
    binding itself. The VM must own `SuperConstructInvocationBoundary`,
    `PrepareNamedSuperCallTarget`, and `PrepareComputedSuperCallTarget`;
    resolve the super binding there; preserve the derived receiver / `this`
    contract for super-member calls; and preserve the existing derived-
    constructor completion rules (`new.target` propagation, double-super guard,
    object-or-undefined return rule). Do not hide super under generic
    call-target preparation, AST fallback, or a partial activation shortcut.
    If the route still needs instance fields, private scopes, spread super
    constructs, super property reads/writes/updates, or other unproven
    activation metadata, decline before VM entry. The principle remains:
    **activation-gate unreachability is a proof-pack blocker**. VM semantics
    for a function kind are only admissible once the invoker can supply the
    matching activation contract and the route is demonstrably reachable in the
    same proof pack. Pair construct/super admission with proof for
    `new.target` propagation, construct argument order, not-a-constructor
    `TypeError`, derived-constructor `super(...)` initialization and
    double-super behavior, super-member receiver-as-`this` behavior, and the
    retained spread/instance-field/super-property declines. WHY: issue #2690 /
    PR #2697 recorded the original activation-gated super decline in ADR 0286.
    Issue `planitem-planmanual1780240661926543000-burn-down-unified-bytecode-production-decl-d41d47d2fc`
    / PR #2862 then widened the invoker, selector, compiler, and VM together so
    the first bounded super invocation shapes became reachable and provable
    without mixed execution.

39. When admitting a per-iteration TDZ head environment (`for (const/let x of/in …)`) to production unified bytecode, audit all three eligibility gate layers simultaneously — not just the first declining gate. The per-iteration TDZ head shape touches independent gates in series: (1) **`IsSupportedPushEnvironment`** by default declines any non-empty `PerIterationBindings`; admit it when all lexical slots resolve to flat activation slots. (2) The **structured loop-reconstruction gates** (`IsSupportedDriverLoopBackEdgeTarget`, `TryIsLinearCanonicalWhileBody`) — a driver loop with a per-iteration environment emits `PopEnvironment` as the back-edge source and has `Push`/`PopEnvironment` in the body; accept `PopEnvironment` as a valid back-edge source and treat the pair as linear flat-slot pass-throughs while still requiring a branch-free body to preserve no-mixed-execution. (3) The **production opcode allowlist** — a new opcode such as `TdzHeadInit` must be explicitly added to the admitted production subset even when the opcode and its VM handler already exist from a prior prototype slice. Emit `TdzHeadInit` **before** the iterable/object source expression ops so the TDZ is established before the source is evaluated (making `for (const x of [x])` throw the correct `ReferenceError`). The safety boundary is: captured/dynamic activations decline wholesale upstream, so the only case where per-iteration binding freshness is observable (a closure over the loop binding) never reaches this path — prove this with a negative routing test. When a new driver-state or per-iteration environment shape is admitted, assume it touches multiple gate layers and test each layer independently before claiming the shape is fully admitted. WHY: issue #2678 / PR #2687 admitted the per-iteration TDZ head shape (Slice A, ADR 0288). The first attempted fix targeted only one of three blocking gates; all three had to move together before the failing test passed. The useful lesson is that eligibility for a stateful driver shape is spread across the environment-support gate, the loop-reconstruction analysis, and the per-opcode allowlist — a shape that looks like a one-line eligibility change often requires synchronized updates across all three surfaces.

40. Keep `ApplyBinaryOperator` in `UnifiedBytecodeVirtualMachine` exhaustive over every `BinaryOperator` enum member. When new enum members are added, or when a production-eligible operator subset is widened, ensure each remaining unsupported operator still has an explicit case arm that declines at eligibility as `UnsupportedPlanShape` rather than silently falling through to a default clause. For operators whose canonical evaluation path is `bool`-returning on `TypedAstEvaluator` (such as `In` and `InstanceOf`), add a `JsValue`-returning internal wrapper in `TypedAstEvaluator.JsValue.cs` in the `#region Public API for JsOps` section before wiring the VM arm — mirror the existing `BitwiseAnd`/`Power` pattern: the wrapper calls the underlying `bool`-returning method and returns `JsValue.True` or `JsValue.False`. When widening `IsProductionBinaryOperator` in `UnifiedBytecodeProductionEligibility.cs`, also update `IsSupportedBinaryOperator` in `UnifiedBytecodeCompiler.cs` to match — these two gates are coupled but live in separate files. If the compiler gate is not widened in sync, the eligibility check passes but the compiler falls through to its default "Unsupported expression op 'Binary'" error at runtime, requiring a second build pass to fix. Treat the binary-operator widening checklist as four simultaneous updates: (1) `IsProductionBinaryOperator` in the eligibility file, (2) `IsSupportedBinaryOperator` in the compiler, (3) `ApplyBinaryOperator` case arms in the VM, (4) `FormatBinaryOperator` in `UnifiedBytecodeProductionEligibility.cs` — replace any wildcard `_ => binaryOperator.ToString()` arm with `_ => throw new ArgumentOutOfRangeException(nameof(binaryOperator), binaryOperator, null)` so unhandled operators surface immediately at runtime and CS8524 (unnamed enum value) is suppressed without hiding real gaps. A wildcard `ToString()` arm silently returns a formatted string for future unhandled operators and defeats the compiler's exhaustiveness signal. WHY: issue `planitem-planmanual1780157100924814000-baseline-batch-1-value-binary-operator-wid-30e0eb731c` / PR #2730 found 10 missing arms in the VM switch (`Power`, `NotEqual`, `BitwiseAnd`, `BitwiseOr`, `BitwiseXor`, `LeftShift`, `RightShift`, `UnsignedRightShift`, `In`, `InstanceOf`). Issue `planitem-planmanual1780157100924814000-baseline-batch-1-value-binary-operator-wid-a079ef8fec` / PR #2731 found the coupled compiler gate (`IsSupportedBinaryOperator`) was not updated alongside the eligibility gate, causing a verification failure that required a second build pass to fix. Issue `planitem-planmanual1780157100924814000-baseline-batch-1-value-binary-operator-wid-b71305a0ac` / PR #2734 found the `FormatBinaryOperator` wildcard arm `_ => binaryOperator.ToString()` still in place after the other three surfaces were updated; the accepted repair replaced it with a throw, making all 25 `BinaryOperator` cases explicit. Without keeping all four surfaces in sync, production widening silently produces wrong, missing, or misleadingly formatted behavior.

41. When extending an existing array-literal span family with a new push-like opcode variant (e.g., adding `ArraySpread` alongside `ArrayPush`), treat it as **four coupled surfaces** that must all move together in the same delivery slice:
    1. **Compiler main switch** (`UnifiedBytecodeCompiler` expression switch): add a case for the new `ExpressionOpKind` value to emit the corresponding `UnifiedBytecodeOpCode`. The span helper (`TryAppendSimpleArrayLiteralSpan`) emits the opcode only inside a recognized literal span; any `ArraySpread` op encountered by the general compiler switch falls to `default` and returns "Unsupported expression op" at runtime.
    2. **Production opcode allowlist** (`TryFindPrototypeOnlyOpcode`): add the new `UnifiedBytecodeOpCode` to the production-eligible subset. A missing entry causes the post-compile opcode-subset check to reject programs that contain the new opcode even when eligibility and compilation succeeded.
    3. **Span helper pair** (eligibility's `TryMeasureSimpleArrayLiteralSpan` + compiler's `TryAppendSimpleArrayLiteralSpan`): update the push-op kind check from an exact equality to `is (existing or new)` so both measurement and emission accept the new variant. When admitting `ArraySpread`, also check whether `ArrayPushHole` (the zero-argument hole-element op) needs standalone admission in `TryMeasureSimpleArrayLiteralSpan` — hole+spread patterns (`[, ...a]`) emit `ArrayPushHole` before `ArraySpread` in the op sequence, so without standalone `ArrayPushHole` acceptance the span measurement fails and the spread admission is incomplete. If `ArrayPushHole` is already recognized as a push element, no change is needed; otherwise add it in the same slice.
    4. **Expansion contract** (`docs/unified-bytecode-expansion-contract.md`): add the new opcode name to the current opcode inventory in the same slice. A missing entry fails the contract-coverage drift-guard test.

    Additionally: when a non-simple source precedes an `ArraySpread` op in an expression program, the main `TryFindExpressionDecline` left-to-right loop will encounter the source's inner ops (e.g., a `Call` in `a.slice(0, b)`) before reaching the `ArraySpread` op and may fire a more generic decline code (`CallDependency`) instead of the intended `ObjectLiteralOrSpreadDependency`. Fix this by adding a **pre-scan** before the main loop that detects `ArraySpread` ops whose immediately-preceding op is non-simple and returns `ObjectLiteralOrSpreadDependency` immediately. The pre-scan must run before the general op-by-op loop so that the specific spread-source decline supersedes any inner-op decline from the source expression. WHY: issue `planitem-planmanual1780157100924814000-baseline-batch-3-array-spread-in-array-lit-300d522431` / PR #2748 wired `ArraySpread` in the span helper first but required a build-back repair to add (1) the compiler main switch case, (2) the production allowlist entry, (3) the docs contract entry, and (4) the pre-scan for non-simple spread sources. Without all four surfaces aligned, eligibility succeeds but the compiler falls through to its default error; or the compiler emits the opcode but the allowlist check rejects it at post-compile validation; or a non-simple spread source produces the wrong decline code for the caller. Confirmed by issue `planitem-planmanual1780157100924814000-baseline-batch-3-array-spread-in-array-lit-389e8f1c98` / PR #2750 (the main array spread delivery), which applied all four surfaces plus standalone `ArrayPushHole` admission in a single slice with no build-back repair needed.

42. When admitting expression-level short-circuit logical (`&&`, `||`) and
    nullish-coalescing (`??`) operators to production unified bytecode, use
    **peek-semantics** jump opcodes — not the existing pop-semantics
    `JumpIfFalse`. The peek/pop distinction is load-bearing:

    - Statement-level `JumpIfFalse` (used for `if`/`while` conditions) consumes
      TOS on the taken branch because the condition value is not part of the
      statement result.
    - Expression-level short-circuit jumps must leave TOS intact on the
      taken branch because the LHS value IS the expression result when the
      short circuit fires (`a && b` returns `a` when `a` is falsy).

    Three distinct opcodes own this: `JumpIfShortCircuitFalse` (for `&&`),
    `JumpIfShortCircuitTrue` (for `||`), and `JumpIfShortCircuitNotNullish`
    (for `??`). None decrement `stackPointer`. The compiler emits placeholder
    operands for these forward jumps in `TryAppendExpressionProgramOps` and
    backpatches the targets using an `exprPcToUnifiedPc[]` map after the full
    expression op sequence is emitted.

    `JumpIfShortCircuited` (optional chain `?.`) remains declined as
    `OptionalChainDependency` unless a later optional-chain proof slice owns the
    exact embedded shape. Do not admit optional-chain forms by extending the
    short-circuit jump opcodes alone; the semantics (nullable receiver,
    optional member lookup, optional call) require a separate proof slice.

    **Dual-dispatch completeness**: When adding new VM opcodes, they must appear
    in BOTH the sync `Execute` dispatch switch AND the `ExecuteResumable` dispatch
    switch. Omitting a case from either switch causes `InvalidOperationException`
    at runtime for any function kind that routes through the omitted path. The
    quality gate catches this — but the structural check to avoid it is to grep
    for the opcode name in `UnifiedBytecodeVirtualMachine.cs` after adding it and
    confirm two `case` entries exist (one in each switch). Bounded optional-read
    operands in logical/nullish expression programs are admitted only through
    the dedicated optional-read span rules in #2851 / ADR 0305; the
    short-circuit jump opcodes themselves do not authorize broader optional
    chains.

    **JsArray result indexing in invocation tests**: When asserting array elements
    from a JS return value in invocation tests, do not use `((dynamic)result)[N]`
    — `JsArray` does not expose an integer indexer via dynamic binding and the
    assertion silently fails or throws. Use the canonical pattern:
    `Assert.IsType<JsTypes.JsArray>(result).Items[N]`.

    WHY: issue
    `planitem-planmanual1780157100924814000-baseline-batch-5-logical-and-nullish-opera-f2c1e6c23b`
    / PR #2761 admitted `&&`, `||`, `??` via three peek-semantics opcodes. The
    durable lesson is that reusing `JumpIfFalse` for the expression short-circuit
    branch would silently consume the LHS result value, producing an incorrect
    `undefined` on the taken path. A future admission of expression-level
    conditional logic must classify each new jump variant by its stack effect
    before deciding whether to reuse an existing opcode or introduce a new one.
    Issue
    `planitem-planmanual1780157100924814000-baseline-batch-5-logical-and-nullish-opera-e15078d09e`
    / PR #2762 then surfaced two build-back repairs: the three new opcodes were
    added to `ExecuteResumable` but missed from the sync `Execute` switch
    (causing `InvalidOperationException` on production invocations), and six
    initial invocation tests used `((dynamic)result)[N]` which `JsArray` does
    not support via dynamic integer indexing.
    Issue
    `planitem-planmanual1780157100924814000-baseline-batch-5-logical-and-nullish-opera-30c56defe6`
    / PR #2766 then revealed two follow-on gaps when this-property LHS shapes
    (`this.enabled && b`) were added to the test suite:
    (a) **Eligibility boundary helper scope**: `TryIsFirstBoundaryNamedPropertyReadCandidate`
    validates the *entire* expression program as a property-read shape; when
    `GetNamedProperty` is followed by `JumpIfShortCircuitFalse` instead of another
    property op, the helper correctly declines it as a non-standalone read — but
    that rejection incorrectly blocked the LHS of a short-circuit operator. The fix
    is `TryIsNamedPropertyReadAtLogicalShortCircuitBoundary`: accepts
    `GetNamedProperty` at index `operationIndex` when (i) `ops[0..operationIndex]`
    form a valid activation-resolved named read chain and (ii) `ops[operationIndex + 1]`
    is one of the three short-circuit jump opcodes. Construct-target member reads
    (`new box.Ctor()`) are unaffected because they are followed by `Construct`, not
    a jump op.
    (b) **Compiler general-loop gap**: the compiler's general expression-op loop had
    no case for `GetNamedProperty`, so expressions that passed the new eligibility
    gate still failed at compile time with "Unsupported expression op". The fix adds
    `GetNamedProperty` to the general compiler loop with non-optional and
    non-private guards.
    The durable lesson from PR #2766: when an admitted operator family is initially
    tested with slot/slot operands only, literal-right and this-property-left shapes
    must follow in the same slice or as a tracked immediate follow-up, because
    this-property LHS shapes expose eligibility and compiler gaps that slot/slot
    shapes cannot reveal.
    Issue `autrun-diwb8ex5sizk-189ab69a31` / PR #2773 completed the this-property
    short-circuit admission by adding a **dedicated helper pair** for the full
    `[activation-resolved base, GetNamedProperty+, JumpIfX, Pop, simple-rhs]`
    expression program shape: `TryIsFirstBoundaryPropertyReadShortCircuitExpressionCandidate`
    (eligibility) and `TryAppendFirstBoundaryPropertyReadShortCircuitExpression`
    (compiler), resolving 9 pre-existing `ThisPropertyLeft` test failures. Two
    design decisions distinguish this helper from the analogous binary-expression
    helper (`TryIsFirstBoundaryPropertyReadBinaryExpressionCandidate`):
    (a) **RHS is restricted to a single simple operand** — multi-op spans (array,
    object, template literals) are not admitted in the RHS position. A single-op
    RHS makes the backpatch trivial: emit the jump placeholder, emit RHS, patch
    the jump to `unified.Count`. A multi-op span RHS would require measuring span
    length before the jump placeholder is emitted; that widening is a future slice.
    (b) **Jump target validation is exact** — eligibility requires
    `jumpOp.Target == expressionProgram.OperationCount`, ensuring the short-circuit
    jump exits at the end of the expression program. Any program where the jump
    target is interior is declined rather than speculatively compiled.
    `TryIsNamedPropertyReadAtLogicalShortCircuitBoundary` (from PR #2766) remains
    in use for boundary-candidate probes that are not standalone short-circuit
    return expressions; this helper pair does not replace it. See ADR 0295.
    Issue
    `planitem-planmanual1780198120145433000-widen-unified-bytecode-production-conditio-ece8f107eb`
    / PR #2851 then narrowed `OptionalChainDependency` for operands inside
    already-owned control expression programs. The durable lesson is that the
    owning control operator and the embedded optional-read span must both be
    proven. Eligibility may admit an optional named/computed read span only
    after detecting an owned control expression (`JumpIfFalse`, `JumpIfTrue`,
    `JumpIfNotNullish`, or `JumpIfConditionalFalse`) and only when the span is
    activation-resolved, non-private, bounded to the existing optional-read
    lowering shapes, and backed by opcode-shape assertions. Do not use this
    operand admission to widen optional writes, optional calls, complex computed
    keys, dynamic lookup, or unbounded optional-chain forms.

43. When admitting simple (non-chained) optional member reads to production
    unified bytecode, use **dedicated null-check opcodes** and constrain
    admission to exact op-count shapes. The two admitted forms:

    - **`a?.b` (named, 2 ops total)**: `GetNamedPropertyOptional` encodes the
      null-check inline with the property read. Eligibility requires
      `IsOptional:true`, `ShortCircuitOnNullishTarget:false` (not a second
      hop in a chain), non-private, and an activation-resolved base.
    - **`a?.[k]` (computed, 4 ops total)**: `JumpIfNullishReplaceUndefined` +
      simple key load + `GetComputedProperty` implements the jump-over
      pattern. Eligibility requires `ReplaceWithUndefined:true`,
      `ShortCircuitOnNullishTarget:false` on the final computed op, and an
      activation-resolved base.

    `JumpIfNullishReplaceUndefined` is not the same as `JumpIfShortCircuited`
    (the optional-chain chaining sentinel used in forms like `a?.b?.c` where
    the second hop carries `ShortCircuitOnNullishTarget:true`).
    `JumpIfNullishReplaceUndefined` replaces TOS with `undefined` and jumps
    atomically; both the taken branch and the fall-through branch leave
    exactly one value on the operand stack.

    Named multi-hop optional chains (`a?.b.c`, `a?.b?.c`) are admitted by
    PR #2804 / ADR 0298 via jump-based lowering — see lesson below. Computed
    multi-hop chains (`a?.[k]?.b`, `a?.b?.[k]`), assignment targets, and
    super-optional forms still retain `OptionalChainDependency`.
    `OptionalChainDependency` is **narrowed, not removed**: admitted named-chain
    forms lift the decline; genuinely unsupported forms retain the explicit
    decline reason.

    Apply dual-dispatch completeness (rule #41): new opcodes must appear in
    both the sync `Execute` switch and the `ExecuteResumable` switch;
    omitting either causes `InvalidOperationException` at runtime.

    WHY: issue gh2771 / PR #2777 admitted simple optional member reads,
    building on the property-read production eligibility boundary (rules 12,
    13, ADR 0296). The durable lesson is that optional-chain admission is not
    a single gate lift. The IR flags `IsOptional` and
    `ShortCircuitOnNullishTarget` are independent: admitting
    `IsOptional:true, ShortCircuitOnNullishTarget:false` keeps the first-hop
    simple case VM-owned while the second-hop chaining sentinel remains a
    separate slice. A future slice admitting chained forms must own
    sentinel-value propagation through the full chain before removing the
    `ShortCircuitOnNullishTarget:true` decline.
    Issue planitem-planmanual1780198120145433000-widen-unified-bytecode-production-conditio-e36522d0f7
    / PR #2800: when proving that the optional-chain base is evaluated exactly
    once, the test base must be a call result (e.g. `getBox()?.value`,
    `getBox()?.["value"]`). A call result is not activation-resolved, so the
    containing function will NOT qualify for the production fast path — do NOT
    assert the `unified-bytecode-production-fast-path` log in these
    single-evaluation tests. The behavioral assertion (call count = 1) alone is
    the correct proof. Asserting the fast-path log alongside a call-result base
    is incorrect because `TryIsFirstBoundaryOptionalNamedPropertyReadCandidate`
    and `TryIsFirstBoundaryOptionalComputedPropertyReadCandidate` reject
    non-activation-resolved bases.
    Issue planitem-planmanual1780198120145433000-widen-unified-bytecode-production-conditio-bbc04f2625
    / PR #2802 (Batch 2) widened optional-chain admission to include `a?.b.c`
    (named chain, variable-length ≥3 ops) and `a?.b[k]` (named-then-computed,
    fixed 4 ops). Four durable lessons:
    (a) **IR encoding of `a?.b.c`**: trailing regular hops after the optional
    start use `GetNamedProperty(IsOptional:false, ShortCircuitOnNullishTarget:true)`,
    NOT `JumpIfShortCircuited`. The `JumpIfShortCircuited` opcode appears only
    in call-target expression programs; property-read chains propagate the null
    sentinel through `ShortCircuitOnNullishTarget:true` on each regular hop.
    Admission discriminator for the `a?.b.c` shape admitted in this batch:
    first hop requires `IsOptional:true, ShortCircuitOnNullishTarget:false`;
    each subsequent regular hop requires `IsOptional:false,
    ShortCircuitOnNullishTarget:true`. A double-optional second hop
    (`a?.b?.c`) has `IsOptional:true` on that hop; this form was admitted in
    the following batch (issue `aae6bde47e` / PR #2804) via jump-based lowering
    rather than the sentinel-propagation scheme originally anticipated.
    (b) **Variable-length vs fixed-count predicates**: `TryIsFirstBoundaryOptionalNamedPropertyReadChainCandidate`
    loops over ≥3 ops for any-length named chain; `TryIsFirstBoundaryOptionalNamedThenComputedCandidate`
    checks exactly 4 ops because `a?.b[k]` has a fixed shape. When admitting a
    new optional-chain form, decide fixed vs variable before choosing the
    predicate structure.
    (c) **No unconditional decline arms in `TryFindExpressionDecline`**: every
    `OptionalChainDependency` decline arm must attempt the relevant predicates
    before declining (AC-7). The `JumpIfShortCircuited` arm was also widened to
    try `TryIsFirstBoundaryOptionalNamedPropertyReadChainCandidate` even though
    property-read chains never contain `JumpIfShortCircuited` — the principle is
    that future chain predicates may eventually cover the arm, so it must not be
    left permanently unconditional.
    (d) **Stale pre-batch guard tests**: when a shape is admitted in a batch
    delivery, any test named `_StillDeclines…` that asserts `IsEligible = false`
    for that shape must be renamed to `_IsAdmitted` and the assertion flipped in
    the same slice. Leaving it creates a test that silently documents an admitted
    shape as declined. WHY: `Evaluate_OptionalThenRegularPropertyChain_StillDeclinesWithOptionalChainDependency`
    was a pre-implementation guard for `a?.b.c` that was never updated after the
    admission commit; the learn-stage build-fix caught and repaired it in PR #2802.
    Issue
    `planitem-planmanual1780240661926543000-burn-down-unified-bytecode-production-decl-55cc574fe4`
    / PR #2898 widened optional named-then-computed property-read chains from
    the prefix form `a?.b[k]` to continuation forms such as
    `a?.items[left + right].value`. The durable lesson is that the accepted
    optional span must include ordinary trailing named continuations after the
    computed hop. Eligibility must prove the whole chain before lifting
    `PropertyReadBoundaryOutOfScope`, and the compiler must target the first
    `JumpIfNullishReplaceUndefined` past the entire chain while appending the
    trailing non-optional, non-private `GetNamedProperty` instructions after
    `GetComputedProperty`. Do not use the prefix predicate as permission for
    optional trailing computed hops, optional writes/updates/deletes, calls,
    `super`, private names, dynamic lookup, or unowned computed-key payloads.
    See ADR 0311.

44. When admitting `ConditionalExpression` (`cond ? a : b`) to production
    unified bytecode, the **only new compiler surface** is
    `ExpressionOpKind.Jump` in `TryAppendExpressionProgramOps`. No new VM
    opcodes are needed: the existing `JumpIfFalse` (for the condition),
    `Pop`, and `Jump` VM opcodes cover the ternary execution model.
    `JumpIfConditionalFalse` (the expression-level opcode for the ternary
    condition check) maps to `UnifiedBytecodeOpCode.JumpIfFalse` — this is
    **consume-semantics**, not `JumpIfShortCircuitFalse` (peek-semantics,
    used by `&&`/`||`). The distinction is load-bearing: `JumpIfShortCircuitFalse`
    leaves TOS intact because the LHS value IS the result when falsy;
    `JumpIfConditionalFalse` consumes TOS because the condition value is not
    part of the ternary result.

    The expression-program compiler emits ternary as:
    ```
    [test ops]
    JumpIfConditionalFalse(alternateStart)   ← consume-semantics; maps to UnifiedBytecodeOpCode.JumpIfFalse
    Pop
    [consequent ops]
    Jump(endTarget)               ← ExpressionOpKind.Jump — the only new compiler case (PR #2772)
    Pop
    [alternate ops]
    [endTarget]
    ```

    Wire `ExpressionOpKind.Jump` with the same `exprPcToUnifiedPc[]` backpatch
    pattern used by `JumpIfFalse/JumpIfConditionalFalse/True/NotNullish`
    (ADR 0293): emit `UnifiedBytecodeOpCode.Jump(0)` as a placeholder, record
    a patch entry, and backpatch the operand after all ops are emitted. Add
    `case ExpressionOpKind.Jump:`, `case ExpressionOpKind.JumpIfConditionalFalse:`,
    and `case ExpressionOpKind.Pop:` to `TryFindExpressionDecline`'s allowed-op set
    alongside `JumpIfFalse`, `JumpIfTrue`, and `JumpIfNotNullish`. `Pop` is required
    because the ternary execution model emits `Pop` on both branches to discard the
    condition value (see the code diagram above); without this case, eligibility
    declines valid ternary programs on `Pop` even after `Jump` and
    `JumpIfConditionalFalse` are admitted. When testing unified-bytecode eligibility
    for ternary programs, assert `UnifiedBytecodeOpCode.JumpIfFalse` for the
    condition jump — not `JumpIfShortCircuitFalse`.

    Note: optional-call expression programs (`box.read?.()`) also contain
    `ExpressionOpKind.Jump` but are attached to
    `CallInvocationBoundaryInstruction`, which `TryGetExpressionProgram` does
    not handle. The eligibility op scan is never reached for those programs, so
    the admitted `Jump` case does not accidentally pass optional-call shapes.

    WHY: issue gh2770 / PR #2772 found that ternary was declined with
    `UnsupportedPlanShape` because `TryAppendExpressionProgramOps` had no
    `case ExpressionOpKind.Jump:` — the compiler fell through to its
    `default:` arm and returned `UnsupportedExpressionOp`. The fix is a single
    new `case` with a placeholder-emit-then-backpatch. No new opcodes, no VM
    changes, no resumable-path changes. ADR 0297.
    Issue gh2794 / PR #2794 then widened production eligibility to admit
    `ExpressionOpKind.JumpIfConditionalFalse` after the ternary compiler
    switched from `JumpIfFalse` to the dedicated `JumpIfConditionalFalse`
    expression op. The build-back repair updated
    `UnifiedBytecodeProductionEligibilityTests.cs` to assert
    `UnifiedBytecodeOpCode.JumpIfFalse` (not `JumpIfShortCircuitFalse`) for
    the ternary condition jump, confirming the consume-semantics mapping.
    The durable lesson: `JumpIfConditionalFalse` and statement-level
    `JumpIfFalse` share consume semantics and the same `UnifiedBytecodeOpCode.JumpIfFalse`
    mapping; only their expression-op kinds differ. Any future expression-level
    conditional opcode must classify its stack effect (consume vs peek) before
    deciding how it maps to the unified VM layer.
    Issue widen-unified-bytecode-production-conditio-0c8d5a9dc7 / PR #2795
    then completed ternary eligibility by adding `ExpressionOpKind.Pop` to
    `TryFindExpressionDecline`'s allowed-op set; without it, the `Pop`
    condition-discard ops on both ternary branches caused eligibility to decline
    valid ternary programs. The delivery also added AC-2 (consume-semantics
    proof: both truthy and falsy branches produce correct results in sequence,
    which would fail if the condition were left on the stack) and AC-3 (nested
    ternary `c1 ? c2 ? a : b : d` for all four condition combinations on the
    production fast path).
    Issue widen-unified-bytecode-production-conditio-852d01f78b / PR #2797
    completed the ternary proof pack with comprehensive eligibility and
    invocation coverage. Two durable lessons emerged:
    (a) **Gate 3 negative-assertion pattern**: when a declined ternary shape
    has side effects (function-call arms, e.g. `cond ? effect(10) : effect(20)`),
    add an explicit `Assert.DoesNotContain` assertion on the
    `unified-bytecode-production-fast-path` log — not just behavioral
    correctness. The JS result is correct regardless of which execution path is
    taken, so a behavioral-only test cannot prove the fast path was declined.
    (b) **AC-5 architectural deviation**: `this.flag ? a : b` is a pre-VM
    decline, not a fast-path accept. `TryIsFirstBoundaryPropertyReadShortCircuitExpressionCandidate`
    handles only peek-semantics opcodes (`JumpIfFalse/JumpIfTrue/JumpIfNotNullish`)
    where the LHS value IS the expression result on the taken branch.
    `JumpIfConditionalFalse` is consume-semantics: the condition value is not
    part of the ternary result. Extending the short-circuit helper to admit
    `JumpIfConditionalFalse` would be architecturally incorrect. Document such
    deviations from originally planned acceptance criteria explicitly in test
    comments so future agents do not re-open the question or silently drop the
    acceptance requirement.

    Issue `planitem-planmanual1780198120145433000-widen-unified-bytecode-production-conditio-aae6bde47e`
    / PR #2804 admitted **multi-hop optional named chains** (`a?.b.c`,
    `a?.b?.c`) via jump-based lowering reusing the existing
    `JumpIfNullishReplaceUndefined` opcode (ADR 0298). Key lessons:
    (a) **Jump-based lowering — no new opcode**: the original plan sketched
    a new `JumpIfShortCircuitNullish` VM opcode and a sentinel-value
    propagation scheme. The simpler approach was to emit
    `JumpIfNullishReplaceUndefined(chain-end)` for *each* optional hop and
    leave non-optional continuation hops as plain `GetNamedProperty`. All
    `JumpIfNullishReplaceUndefined` jumps in the chain target the **same**
    chain-end PC, so a nullish value at any hop short-circuits the entire
    remainder to `undefined` in one jump — no propagation flag needed. Zero
    VM changes were required.
    (b) **Real-undefined vs nullish distinction**: `a?.b.c` where `a.b`
    evaluates to actual `undefined` (non-nullish, e.g. `a = { b: undefined }`)
    still throws `TypeError` on the `.c` read, because the `ShortCircuitOnNullishTarget:true`
    continuation hop is a plain `GetNamedProperty` that runs on the real
    `undefined` value. `a?.b?.c` differs: the second `?.` emits its own
    `JumpIfNullishReplaceUndefined(chain-end)`, so an actual-undefined `a.b`
    short-circuits to `undefined` without throwing.
    (c) **Admission discriminator widened**: `TryIsFirstBoundaryOptionalNamedChainCandidate`
    accepts `[activation-base, GetNamedProperty(IsOptional:true), GetNamedProperty(ShortCircuit:true or IsOptional:true)+]`.
    Continuation hops may be either plain-continuation (`IsOptional:false,
    ShortCircuitOnNullishTarget:true`) or another optional hop (`IsOptional:true`).
    The compiler emits `JumpIfNullishReplaceUndefined(chain-end)` for
    `IsOptional:true` hops and plain `GetNamedProperty` for
    `ShortCircuitOnNullishTarget:true` hops.
    (d) **Deferred forms**: computed multi-hop chains (`a?.b?.[k]`,
    `a?.[k]?.b`), optional-then-computed (`a?.b[k]?.c`), assignment targets,
    super-optional, and dynamic-lookup chains still decline as
    `OptionalChainDependency`.

45. When admitting multi-hop optional call chains (`a?.b.c()`, `a?.b?.c()`)
    to production unified bytecode, classify by where the `?.` appears relative
    to the call member, and set `isCallTargetPreparationCandidate` before the
    per-op decline loop reaches any `GetNamedProperty(IsOptional:true)`:

    - **`a?.b.c()` (Case 4)** — optional-start chain, plain non-optional call:
      the IR emits `[base, GetNamedProperty(IsOptional:true, b), JumpIfShortCircuited,
      LoadNamedCallTarget(c), args..., Call]`.
      `JumpIfShortCircuited` is produced by the IR builder whenever
      `HasOptionalChaining(member.Target)` is true, even when the call member
      itself is non-optional. Lowers to:
      `LoadSlot(base)`, `JumpIfNullishReplaceUndefined(end)`,
      `GetNamedProperty(b)`, `PrepareNamedCallTarget(c)`, args.
    - **`a?.b?.c()` (Case 5)** — double-optional chain, receiver-optional call:
      the IR adds `JumpIfNullish(ReplaceWithUndefined:true)` before
      `LoadNamedCallTarget`. Lowers to:
      `LoadSlot(base)`, `JumpIfNullishReplaceUndefined(end)`,
      `GetNamedProperty(b)`, `PrepareNamedOptionalCallTarget(c, end)`, args.

    Both `JumpIfNullishReplaceUndefined` and `PrepareNamedOptionalCallTarget`
    share the same `end` backpatch target (the PC after `CallInvocationBoundary`),
    set after all argument spans are emitted. No new VM opcodes are required.

    **Eligibility gate**: `GetNamedProperty(IsOptional:true)` at op[1] is
    normally declined as `OptionalChainDependency` unless a candidate recognizer
    sets `isCallTargetPreparationCandidate = true` before the per-op loop. The
    two new candidate recognizers (`TryIsFirstBoundaryOptionalChainPlainCallCandidate`
    and `TryIsFirstBoundaryOptionalChainReceiverOptionalCallCandidate`) set this
    flag; the existing `if (isCallTargetPreparationCandidate) break;` escape in
    `TryFindExpressionDecline` then admits the op without firing `OptionalChainDependency`.

    Deferred: `a.x?.b.c()` (non-activation-resolved base) still declines as
    `OptionalChainDependency` (AC-4); computed intermediate, super-optional, and
    dynamic-lookup call chains remain deferred.

    Test coverage must include: null base → `undefined`, null intermediate →
    `undefined`, live chain → computed value, and base-evaluated-once (side
    effects on the base are not repeated on short-circuit).

    WHY: issue gh2806 / PR #2814 (ADR 0301) extended Cases 4–5 for optional call
    chains, reusing the `JumpIfNullishReplaceUndefined` jump-based pattern from
    ADR 0298 (multi-hop optional property reads). The durable lesson is that
    `JumpIfShortCircuited` is an IR artifact of `HasOptionalChaining(member.Target)`
    being true — it appears in call-target programs even when the call member is not
    optional — and the eligibility gate must set `isCallTargetPreparationCandidate`
    before the per-op loop to allow the preceding `GetNamedProperty(IsOptional:true)`
    through. Without that flag, adding optional-chain call patterns will silently
    decline the intermediate optional property access with `OptionalChainDependency`.

## Why

Issue #2118 / PR #2137 introduced the first unified bytecode slice for
`function add(x, y) { return x + y; }`. The useful decision was not just the
new files; it was the boundary: compile from existing `ExecutionPlan`, flatten
only the proven return-expression payload, keep the VM fallback-free, and leave
production routing untouched. Future agents should preserve that boundary so
unified-bytecode coverage gaps stay explicit instead of being masked by the
existing statement IR, expression bytecode, or AST evaluators.

Issue #2139 / PR #2144 expanded the prototype to an exact linear local
declaration plus return shape and then fixed review feedback by passing
function-kind metadata into `UnifiedBytecodeCompiler.TryCompile`. The lesson is
that async and generator bodies can look shape-compatible while requiring
promise, iterator, and suspension semantics that the current unified VM does
not implement. Function kind must stay part of the compile-time eligibility
contract.

Issue #2158 / PR #2162 expanded the same prototype into a small linear sync
expression pack with multiple declarations, literals, and numeric binary
operators. The lesson is that this expansion must still own the bytecode it
executes: literals belong in `UnifiedBytecodeProgram`, supported expression ops
are flattened into unified instructions, and unsupported statements or
expression ops decline before execution. Adding a generic expression-program
eval opcode would hide coverage gaps and make the fallback-free VM boundary
untrue.

Issue #2166 / PR #2173 crossed the prototype from linear body walking into
acyclic branch CFG compilation. The lesson is that branch support needs an
explicit bytecode-PC owner: map IR instruction indices to emitted PCs and patch
`JumpIfFalse` and `Jump` targets after blocks are emitted.

Issue #2182 / PR #2186 then extended that boundary to one canonical
condition-first back-edge IR shape (currently produced by guarded `while` and
equivalent condition-only `for` forms without initializer/post-update or loop
control statements). The lesson is to keep this loop support narrow and
compiler-owned: accept only the proven canonical IR topology, reject other
loop/control-flow families before unsupported details hide the real boundary,
and keep production routing unchanged. The review correction on #2182 is part
of the rule: this is not a source-syntax `while` exception; source forms are
eligible only when lowering produces the same condition-first back-edge shape.

Faktorial issue
`planitem-planmanual1779822558747978000-batch-1-production-eligibility-boundary-de-8d0f3a4e16`
and PR #2205 introduced the first production-routing eligibility selector. The
lesson is that production eligibility is a separate contract from prototype
compile coverage: the first route accepts only neutral slot/literal/store/return
bytecode and declines async/generator functions, captured or dynamic activation,
arguments-object dependency, `this`, `new.target`, calls, dynamic lookup,
labels, break/continue, and prototype-only opcodes before VM execution.

Faktorial issue
`planitem-planmanual1779822558747978000-batch-1-production-eligibility-boundary-ba-aa82d6b615`
and PR #2217 made that selector execute in production sync invocation for the
first time. The lesson is that runtime routing is a three-way contract between
existing sync fast-path ordering, the decline-first unified-bytecode selector,
and the `ActivationSlotShape` slot bridge. Future agents should not bypass that
bridge with source-shape checks, environment creation, runner callbacks, or VM
fallbacks.

Faktorial issue
`planitem-planmanual1779822558747978000-batch-1-production-eligibility-boundary-ba-c0e3afa6d7`
and PR #2231 hardened the Binary production boundary before branch or loop
routing can use it. The lesson is that prototype Binary support and numeric VM
parity do not make `Binary` production-safe. Production eligibility must decline
the operator family first, include operator-specific diagnostics, and keep
branch/loop structural routing from admitting unproven condition semantics.

Issue #2227 / PR #2239 admitted the first production `JumpIfFalse` shape:
a single direct forward branch-return program with immediate return arms and no
`Jump`. The review correction is part of the rule. The invocation proof first
conflicted with restored `SyncIrCallTrampoline` priority, then was fixed by
using a local selector shape that still lowers to the accepted branch-return
program while avoiding the existing trampoline shortcut. Future agents should
prove production routing with a route-discriminating shape, not by moving
unified bytecode ahead of older fast paths. WHY: the incident showed that
selector acceptance and invocation routing are separate contracts; a source
shape can be eligible for unified bytecode but still correctly execute through
a higher-priority fast path.

Faktorial issue
`planitem-planmanual1779822558747978000-batch-1-production-eligibility-boundary-ba-4fb4d210a6`
and PR #2243 widened production routing to branch joins, joined-local updates,
comparison conditions, string/coercing Binary use, and the canonical
condition-first loop shape. The lesson is that this was safe only after the VM
stopped using direct numeric Binary operations and reused the existing
`JsValue` operator helpers with the active `EvaluationContext`. The delivery
also intentionally moved unified production routing ahead of the broad
`SyncIrCallTrampoline` while keeping direct specialized binary shortcuts ahead
of unified bytecode. WHY: the incident showed that admitting control-flow
opcodes is not the decision by itself; operator semantics, compiler-owned CFG
shape, route priority, and public route-log evidence must move together.

Faktorial issue
`planitem-planmanual1779822558747978000-batch-1-production-eligibility-boundary-ba-2dd33add2a`
and PR #2254 updated the ADR, roadmap, and performance evidence after Batch 5.
The lesson is that stale docs can become a routing hazard: ADR 0204/#2227
direct-branch text remained useful history, but ADR 0210 owned the current
branch-join/canonical-loop production boundary. WHY: the issue existed because
future agents needed the exact eligible set, unsupported declines,
no-mixed-execution rule, and allocation-stability-only proof language in the
same maintained surfaces before widening production eligibility again.

Issue #2256 / PR #2261 widened the ADR 0210 production Binary subset by adding
only loose equality (`BinaryOperator.Equal`, `==`). The lesson is that even a
single operator widening needs paired selector, unified compiler allowlist, VM
semantics, public route-log proof, and no-route proof for a nearby unsupported
operator. The VM used `JsOps.LooseEquals(left, right, context)` instead of a
direct host comparison, while strict equality (`===`) stayed declined and
route-negative. WHY: the issue was a roadmap follow-up specifically to prevent
selector-only widening or mixed execution after ADR 0210; accepting `==`
without compiler/VM parity and branch-shaped route evidence would have made the
production boundary look wider than the runtime semantics actually proved.

Faktorial issue
`planitem-planmanual1779860498694736000-batch-1-property-read-boundary-define-and-8d40cdb281`
and PR #2288 defined the first production property-read boundary as a
selector-only contract. The lesson is that property reads are observable even
when the lowered operation names look simple: ordinary computed reads must keep
`RequireObjectCoercible(Depth: 1) -> ResolvePropertyKey -> GetComputedProperty`
in order, optional chains and adjacent write/call/delete/super/object-literal
families must stay declined, and recognized candidates still decline until the
unified compiler and VM execute them directly. WHY: the issue existed to keep
future property-read widening from admitting source-shaped or opcode-shaped
candidates into a fallback-free production VM before the executable semantics
and route proof exist.

Faktorial issue
`planitem-planmanual1779860498694736000-batch-1-property-read-boundary-batch-2-uni-990bcd3283`
and PR #2311 made that boundary executable by adding owned property-read
opcodes, `StringConstants` operand storage, `JsOps`-backed lookup/key
semantics, production eligibility acceptance, and public invocation proof.
WHY: the incident closed the deliberate ADR 0218 gap where property-read
candidates were recognized but declined until the VM could execute them
directly. Future widening must preserve the same all-at-once contract so
observable property semantics do not slip through a mixed
`ExpressionProgram`/runner/AST fallback.

Issue #2314 / PR #2320 widened the executable property-read boundary to the
exact two-hop direct named chain `LoadIdentifier -> GetNamedProperty ->
GetNamedProperty`. The durable lesson is two-part: the boundary itself stays
small and owned by existing `GetNamedProperty` opcodes, and shape-probing
compiler helpers must not partially mutate shared builders before a full shape
match is known. WHY: focused verification first exposed stack corruption from
partial emission in the new named-chain helper; the accepted fix prevalidated
the full chain before emitting `LoadSlot` and property-read opcodes. Issue
`planitem-planmanual1779965179415360000-batch-1-receiver-aware-call-execution-boun-3cea46640b`
/ PR #2609 later removed the exact two-hop cap for optional-free
activation-resolved named chains by reusing existing `GetNamedProperty`
emission. The durable lesson is that deeper named reads are acceptable only
when every hop remains VM-owned and adjacent computed/optional/private/dynamic
families keep explicit pre-VM declines.

Faktorial issue
`planitem-planmanual1779860498694736000-batch-1-property-read-boundary-batch-3-com-ca10aa7559`
and PR #2321 completed the property-read boundary proof by adding explicit
declines for unsupported computed property-key payloads: `box[{ value: 1 }]`
and `box[{ ...source }]`. WHY: object literal/spread operations can appear in
the key-evaluation payload before the final computed property read. Without
source examples for those payloads, future widening can misclassify them as a
generic property-read boundary miss or lose the guardrail while merging adjacent
property-read batches.

Faktorial issue
`planitem-planmanual1779860498694736000-batch-1-property-read-boundary-batch-4-pub-8e90a024bf`
and PR #2329 expanded public invocation route proof for production property
reads. Review caught that the first `super.value` no-route case asserted against
the wrapper function `readSuper`, while the unsupported `super.value`
expression lived inside the derived method. The repair renamed that method to
`readViaSuperBoundary` and asserted no route for the owning method instead.
WHY: a wrapper can stay off the fast path while its callee incorrectly routes,
so negative public-log proof must target the body that actually owns the
unsupported expression.

Faktorial issue
`planitem-planmanual1779887420937175000-batch-1-boundary-and-baseline-gate-define-5cc93efb5a`
and PR #2379 widened production routing to the first ordinary property
write/update boundary. Review found that private-field named-property op shapes
needed explicit guards before the selector or compiler treated them as ordinary
named properties. WHY: non-computed private member access can lower into named
property read/write/update operations with private-name strings, but the unified
VM's ordinary named property opcodes do not own private-name lexical resolution,
brand checks, or private accessor behavior. Future property write/update
widening must decline private names before VM execution unless a separate slice
adds owned private-field semantics.

Faktorial issue
`planitem-planmanual1779887420937175000-batch-1-boundary-and-baseline-gate-batch-2-d45261c97e`
and PR #2396 completed the first ordinary property-write production proof by
closing two review gaps. The computed-write proof had to use a call-free,
route-eligible `write(box, key, value)` body and move RHS side effects to the
call site, because call payloads are outside the admitted boundary. The strict
failed-write proof also had to show the strict function itself used
`unified-bytecode-production-fast-path`. WHY: the unified VM executed accepted
property writes without a function environment, so `context.CurrentScope`
strictness alone was insufficient. The accepted repair passes lexical
strictness from `SyncFunctionInvoker` into `UnifiedBytecodeVirtualMachine` and
lets the compiler skip only directive string-literal discard instructions for
strict directive prologues.

Faktorial issue
`planitem-planmanual1779887420937175000-batch-1-boundary-and-baseline-gate-batch-3-d4c5a8e668`
and PR #2415 added boundary-decline coverage around the admitted ordinary
property-write slice. Logical member assignment, a dynamic/global RHS
dependency, and computed-key expression payloads all execute correctly through
fallback paths and must not log `unified-bytecode-production-fast-path` until a
future slice owns their selector, compiler, VM, and route-proof semantics. WHY:
the admitted simple property-set route is intentionally narrower than the full
property-write family, so nearby write shapes need explicit decline examples to
stop future agents from treating "property write" as a broad source-syntax
permission.

Faktorial issue
`planitem-planmanual1779887420937175000-batch-1-boundary-and-baseline-gate-batch-4-f0057ffdc4`
and PR #2426 admitted direct named and computed compound property assignments
by adding compound get-for-set opcodes rather than generic unified stack
operators. WHY: compound assignment lowering needs to read the old property
value while preserving the receiver, and computed compound assignment also must
preserve the once-resolved key for the eventual set. A generic duplicate/swap
surface or VM callback would have widened production unified bytecode beyond
the proven selector, compiler, VM, and route-proof boundary.

Faktorial issue
`planitem-planmanual1779943568009120000-batch-1-shared-bytecode-surface-and-parall-fc03ae9db9`
and PR #2503 closed the parallel-lane production-routing integration batch with
tests rather than runtime widening. The lesson is that the accepted surface must
be proven both lane-by-lane and as an ordinary mixed program: literals, property
reads/writes/updates, block lexical scopes, loop control, and primitive
operations should compose as one `UnifiedBytecodeProgram` without non-executable
call-boundary opcodes or fallback. That older slice proved binary-chain simple
returns still used their specialized fast path; the later bytecode-only route
work superseded that priority by making production unified bytecode run before
the simple-return/simple-binary shortcuts whenever the plan is production
eligible. WHY: without an integrated guard, future agents can have
complete-looking per-lane coverage while an ordinary sync function either
drifts into mixed execution or shadows the bytecode-first route with an older
shortcut.

Faktorial issue
`planitem-planmanual1779943568009120000-batch-1-shared-bytecode-surface-and-parall-a88c0a9ba1`
and PR #2509 made the production unified-bytecode source gate persistent. The
lesson is that no-mixed-execution proof needs two precise scan scopes: the
accepted sync-invoker method body, not the whole mixed-responsibility invoker
file, and the unified VM source itself. Review feedback also showed that
worktree-aware root discovery belongs in the gate, and that `ExpressionProgram`
must be forbidden in the VM source alongside `ExecutionPlanRunner` and AST-eval
entry points. WHY: otherwise a guard can look complete while either failing in
linked worktrees or allowing an expression-bytecode bridge back into the
production unified VM.

Faktorial issue
`planitem-planmanual1779965179415360000-batch-1-receiver-aware-call-execution-boun-6700005263`
and PR #2613 added the first production resumable unified-bytecode route for
simple async functions and sync generators. Future resumable widening must keep
this route separate, decline-first, and VM-state-owned: accepted programs must
preserve program counter, operand stack, slots, pending await promise, pending
resume payload, and completion state in `UnifiedBytecodeVirtualMachine`, with
positive route-log proof and adjacent no-route/fallback proof. Do not treat
`YieldStar` opcode presence alone as production support. Sync-generator
`yield*` may route only after the VM owns delegated `.next(value)`,
`.return(value)`, and `.throw(value)` behavior end to end, including missing
delegate methods, iterator-close/error propagation, iterator-result object
validation, yielded-vs-completed results, resume-state preservation, positive
resumable route-log proof, and adjacent still-unowned delegate declines.
Async-generator delegated `yield*` follows the same ownership bar plus
promise/async-iterator settlement through the resumable async-generator
`PendingAwait` bridge. Awaited delegated sources (`yield* await ...`) may route
only when the source expression is VM-owned as `AwaitValue` before `YieldStar`;
unsupported delegated expression payloads must still decline before VM
execution until their delegated-source settlement semantics are modeled. WHY: the
build-stage repair for PR #2613 had to decline `yield*` after the first slice
exposed delegated abrupt resume as a separate protocol boundary from simple
`yield` and awaited return. Faktorial issue
`planitem-planmanual1780240661926543000-burn-down-unified-bytecode-production-decl-dc74ab36e4`
/ PR #2948 then reopened only the sync-generator lane after the VM delegated
abrupt resume through the underlying iterator and proved `.return()`/`.throw()`
on the resumable fast path. Issue #2955 / PR #2958 kept the async-generator
lane declined with explicit tests for delegated `.return(value)` and
`.throw(value)` staying on the IR async-generator path and producing no
resumable unified-bytecode route log. Faktorial issue
`planitem-planmanual1780639098493226000-full-unified-bytecode-execution-burndown-b-1747a4b32a`
/ PR #3221 then reopened the non-awaited async-generator lane after the VM
owned delegated async iterator settlement across `PendingAwait`. Faktorial issue
`planitem-planmanual1780639098493226000-full-unified-bytecode-execution-burndown-b-c98550dd55`
then reopened the awaited-source lane by proving source-await settlement through
`AwaitValue` before `YieldStar`, not just `YieldStar` opcode reachability.

Faktorial issue
`planitem-planmanual1780157100924814000-baseline-batch-2-object-literal-shorthand-ebbe2ff1ae`
and PR #2740 admitted `LoadFunctionLiteral` as the first opcode that creates
function values from AST descriptors inside the unified VM. Future opcodes with
similar semantics must follow the same four-part pattern:

1. **Eligibility**: move the opcode from the decline block into the
   allowed-opcode subset; do not assume a blanket-decline entry is safe to leave
   as-is just because it has not been exercised in the target slice.
2. **Constant pool**: add a typed `FunctionLiteralConstants` field (or analogous
   typed pool) to `UnifiedBytecodeProgram` for the descriptors — do not reuse
   `LoadLiteral`/`JsValue` constants for non-`JsValue` payloads. Default the
   field to `ImmutableArray.Empty` when the program contains no such opcodes.
   Encode the pool index in the upper operand bits and any flags in the low bits
   (PR #2740 uses bit 0 for `isConstructor`).
3. **Calling environment gate**: if the opcode needs closure creation, register
   the program via `RequiresProductionUnifiedBytecodeCallEnvironment` in
   `SyncFunctionInvoker` so the calling environment is provisioned before the VM
   runs. The VM case must throw `InvalidOperationException` when `currentCallingEnvironment`
   is null — never silently fall through with an uninitialized closure.
4. **VM case**: call `TypedAstEvaluator.CreateFunctionValueFromLiteral` (or the
   relevant internal wrapper in `FunctionExpressionExtensions`) with the
   descriptor, calling environment, execution context, and `isConstructor`; push
   the resulting `JsValue` and advance the program counter.

Investigation-stage caution: when unblocking an `AllowNameInference`-gated shape
in the eligibility and compiler, count the full set of guards — eligibility
decline block, compiler guard, VM throw stub, and the calling-environment gate —
before declaring the fix complete. PR #2740's build stage discovered a fourth
guard (`LoadFunctionLiteral` had no VM support at all) that the investigation
summary missed. WHY: guard-removal analysis that stops at three visible blocks
misses a fourth structural gap (unsupported opcode), which the build stage must
then repair, adding latency and context switch.

Issue `planitem-planmanual1780157100924814000-baseline-batch-3-array-spread-in-array-lit-300d522431` / PR #2748 admitted `ArraySpread` as the first production-eligible spread opcode in array literals. The span helper (`TryAppendSimpleArrayLiteralSpan`) was extended to emit `ArraySpread` for spread elements whose source is `IsSimpleOperand` (activation-resolved), and `TryMeasureSimpleArrayLiteralSpan` was widened to accept `ArraySpread` alongside `ArrayPush`. Non-simple spread sources decline with `ObjectLiteralOrSpreadDependency` (a pre-scan in `TryFindExpressionDecline` detects this before the source ops are processed by the general loop). The initial implementation only wired the span helper; a fix commit added the four missing secondary sites. The durable lesson is rule #40: the span helper is not self-contained — it has four dependent infrastructure layers (main compiler switch, production opcode allowlist, decline pre-scan, and expansion contract) that must each be updated in the same slice.

Faktorial issue
`planitem-planmanual1780157100924814000-baseline-batch-3-array-spread-in-array-lit-389e8f1c98`
and PR #2750 completed the array spread in array literals admission (Batch 3),
applying all four span-extension surfaces (rule #40) plus VM opcode, eligibility
tests, and invocation tests in a single slice without a build-back repair. The
delivery also admitted `ArrayPushHole` standalone to `TryMeasureSimpleArrayLiteralSpan`
alongside `ArraySpread`, enabling hole+spread patterns (`[, ...a]`). The lesson is
that following rule #40 proactively — applying all four surfaces together, including
`ArrayPushHole` standalone measurement admission for hole-bearing spread arrays —
avoids the build-back repair cycle that the sibling task (PR #2748) required.

46. When admitting logical compound assignment on slot identifiers (`x &&= y`,
    `x ||= y`, `x ??= y`) to production unified bytecode, reuse the existing
    peek-semantics short-circuit jump opcodes (`JumpIfShortCircuitFalse`,
    `JumpIfShortCircuitTrue`, `JumpIfShortCircuitNotNullish`) in a
    **statement-level** pattern that is distinct from the expression-level form:

    ```
    LoadSlot(slot)                    // push slot value as condition
    JumpIfShortCircuitX(sc-pop)      // peek: if short-circuit, jump to sc-pop
    Pop                              // proceeding: discard slot value
    [RHS expression program ops]     // evaluate RHS
    StoreSlot(slot)                  // write RHS result back to slot
    Jump(end)                        // skip sc-pop
    [sc-pop:] Pop                    // short-circuit: discard slot value from TOS
    [end:]
    ```

    Both paths leave the operand stack balanced (zero net delta). The slot value
    is used only as a condition and is discarded by **both** paths — contrast with
    expression-level `&&`/`||`/`??` where the LHS value IS the result on the
    short-circuit path and stays on TOS. The proof pack must include:
    (a) positive fast-path route for both the short-circuit branch and the
        proceed branch;
    (b) a stack-balance multi-call test asserting correctness across repeated
        invocations, which catches any `Pop`/`StoreSlot` mismatch that would
        corrupt the stack on the second call;
    (c) a Gate 1 side-effect test where the RHS is a global-call side effect —
        a global identifier call without `HasExplicitThis` declines the production
        path, so this test proves short-circuit opcode correctness without
        asserting `unified-bytecode-production-fast-path`.

    Computed logical compound assignments and unowned member forms remain
    declined as `PropertyWriteDependency`. Direct named and direct computed
    member logical assignments are admitted by the dedicated member rules below;
    do not reuse this slot pattern for member targets without preserving the
    receiver and the expression result through the dedicated cleanup shape.
    WHY: issue
    `planitem-planmanual1780198120145433000-widen-unified-bytecode-production-conditio-dad47dee93`
    / PR #2810 admitted slot-identifier logical compound assignment (ADR 0300).
    The durable lesson is that the same peek-semantics opcode serves two roles
    depending on context: in expression programs it returns the LHS value on the
    short-circuit path; in statement programs it is a condition-only branch that
    both paths must discard. Mixing the two roles in test or compiler analysis
    produces incorrect stack effects.
47. When admitting direct named member logical assignment (`box.value &&= y`,
    `box.value ||= y`, `box.value ??= y`, including `this.value` bases) to
    production unified bytecode, keep the shape exact and receiver/result
    preserving. The accepted expression-program shape is:

    ```
    base
    DuplicateTop
    GetNamedProperty
    JumpIfFalse | JumpIfTrue | JumpIfNotNullish
    Pop
    rhs
    SetNamedProperty
    DuplicateTop
    SwapTopTwo
    Pop
    ```

    Compile it through `GetNamedPropertyForCompoundSet`, the matching
    peek-semantics short-circuit jump, `SetNamedProperty`, and `SwapTopTwo` plus
    `Pop` cleanup. `GetNamedPropertyForCompoundSet` preserves the receiver for
    the write; on the short-circuit path `SwapTopTwo`/`Pop` discards that
    preserved receiver while keeping the current property value as the
    assignment expression result. On the proceeding path, `SetNamedProperty`
    leaves the assigned RHS value as the result.

    Keep selector and compiler probes matched to the exact direct named shape:
    activation-resolved base, non-optional/non-private named read and write,
    matching property names, exact cleanup jump target, and simple RHS. Optional
    chains, deeper member chains, private fields, `super`, destructuring,
    dynamic lookup, and complex RHS/key payloads still decline before VM
    execution. Direct computed logical member assignment now belongs to rule 47,
    including its compiler-owned key/RHS boundaries. Any new stack-mechanics
    opcode used by an admitted shape must be added to the VM handlers,
    production opcode allowlist, `docs/unified-bytecode-expansion-contract.md`,
    and focused opcode/route proofs in the same delivery slice.

    WHY: issue
    `planitem-planmanual1780198120145433000-widen-unified-bytecode-production-conditio-f31b87b5d8`
    / PR #2826 admitted direct named member logical assignment (ADR 0302). The
    delivery needed `SwapTopTwo` because the named get-for-set model preserves a
    receiver beneath the logical assignment result; without the cleanup shuffle,
    the VM could not keep the short-circuit result while discarding the receiver.
    The build-back repair also had to add `SwapTopTwo` to the expansion-contract
    opcode inventory, reinforcing that VM-executed opcode additions are delivery
    artifacts, not learn-stage cleanup. Follow-up issue
    `planitem-planmanual1780198120145433000-widen-unified-bytecode-production-conditio-0aa2351edc`
    / PR #2812 restored the remaining neighboring decline gates: deeper chains
    stay `PropertyWriteDependency`, private fields stay
    `PrivateFieldDependency`, and optional-chain assignment remains
    parser-rejected before eligibility.
48. When admitting direct computed member logical assignment (`box[key] &&= y`,
    `box[key] ||= y`, `box[key] ??= y`) to production unified bytecode, keep
    the compiler ownership and stack invariants aligned with the existing
    computed member get-for-set model. Preserve both receiver and resolved key,
    thread the matching peek-semantics short-circuit jump, and keep cleanup
    balanced so short-circuit returns the current property value while proceed
    returns the assigned RHS value.

    Keep selector and compiler probes exact and matched: activation-resolved
    base, direct computed read/write with matching key payload,
    non-optional/non-private access, and simple RHS/key payloads already owned
    by the computed member write boundary. The selector's computed-key
    predicate must stay aligned with the compiler helper that emits the key
    (`TryAppendComputedPropertyKeyLoad`); if `LoadThis` or `LoadNewTarget` is
    not compiler-owned for a computed key, the eligibility scan must decline the
    same shape before route selection instead of admitting it through a broader
    "simple operand" check. Optional chains, deeper member chains, private
    fields, `super`, destructuring, dynamic lookup, and unsupported key/RHS
    payloads remain pre-VM declines.

    WHY: issue #2844 / PR #2847 admitted direct computed member logical
    assignment after the named-member route from ADR 0302. The delivery reused
    `GetComputedPropertyForCompoundSet`, `SetComputedProperty`, peek-semantics
    short-circuit jumps, and cleanup stack shuffles so both the preserved
    receiver and resolved key are removed while the assignment expression result
    remains. The build-back repair found that selector eligibility initially
    treated `box[this] &&= value` and `box[new.target] &&= value` as accepted
    simple operands even though `TryAppendComputedPropertyKeyLoad` did not own
    those key loads for this route. The durable lesson is that every production
    computed-key admission site must use the compiler-supported computed-key
    predicate, with focused negative rows for near-simple but unsupported key
    operands, so eligibility cannot promise a route the compiler cannot emit.

49. When moving ordinary sync production activation pre-gates into
    `UnifiedBytecodeProductionEligibility`, use an explicit
    `UnifiedBytecodeProductionActivationDescriptor` field and stable
    `UnifiedBytecodeProductionDeclineCode` for each blocker instead of leaving
    it as an anonymous boolean in `CanUseProductionUnifiedBytecodeFastPath`.
    Keep the selector and eligibility paths single-sourced by calling the same
    activation-decline helper from the sync invoker pre-gate and from
    `Evaluate` before plan-shape inspection. Any descriptor-owned decline must
    also be excluded from `IsPlanStructuralDecline`, because the
    `ExecutionPlan` permanent-decline cache is global to the immutable plan
    while activation descriptor facts can vary per closure/invoker. Update the
    expansion contract's decline ledger and add both eligibility-code proof and
    public invocation no-route proof for the exact blocker family. WHY: issue
    `planitem-planmanual1780240661926543000-burn-down-unified-bytecode-production-decl-d89f847aed`
    / PR #2855 descriptorized arrow lexical this, class constructor activation,
    function-name/parameter collision, function declarations, parameter-var
    declarations, and materialized activation dependency. The key lesson was
    that stable decline taxonomy, cache-safety classification, and route
    fallback proof must move together; otherwise future widening can either hide
    blocker reasons behind generic pre-gates or incorrectly cache an
    invocation-dependent decline at plan scope.

50. When admitting class literals or other closure/environment-dependent value
    literals to production unified bytecode, keep the opcode VM-owned while
    reusing the runtime helper that already owns the semantic construction. For
    class literals, lower `ExpressionOpKind.LoadClassLiteral` to a dedicated
    `UnifiedBytecodeOpCode.LoadClassLiteral`, execute it through
    `TypedAstEvaluator.CreateClassValueFromLiteral(...)`, and require the sync
    bridge to provide a calling environment before VM execution. On the
    resumable route, admit only the shape whose class-definition machinery is
    already safe to run through the captured calling environment:
    constructor-only class expressions, implicit base constructors, and implicit
    derived constructors whose `extends` value is resolved from the surrounding
    environment rather than from resumable activation slots, narrow private
    instance fields, and narrow private method/accessor class literals whose
    constructor/private member bodies do not capture activation slots, and
    public non-computed instance accessors whose getter/setter bodies do not
    capture activation slots or use `super`. The resumable handler must
    synchronize unified slots into that environment before class creation, call
    `CreateClassValueFromLiteral(...)`, synchronize back after creation, and
    translate class-creation throws into the resumable throw step. Keep
    unproven fields, static elements/blocks, static/computed/private or
    activation-capturing public accessors, computed members, member `super`,
    activation-capturing private members, and `extends` expressions that read
    resumable activation slots as pre-VM declines until the VM owns those
    class-definition environment semantics. Do
    not duplicate class-definition,
    `extends`, static-element, private-name, or name-inference semantics inside
    the VM, and do not satisfy the route by falling back to `ExpressionProgram`,
    `ExecutionPlanRunner`, or raw AST evaluation. Keep the opcode allowlist,
    compiler lowering, VM handler, environment preflight,
    `docs/unified-bytecode-expansion-contract.md` opcode inventory, and focused
    eligibility/runtime proof in the same slice. WHY: issue
    `planitem-planmanual1780240661926543000-burn-down-unified-bytecode-production-decl-ad43f135ac`
    / PR #2858 admitted class literal values after they had been declining as
    `ObjectLiteralOrSpreadDependency`. The quality-gate re-entry fix found the
    expansion contract still listed `LoadFunctionLiteral` twice and omitted
    `LoadClassLiteral`, confirming that every newly VM-executed opcode must
    update the contract inventory as part of the delivery slice. Issue
    `planitem-planmanual1780639098493226000-full-unified-bytecode-execution-burndown-b-7698c2a64b`
    / PR #3195 added the B24a resumable class-literal route and showed the
    narrower resumable boundary: constructor/default-constructor class creation
    is safe through the captured calling environment, but class elements and
    activation-slot-dependent `extends` still require later class-definition
    environment ownership.
    Issue
    `planitem-planmanual1780639098493226000-full-unified-bytecode-execution-burndown-b-7d0f3d6a80`
    / PR #3194 widened that boundary for B24f private instance methods and
    accessors only after adding activation-capture declines and focused
    route/no-route proof. Related ADR:
    `docs/adrs/0327-admit-resumable-class-literal-private-members-through-shared-class-creation.md`.
    Issue #gh3237 / PR #3239 admitted B24g public non-computed instance
    accessors after keeping activation-capturing and `super` accessor bodies as
    pre-VM declines. Related ADR:
    `docs/adrs/0336-admit-resumable-class-literal-public-accessors-through-shared-class-creation.md`.

51. When widening production unified-bytecode construct routing beyond the
    identifier-only `new F(...)` lane, keep constructor-target recognition
    anchored to the terminal `Construct` op and reuse already-owned
    property-read and spread-boundary machinery instead of inventing a
    construct-only target-preparation lane or VM fallback. Named constructor
    targets may use ordinary non-optional `GetNamedProperty`; computed
    constructor targets may use only the exact ordinary-read sequence
    activation-resolved base load, simple key load,
    `RequireObjectCoercible(Depth: 1)`, `ResolvePropertyKey`, then
    `GetComputedProperty`; spread arguments must keep the existing
    invocation-boundary spread-mask encoding and left-to-right flattening. When
    the widening requires a generic opcode such as
    `RequireObjectCoercible` to become prototype-compiler-owned outside an
    older boundary assumption, update prototype tests in the same slice:
    replace stale "outside boundary decline" assertions with positive
    opcode-stream proof for the now-owned surface. WHY: issue
    `planitem-planmanual1780240661926543000-burn-down-unified-bytecode-production-decl-0be1791f55`
    / PR #2861 widened construct routing to spread/member/computed constructor
    targets and reused the existing computed-property and spread-boundary
    semantics. The merged build-back repair then had to fix a stale prototype
    assertion for `box[left + right]`, proving that construct-boundary
    widening and prototype compiler-surface expectations drift together.

52. When admitting ordinary property delete to production unified bytecode, keep
    the delete lane descriptor-aware, strictness-threaded, and narrower than the
    full delete family. Named deletes may route only when the receiver is
    activation-resolved, every intermediate receiver hop is non-optional and
    non-private, and the final operation is a non-private
    `DeleteNamedProperty`. Computed deletes may route only when the receiver is
    activation-resolved, any intermediate receiver prefix is made only of
    non-optional, non-private named property reads, the key operand is
    compiler-owned, and the final operation is `DeleteComputedProperty`.

    Lower accepted shapes to dedicated `DeleteNamedProperty` and
    `DeleteComputedProperty` opcodes and execute them through the runtime
    property-handle delete semantics with the active strictness. Do not satisfy
    the route through `ExpressionProgram`, `ExecutionPlanRunner`, AST
    evaluation, or internal force-delete helpers. Keep optional-chain deletes,
    private names, `super`, and out-of-boundary dynamic identifier deletes as
    pre-VM declines until a later slice owns selector, compiler, VM, and route
    proof for those exact shapes.

    For nested named receiver computed deletes such as `delete box.child[key]`,
    compose the existing `GetNamedProperty` receiver hops with the existing
    `DeleteComputedProperty` opcode. Do not add a VM callback or generic
    expression-stack fallback for the receiver chain.

    Optional computed delete routing is narrower than optional-chain delete as
    a family. The admitted shapes are exactly `delete box?.[key]`,
    `delete box?.child[key]`, and `delete box.child?.[key]`, with activation-resolved receivers,
    non-private named receiver hops, supported computed-key spans, and
    compiler-owned nullish short-circuit-to-true lowering. For
    `delete box?.child[key]`, emit the nullish guard before the named hop so
    the computed key is skipped on the short-circuit path. For
    `delete box?.[key]` and `delete box.child?.[key]`, recognize the existing
    `JumpIfNullish, key span, DeleteComputedProperty, Jump, Pop, true` tail and
    re-lower it to the same VM-owned short-circuit block. Keep chained optional
    delete neighbors, richer computed-key payloads, dynamic lookup, private
    names, and `super` as pre-VM declines until a later slice proves those exact
    semantics.

    Chained optional delete can use two different guard op families. A terminal
    optional hop (`delete box?.value?.leaf`) carries a normal nullish guard,
    while a non-terminal optional target-chain guard (`delete box?.child[key]`)
    carries `JumpIfShortCircuited` after the optional receiver hop. Treat
    `JumpIfShortCircuited` as a net-neutral conditional jump in first-boundary
    stack-depth tests: it skips later key/delete work but leaves stack height
    unchanged by itself. Do not model it like a pop-semantics conditional branch
    or the stack-depth proof can reject valid optional-delete bytecode even when
    the compiler and VM route are correct.

    Pair each positive widening with opcode proof, public fast-path route logs,
    computed-key coercion/order proof for computed deletes, strict/sloppy
    descriptor failure proof, and adjacent negative decline coverage. WHY:
    issue
    `planitem-planmanual1780240661926543000-burn-down-unified-bytecode-production-decl-df696bb0ff`
    / PR #2900 admitted ordinary named, nested named, and computed property
    delete (ADR 0309). The important boundary is that `DeleteDependency` was
    narrowed, not retired: descriptor-aware ordinary deletes now have owned VM
    opcodes, while optional-chain, private-name, and super delete semantics
    remain separate dependency families. Issue #gh2926 / PR #2931 then admitted
    nested named receiver computed deletes by composing existing
    `GetNamedProperty` and `DeleteComputedProperty` opcodes, while retaining the
    optional receiver, dynamic-key, and richer-key declines (ADR 0316).
    Issue #gh2934 / PR #2938 then admitted the first optional computed delete
    shapes by reusing `JumpIfNullishReplaceUndefined`, `Pop`,
    `LoadLiteral(true)`, named receiver reads, and `DeleteComputedProperty`
    rather than adding an optional-delete VM callback. The durable lesson is
    that optional delete is not a broad gate lift: selector and compiler
    predicates must match the exact short-circuit-to-true expression-program
    shape, prove the computed key is skipped when the receiver is nullish, and
    leave adjacent optional delete forms declined (ADR 0317).
    Faktorial issue
    `planitem-planmanual1780639098493226000-full-unified-bytecode-execution-burndown-b-31d21ea9a4`
    / PR #3255 then admitted chained optional named delete (`delete a?.b?.c`)
    and repaired the stack-depth guard after the accepted optional-computed
    delete route exposed that `JumpIfShortCircuited` is a neutral guard in this
    model, not a stack-consuming branch.

53. When admitting labeled `break` or `continue` that crosses nested
    iterator/for-in driver loops to production unified bytecode, make cleanup
    descriptor-topology-backed instead of target-name, active-state-only, or
    program-counter-order based. Each compiled driver descriptor must carry its
    cleanup/break target and an explicit continue target for the active driver.
    The VM should resolve leading cleanup-chain opcodes (`PopEnvironment` and
    `LeaveWith`) for both the abrupt target and descriptor break/continue
    targets, close every currently active driver whose descriptor lifetime is
    exited by that effective target, and sort active driver states by
    `ActiveDriverOrdinal` descending so cleanup runs inner-to-outer.

    Keep `break` and `continue` cleanup classification separate. A `break`
    target closes the matched exited driver and any deeper active drivers. A
    `continue` target keeps the target driver open and closes only crossed inner
    drivers or drivers whose body no longer contains the cleanup-chain-resolved
    target. Do not infer this distinction from numeric program counter ordering
    or from the active state slot alone; carry the abrupt kind and descriptor
    target through the VM cleanup call.

    Keep the backedge admission narrow and compiler-owned: direct jumps,
    completion-value pass-through, `LeaveTry`, `EndFinally`, and
    continue-targeted `PopEnvironment` cleanup into an active driver continue
    or `MoveNext` target are valid only when the surrounding driver body
    topology is still proven by the existing compiler reconstruction checks. Do
    not reintroduce program-counter heuristics for multi-driver cleanup, and do
    not satisfy a missed cleanup case by calling back into
    `ExecutionPlanRunner`, `ExpressionProgram`, or AST evaluation.

    Pair every widening with route proof and observable cleanup proof: labeled
    for-of continue across an inner driver should close the inner iterator only,
    labeled for-of break across nested drivers should close exited iterators
    inner-to-outer, for-in labeled continue should route through production, and
    unsupported async/awaited driver-source shapes should remain pre-VM
    declines. WHY: issue
    `planitem-planmanual1780240661926543000-burn-down-unified-bytecode-production-decl-9e9f5025f7`
    / PR #2914 retired the ADR 0285 driver-crossing residue for synchronous
    nested drivers. The first safe model was not a looser label gate; it was
    carrying driver `MoveNextTarget` topology into descriptors and using that
    topology to decide which active driver lifetimes a control target exits
    (ADR 0313). Follow-up issue
    `planitem-planmanual1780240661926543000-burn-down-unified-bytecode-production-decl-1daee0b11e`
    / PR #2915 split driver break and continue cleanup classification after a
    crossing-continue proof showed that a single target model kept the wrong
    driver lifetime. Future changes must preserve explicit `ContinueTarget`
    metadata and pass the abrupt kind into cleanup selection (ADR 0314).

54. When admitting expression-level assignment destructuring through
    `ExpressionOpKind.ApplyBindingTarget` in production unified bytecode, keep
    it a descriptor-backed bridge rather than a general fallback. The compiler
    may lift an already-lowered `BindingTargetProgram` into
    `UnifiedBytecodeProgram.BindingTargetConstants` and emit
    `ApplyBindingTarget`; the VM must own the opcode dispatch, duplicate the RHS
    when expression stack semantics require the assignment value to remain
    available, sync unified slots to the activation environment before applying
    the descriptor, then sync the environment back to unified slots afterward.
    The bridge must call `ApplyLoweredAssignmentBindingTargetProgram(...)` with
    `allowNameInference: false`, matching the existing expression-runner
    assignment path. Do not
    use this bridge to admit generic binding declarations, descriptor-ineligible
    destructuring targets, dynamic-name target families, or unsupported driver
    shapes; those remain pre-VM declines under `DestructuringDependency` or the
    narrower owning decline. Pair each widening with positive route proof,
    stack-shape proof for assignment result preservation, abrupt/default/rest or
    nested-target behavior as applicable, and a no-name-inference regression for
    existing anonymous function values. WHY: issue
    `planitem-planmanual1780240661926543000-burn-down-unified-bytecode-production-decl-c537361518`
    / PR #2941 admitted descriptor-backed `ApplyBindingTarget` assignment
    destructuring into ordinary sync production routing. Review found the first
    bridge call inferred a binding-target name for an existing anonymous
    function value; the accepted repair set `allowNameInference: false` and
    added a production-fast-path regression. ADR 0318 records the bounded bridge
    decision.

Related ADRs:
- `docs/adrs/0181-keep-unified-bytecode-prototype-ir-owned-and-all-or-nothing.md`
- `docs/adrs/0186-keep-unified-bytecode-function-kind-eligibility-explicit.md`
- `docs/adrs/0189-keep-unified-bytecode-linear-expression-packs-flattened.md`
- `docs/adrs/0192-keep-unified-bytecode-acyclic-control-flow-compiler-owned.md`
- `docs/adrs/0201-keep-unified-bytecode-production-routing-decline-first.md`
- `docs/adrs/0204-keep-unified-bytecode-sync-production-routing-slot-bridged.md`
- `docs/adrs/0205-keep-unified-bytecode-binary-production-eligibility-operator-explicit.md`
- `docs/adrs/0208-keep-unified-bytecode-branch-production-routing-shape-discriminated.md`
- `docs/adrs/0210-keep-unified-bytecode-control-flow-production-routing-operator-and-shape-owned.md`
- `docs/adrs/0218-keep-unified-bytecode-property-read-production-boundary-selector-owned.md`
- `docs/adrs/0221-keep-unified-bytecode-property-reads-vm-owned-and-observable.md`
- `docs/adrs/0222-keep-unified-bytecode-two-hop-named-property-read-boundary-owned.md`
- `docs/adrs/0224-keep-unified-bytecode-shape-probes-side-effect-free-before-emission.md`
- `docs/adrs/0231-keep-unified-bytecode-property-write-private-names-guarded.md`
- `docs/adrs/0234-keep-unified-bytecode-property-writes-strict-and-directive-owned.md`
- `docs/adrs/0238-keep-unified-bytecode-compound-property-writes-get-for-set-owned.md`
- `docs/adrs/0246-keep-unified-bytecode-expansion-contract-source-of-truth-and-drift-guarded.md`
- `docs/adrs/0247-keep-unified-bytecode-activation-value-loads-call-time-owned.md`
- `docs/adrs/0251-keep-unified-bytecode-iterator-and-destructuring-drivers-model-first.md`
- `docs/adrs/0252-keep-unified-bytecode-completion-lane-vm-owned.md`
- `docs/adrs/0253-keep-unified-bytecode-loop-control-targets-compiler-owned.md`
- `docs/adrs/0255-keep-unified-bytecode-block-lexical-scopes-program-slot-owned.md`
- `docs/adrs/0258-keep-unified-bytecode-completed-lanes-integrated-at-production-boundary.md`
- `docs/adrs/0271-keep-unified-bytecode-exception-regions-vm-owned-and-driver-cleanup-topology-guarded.md`
- `docs/adrs/0277-keep-resumable-unified-bytecode-state-bounded-and-yield-star-declined.md`
- `docs/adrs/0279-accept-this-dependent-ordinary-sync-in-unified-bytecode.md`
- `docs/adrs/0283-accept-this-dependent-async-generator-in-resumable-unified-bytecode.md`
- `docs/adrs/0285-admit-labeled-control-flow-in-unified-bytecode-and-decline-driver-crossing.md`
- `docs/adrs/0286-accept-unified-bytecode-construct-calls-and-decline-super-calls.md`
- `docs/adrs/0307-admit-bounded-super-invocation-in-production-unified-bytecode.md`
- `docs/adrs/0287-accept-unified-bytecode-spread-calls-spreadmask-indexed-and-receiver-owned.md`
- `docs/adrs/0288-admit-tdz-head-environments-for-sync-iterator-and-for-in-drivers.md`
- `docs/adrs/0289-admit-optional-calls-in-unified-bytecode-nullish-short-circuit-receiver-owned.md`
- `docs/adrs/0290-admit-array-and-object-literals-in-unified-bytecode-simple-span-measurement.md`
- `docs/adrs/0292-admit-template-literals-in-unified-bytecode-simple-span-measurement.md`
- `docs/adrs/0293-admit-logical-and-nullish-expressions-in-unified-bytecode-with-peek-jump-semantics.md`
- `docs/adrs/0294-cache-plan-level-production-eligibility-permanent-decline-cross-invoker.md`
- `docs/adrs/0295-admit-property-read-short-circuit-expressions-simple-rhs-owned.md`
- `docs/adrs/0296-admit-optional-member-access-in-unified-bytecode-with-null-check-opcodes.md`
- `docs/adrs/0297-admit-conditional-ternary-expression-in-unified-bytecode.md`
- `docs/adrs/0298-admit-multi-hop-optional-named-chains-in-unified-bytecode-jump-based-lowering.md`
- `docs/adrs/0301-admit-optional-call-chain-forms-in-unified-bytecode.md`
- `docs/adrs/0300-admit-logical-compound-assignment-on-slots-in-unified-bytecode.md`
- `docs/adrs/0302-admit-named-member-logical-assignment-in-unified-bytecode.md`
- `docs/adrs/0306-admit-class-literals-in-unified-bytecode-through-shared-class-creation.md`
- `docs/adrs/0308-admit-nested-named-property-write-receiver-chains-in-unified-bytecode.md`
- `docs/adrs/0309-admit-ordinary-property-delete-in-unified-bytecode.md`
- `docs/adrs/0313-admit-nested-driver-labeled-abrupt-cleanup-in-unified-bytecode.md`
- `docs/adrs/0314-split-unified-bytecode-driver-break-and-continue-cleanup-targets.md`
- `docs/adrs/0316-admit-nested-named-receiver-computed-delete-in-unified-bytecode.md`
- `docs/adrs/0318-admit-apply-binding-target-assignment-destructuring-bridge-in-unified-bytecode.md`
- `docs/adrs/0332-admit-resumable-try-catch-with-owned-frame-state.md`
