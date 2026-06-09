# Function Activation Proof Pack

Before changing function-call activation setup, run the named internal proof pack
for activation semantics:

```bash
rtk dotnet test tests/Asynkron.JsEngine.Tests --filter "FullyQualifiedName~ActivationSemanticsProofPackTests"
```

## Rules

1. Treat activation changes as semantically high-risk even when the goal is only
   overhead reduction. The proof pack must stay green before claiming the change
   preserves JavaScript behavior.
2. Keep the pack broad across activation families: sloppy and strict
   `arguments`, parameter aliasing, default/rest/destructured parameters, nested
   closures, direct eval, `with` / dynamic scope, strict vs sloppy `this`,
   generators, async functions, and async generators.
3. If activation work changes one of those semantics, update
   `ActivationSemanticsProofPackTests` in the same delivery so the named filter
   remains the focused confidence gate for future agents.
4. Do not replace this narrow internal proof with Test262-only evidence. Test262
   can widen confidence after the focused pack passes, but the named pack is the
   fast regression gate for this subsystem.
5. When optimizing lazy `arguments` creation, prove the observable-binding split
   explicitly: ordinary body `arguments`, parameter-default `arguments`, direct
   eval in the body, direct eval in parameter defaults, nested-arrow
   `arguments`, and nested-arrow direct eval. Arrow functions inherit the
   enclosing activation's `arguments`; nested non-arrow functions are the
   boundary.
6. Keep `argumentsObjectNeeded` separate from `NeedsArgumentsBinding`.
   `argumentsObjectNeeded` is the spec activation decision for creating and
   protecting the arguments object/binding; `NeedsArgumentsBinding` is an
   optimization guard for whether ordinary execution can observe that binding.
   Do not gate arguments-object creation solely on `NeedsArgumentsBinding`, and
   do not let hoisted `var arguments` replace an existing non-lexical
   `arguments` object binding with `undefined`.
7. When optimizing arity-specific sync calls, keep struct argument carriers on
   concrete generic paths until parameter binding consumes them. Do not pass
   `TwoValueArgs`, `ThreeValueArgs`, or similar readonly struct lists through
   `IReadOnlyList`-typed hot helper parameters or locals, because that boxes the
   struct and reintroduces the allocation the optimization is trying to remove.
   If the arity reduction is for an Array iteration callback, also prove the
   callback cannot observe omitted arguments before switching from the full
   `(value, index, array)` or reducer
   `(accumulator, value, index, array)` carrier to a narrower carrier. Ordinary
   functions, rest parameters, parameter expressions, async/generator callbacks,
   and callbacks with explicit index or array parameters must stay on the full
   observable argument path. For `reduce`/`reduceRight`, a two-argument reducer
   carrier is only valid for guarded simple arrow callbacks that cannot observe
   callback-local `arguments`, `index`, or `array`.
8. When binding parameters into an activation that already has slot storage,
   update the planned parameter slots directly. Do not call
   `DefineParameterFast` as a closure mirror for those parameters; it appends a
   new binding and can reintroduce per-invocation `JsSlot[]` growth. Use
   `DefineParameterFast` only for dictionary/no-slot fallback paths or real
   appended activation bindings.
9. When pre-sizing function or parameter environments, count only activation
   bindings that the runner will append on that exact path. Capacity reservations
   may avoid `GrowSlots`, but they must not change logical slot count, binding
   order, or whether a binding exists.
10. Keep activation fast-path assertions tied to stable behavior, negative
   fallback signals, or measured allocation evidence. Do not require optional
   activation trace logs when the optimized path may validly skip creating an
   activation object or omit the trace.
11. When skipping concrete `JsArgumentsObject` materialization for performance,
    keep the optimization guarded by both `argumentsObjectNeeded` and
    `NeedsArgumentsBinding`. The former owns the spec-shaped activation
    decision; the latter owns whether the binding/object can be observed on the
    current path. Do not replace this with a direct body scan, and do not weaken
    direct eval, parameter-default, dynamic-scope, nested-arrow, or hoisted
    `var arguments` proofs.
12. When changing script-mode FunctionCode IR gating, invocation-environment
    pooling, or recursive activation reuse, prove both sides of the boundary:
    `Name=FunctionCode` for declaration/parameter/arguments instantiation
    semantics and the focused strict same-function tail-call test for stack
    stability. Function declaration conflicts with parameters or `arguments`
    are activation-isolation signals; they are not permission to disable all
    strict recursive IR fast paths.
13. When allowing arrow functions onto simple IR activation paths, require the
    lowered simple return `ExpressionProgram` to prove that the arrow has no
    lexical `this`, lexical `new.target`, or `super` dependency. Do not use
    source size, parameter count, or callback arity alone as permission for the
    activation shortcut. Pair the positive simple-arrow value-semantics proof
    with negative lexical binding coverage so dependency-bearing arrows keep
    full arrow invocation semantics.
14. When adding direct-return shortcuts that skip invocation context,
    environment, or sync trampoline frame setup, require the ordinary simple IR
    activation eligibility plus a plan-proven return shape. A no-argument
    literal-return shortcut may return before activation setup only when
    `ExecutionPlan` proves the one-literal return. A no-argument
    parameter-return shortcut may return `undefined` before activation setup
    only when `ExecutionPlan` proves the expression is exactly one
    parameter-slot load and the call supplies zero arguments; supplied
    arguments, locals, globals, `arguments`, and multi-op expressions must stay
    on the ordinary path. Both shapes must keep the invoker guard rejecting
    constructor/class, async/generator, `arguments`, captured activation,
    dynamic scope, private-name, `super`, home-object, and instance-field cases.
    Pair the positive direct-return proof with negative activation-observable
    coverage.
15. When optimizing `activation-arguments` paths where `arguments` is
    observable, do not skip `JsArgumentsObject` materialization. Keep the object
    and binding semantics from ADR 0100/0124, and limit the optimization to
    profile-proven setup work such as descriptor capacity reservations or
    plan-proven no-op hoist work. Strict-mode Annex B blocked-name construction
    can be skipped because the legacy hoist path is sloppy-only; function-body
    hoist scans can be skipped only when `HoistableDeclarationsPlan` proves no
    hoistable declarations. If the residual owner is lexical-name setup, use
    `HoistPlan` cached templates as the source of truth: `BodyLexicalTemplate`
    owns the lexical-minus-simple-catch set, and mutable lexical/catch sets
    should be built only for consumers that mutate or retain them, such as
    `JsEnvironment` body-lexical checks or `HoistVarDeclarations`. Do not pool
    those retained sets across generator, async, or async-generator suspension
    unless the ownership boundary is separately proven. Do not replace these
    guards with source scans, benchmark-name checks, or runner-local AST
    predicates.
16. When pooling or reusing sync IR activation environments, treat pool return
    as a lifecycle ownership change. Rent and return only transient ordinary
    sync invocation environments whose lifetime ends with `RunSync` or the
    simple IR activation fast path. Do not return the caller-owned closure root,
    script-mode, generator, async, async-generator, or constructor activation
    state that was not created and owned by that exact fast path, and do not
    reuse a caller context when private-name scope replay requires a distinct
    callee context. Pair the allocation win with a
    captured-closure regression, recursive binding/strict-self fallback tests,
    and selected-profile before/after allocation evidence.
17. When adding parameter-only binary or binary-chain direct-return shortcuts,
    extend `ExecutionPlan` return-shape metadata and
    `ActivationSemanticsProofPackTests` together. A caller fast path that
    returns before invocation context setup may handle only exact arity and
    runtime operands it directly models, such as all-number arguments for a
    numeric chain. Coercing inputs and omitted parameter values must either use
    the shared JavaScript operator helpers behind the existing simple
    IR/trampoline eligibility guard or fall back to ordinary activation. Pin the
    positive numeric shortcut, the coercion-correct fallback, and negative
    non-simple shapes in the proof pack. Do not infer eligibility from source
    text, function names, or benchmark names.
18. When allowing class or object-literal methods with a home object onto the
    simple IR activation path, require the lowered simple return
    `ExpressionProgram` to prove that the method has no `super` operation.
    A home object alone is not an activation-observable dependency for a plain
    simple-return method, because ordinary receiver `this` binding is already
    supplied by simple activation. `super` access is the dependency that must
    keep the method on the full invocation path. Keep private-name scopes,
    explicit super constructor/prototype state, instance fields, async/generator
    shapes, parameter expressions, `arguments`, captured activation, and
    dynamic identifier-cache cases on their existing fallbacks. Do not replace
    the bytecode-owned super scan with source text, method names, benchmark
    names, or callback-shape predicates.
19. When optimizing root activation TDZ setup for functions with a planned slot
    layout, keep lexical slot-index metadata plan-owned. `ActivationSlotShape`
    may carry precomputed root lexical slot indices, and runtime setup may mark
    those indices directly before falling back to symbol-set enumeration for
    layouts without index metadata. Do not derive this in `ExecutionPlanRunner`
    from source text, benchmark names, or runner-local AST predicates. When
    trimming activation bindings, gate generator-only internals such as
    `YieldResumeContext` and the generator-instance back-reference on the
    actual generator runner kind; ordinary sync functions must not define or
    pre-size those bindings.
20. When optimizing numeric `arguments[i]` reads, keep the shortcut owned by
    `JsArgumentsObject`. A generic computed-property helper may route numeric
    keys to `JsArgumentsObject.TryGetIndex`, but that owner must preserve
    mapped sloppy parameter reads, direct unmapped data descriptors, accessor
    descriptors, deleted indices, prototype fallback, and the original
    receiver for ordinary `[[Get]]`. Do not treat `activation-arguments`
    evidence as permission to skip observable arguments-object materialization
    or to handle arguments objects as dense arrays. Pair the performance
    evidence with activation proof-pack coverage for mapped arguments,
    strict unmapped arguments, accessor descriptors, and prototype fallback.
    WHY: issue `autrun-dit3byldulw0-d4ff922b20` / PR #2249 optimized
    `activation-arguments-lite` by adding a direct numeric read path, and the
    correctness boundary was preserving descriptor/prototype/mapping semantics
    while avoiding generic property-key conversion.
20a. When making `JsArgumentsObject` initial index descriptors lazy, treat the
     synthetic index properties as ordinary own properties at every observable
     slow path. Resolve only canonical index strings such as `"0"`; `"00"` and
     other parsed numeric aliases are ordinary properties, not lazy index
     aliases. Materialize affected indices before descriptor/enumeration,
     assignment, delete, `defineProperty`, and extensibility operations. For
     `Object.seal(arguments)` and `Object.freeze(arguments)`, materialize all
     remaining initial indices before the integrity mutation and refresh tracked
     descriptors from the backing object afterward so descriptor flags,
     `Object.isSealed`, and `Object.isFrozen` are current. Pair retained
     changes with proof-pack coverage for descriptor/enumeration visibility,
     non-canonical numeric names, `preventExtensions`, seal/freeze, mapped and
     unmapped values, materialized delete, and prototype fallback. WHY: issue
     `autrun-diubg9b2tezc-a6f082fc8b` / PR #2546 retained a 16.2%
     `activation-arguments-lite` win by virtualizing initial index descriptors,
     but build-back repairs caught both the `"00"` alias bug and stale
     seal/freeze lazy descriptor flags. Related ADR:
     `docs/adrs/0265-keep-arguments-lazy-index-descriptors-observable-and-integrity-synced.md`.
20b. When optimizing direct `arguments.length` reads, keep the shortcut owned
     by `JsArgumentsObject` and valid only while the initial length property is
     untouched. Assignment, delete, and `defineProperty` on `length` must
     disable the direct value before falling back to ordinary backing-object
     behavior, so later reads observe data-property mutation, deletion,
     descriptor effects, and prototype fallback. A generic property helper may
     ask a concrete arguments object for the untouched length value, but it must
     not own the mutation state or treat every property named `length` as a
     direct read. Pair retained changes with proof-pack coverage for untouched
     length, mutation, deletion, prototype fallback, and still-direct numeric
     index reads. WHY: issue `autrun-diupj7a8kdqg-135f5b6bd4` / PR #2612
     retained an 11.7% `activation-arguments-lite` median win by bypassing
     generic lookup for untouched `arguments.length`, while preserving
     mutation/prototype semantics through fallback. Related ADR:
     `docs/adrs/0276-keep-arguments-length-direct-read-untouched-and-descriptor-aware.md`.
21. When adding or widening base-class-constructor simple IR activation fast
    paths, keep eligibility aligned with the binder and environment contract.
    `BindSimpleIrActivationParameters` is a positional simple-identifier
    binder; rest, default, destructured, parameter-expression, observable
    `arguments`, dynamic-lookup, derived-constructor, home-object, explicit
    `super`, and captured-private-scope shapes must stay on the existing
    fallback unless the owning binder/environment setup is widened and proved.
    The constructor fast path must also preserve `this`, `new.target`, active
    function binding, instance initialization, throw propagation, and transient
    activation-environment ownership. Pair retained changes with class/super
    semantics, non-simple-parameter negative coverage, selected-profile timing,
    and the AST-eval seam scan. WHY: issue
    `autrun-dit813mc4a00-66ec91b3ab` / PR #2302 initially admitted rest
    parameters into the base-constructor fast path until review-back added the
    missing `_hasOnlySimpleIdentifierParameters` guard.
22. When adding or widening derived-class-constructor simple IR activation fast
    paths, keep `super()` as the owner of construction and `this`
    initialization. A derived constructor fast path may create only the
    transient function/body activation environment pair, bind simple identifier
    parameters into planned slots, mark `this` uninitialized, define
    `new.target`, active function, and `SuperBinding`, and then run the lowered
    plan so the existing `SuperConstruct` expression instruction performs
    construction and `this` initialization. Default derived constructors,
    rest/default/destructured parameters, parameter expressions, observable
    `arguments`, dynamic lookup, lexical-this capture, home-object semantics,
    private scopes, captured private scopes, instance fields, async/generator
    shapes, and missing super bindings must stay on the full invocation path
    unless the owning binder/environment and `super()` semantics are separately
    widened and proved. Pair retained changes with class/super semantics,
    non-simple-parameter negative coverage, selected-profile timing, the
    AST-eval seam scan, and `forloop --memory`. WHY: issue
    `autrun-dithx7y92pu8-176deb2370` / PR #2384 retained a 52% focused
    `classdef` improvement by creating only the derived activation environment
    while leaving `super()` construction semantics on the existing runner
    instruction; earlier broader classdef construction shortcuts had failed or
    remained unproven.
23. When migrating ordinary sync-function `this` binding setup, keep the
    non-arrow slow invocation path `JsValue`-native end to end. Treat the
    caller-supplied `thisValue`, any local bound/coerced receiver, and the
    function environment's stored `this` value as `JsValue`. Use a typed
    non-strict coercion helper for nullish-to-global and primitive boxing
    semantics; do not reintroduce `object? initialThisValue`,
    `object? boundThis`, or boxed primitive cache helpers such as
    `JsValueCache.GetBoolean` / `JsValueCache.GetNumber` in private activation
    `this` setup. Pair retained changes with activation proof-pack coverage for
    strict/sloppy primitive `this` behavior and derived-constructor `super()`
    initialization, plus a scoped legacy-carrier search in
    `SyncFunctionInvoker`. WHY: issue `autrun-ditio0qdfdmo-14faf7d798` /
    PR #2393 removed three leftover object/boxed-cache matches from
    `SyncFunctionInvoker`; the semantic risk was preserving primitive
    strict/sloppy receiver coercion while not regressing derived constructor
    initialization.
24. When dispatching class-constructor simple IR activation fast paths from the
    generic sync invocation path, gate helper entry by constructor shape before
    calling the helper predicate. Try the derived helper only for class
    constructors with `_isDerivedClassConstructor`; try the base helper only for
    class constructors without `_isDerivedClassConstructor`. Keep the helper
    predicates from ADR 0225 and ADR 0230 as the semantic eligibility boundary,
    but do not make ordinary functions, arrows, class methods, or the wrong
    constructor shape pay impossible helper probes in the hot path. For the
    simple derived constructor environment, define
    `Symbol.LexicalThisEnvironment` to the transient function environment that
    owns uninitialized `this`, so `super(...)` can resolve the
    this-initialization owner directly while preserving existing fallbacks.
    Pair retained changes with repeated selected-profile timing, focused
    class/super semantics, the runner AST-eval seam scan, and `forloop
    --memory`. WHY: issue `autrun-diu14wtxo3eo-3299efe044` / PR #2456 found
    that the retained base/derived constructor fast paths made non-class
    `classdef` tail calls pay impossible class-constructor probes, and that the
    simple derived constructor path could bind its existing lexical-this owner
    directly for a 16% focused `classdef` improvement without broadening
    constructor eligibility.
25. When evaluating simple IR activation fast-path eligibility in
    `CanUseSimpleIrActivationFastPath`, use the IR observability flags
    (`_usesArguments`, `_needsArgumentsBinding`) to gate arguments-related
    cases — do NOT add `_argumentsObjectNeeded` as a fast-path eligibility
    guard. These answer different questions: `_argumentsObjectNeeded` is the
    spec-level compiler flag for whether an arguments object must be created
    (true for virtually all non-arrow functions); the IR observability flags
    reflect whether the compiled IR plan actually accesses the arguments binding.
    When both IR observability flags are false, the plan contains no argument
    access and the arguments object may be skipped entirely on the fast path,
    regardless of the spec flag. Rule 11's dual-guard requirement applies to
    **lazy materialization on the slow invocation path** (where the object may
    still be needed); it does not apply to fast-path eligibility (where the
    object is bypassed entirely). WHY: issue `autrun-diuvwweuwsrs-e67e789465` /
    PR #2646 found that `_argumentsObjectNeeded = true` was blocking closures
    from the fast path even when they never used `arguments`, causing
    `CastHelpers.Box` to consume 85.7% of `InvokeWithContextSlow` time
    (6.77x speedup after removal). Related ADR:
    `docs/adrs/0280-keep-simple-ir-fast-path-eligibility-ir-observability-guarded.md`.
26. When retiring class-constructor runner fallback residue, keep constructor
    fallback ownership split from ordinary sync-function fallback ownership and
    from script-mode FunctionCode IR seams. Class constructors may attempt
    production unified bytecode first, but a production decline must fall
    through to the existing dynamic-scope constructor evaluator, not construct a
    class-constructor `ExecutionPlanRunner` or call `runner.RunSync()`. The
    remaining ordinary sync fallback runner is a separate E5d row and must not
    be used as evidence that constructor residue remains. When narrowing
    FunctionCode IR-seam bypasses in the same area, allow production unified
    bytecode only when the plan is eligible and the function has no observable
    `arguments`, no needed arguments binding, no legacy tail-restart reset
    vars, and no strict block-scoped function-declaration instruction. Pair the
    tombstone with `BytecodeProofManifestTests`, focused class-constructor
    semantics, and focused FunctionCode quality proof. WHY: Faktorial issue
    `planitem-planitem-gh3495-shared-context-e5b-runner-entry-point-tombstones-after-e-e3f725d87e`
    / PR #3554 retired `CreateClassifiedClassConstructorFallbackRunner`, then
    quality-gate repair had to tighten the FunctionCode bypass after
    observable `arguments`, legacy restart reset, and strict block-function
    shapes were incorrectly treated as production-UBC-safe. Related ADR:
    `docs/adrs/0380-retire-class-constructor-runner-fallback-through-dynamic-constructor-evaluator.md`.
27. When pinning ordinary FunctionCode/direct-eval replacement surfaces around
    runner-removal work, keep the route assertion owned by the activation proof
    pack, not only a feature-local eval test. Assert the observable JavaScript
    result and name the currently accepted post-decline route explicitly. Before
    the ordinary sync runner tombstone, the accepted routes were
    `classified-ordinary-sync-function-fallback` with a production
    unified-bytecode decline reason or `unified-bytecode-production-fast-path`,
    while `Executing sync function via dynamic-scope executor` was rejected.
    After the ordinary sync runner tombstone, the same proof must instead reject
    the retired classified ordinary sync fallback log and accept the current
    replacement route (`Executing sync function via dynamic-scope executor`) or
    a later `unified-bytecode-production-fast-path`. Do not lock the proof to one
    currently selected owned route unless the issue specifically changes that
    routing choice. WHY: Faktorial issue
    `planitem-gh3560-batch-1-pin-the-replacement-surface-batch-4-function-code-and-act-e772459f34`
    / PR #3576 promoted the route contract into
    `ActivationSemanticsProofPackTests`; batch 6 later tombstoned the ordinary
    sync runner, so the durable contract became "no retired runner fallback"
    while still allowing future production-bytecode admission to replace the
    dynamic-scope route without failing the proof.

## Why

Issue `planitem-planmanual1779530433702731000-reduce-function-call-activation-overhead-p-8b2aee3a48`
and PR #1636 added `ActivationSemanticsProofPackTests` after the
function-call activation overhead plan identified many easy-to-break edge cases.
Ordinary functions and generator/async-generator activation paths are separate,
and mapped `arguments`, non-simple parameters, direct eval, `with`, mode
differences, and resumable functions can regress independently. Future
activation-overhead work needs one explicit, cheap proof gate before broader
quality or Test262 runs.

Issue
`planitem-planmanual1779530433702731000-reduce-function-call-activation-overhead-p-3c8725f1f9`
and PR #1637 showed the lazy-arguments trap directly: an optimization that only
looks for syntactic `arguments` in the immediate body misses direct eval and
nested arrows, both of which can observe the enclosing function's binding. The
durable rule is to prove the observable-binding decision, not just allocation
avoidance.

Related ADR: `docs/adrs/0100-keep-observable-arguments-binding-eval-aware-through-arrows.md`.

Issue `autrun-dit3byldulw0-d4ff922b20` / PR #2249 added the numeric
`arguments[i]` read fast path after profiling showed repeated computed
arguments reads under `JsOps.TryGetPropertyValueJsValue`. The durable lesson is
to keep the optimization inside `JsArgumentsObject`, where mapped parameters,
tracked data descriptors, accessor descriptors, and prototype fallback are all
known. Related ADR:
`docs/adrs/0211-keep-arguments-object-index-reads-storage-owned-and-descriptor-aware.md`.

Issue #1750 / PR #1789, reviewed again during the #gh1806 red-main learn pass,
showed the inverse lazy-arguments trap directly:
`function f() { return typeof arguments; var arguments = 42; }` still needs the
sloppy function arguments object at the return point even though the later
`var arguments` declaration is unreachable and has no executed initializer. The
runtime must create/protect the spec arguments object from
`argumentsObjectNeeded`, define it in the body/execution environment when slot
layout could otherwise shadow it, and preserve that binding during var hoisting.
Future work in this area should prove both the internal hoisting regression
(`HoistingTests.VarDeclaration_ShouldNotOverride_ArgumentsObject_BeforeReturn`)
and the activation proof pack, then use focused Test262 coverage when the
failing file is available.

Related ADR: `docs/adrs/0100-keep-observable-arguments-binding-eval-aware-through-arrows.md`.

Issue
`planitem-planmanual1779530433702731000-reduce-function-call-activation-overhead-s-f3dc144c31`
and PR #1657 showed the arity-carrier trap directly: after the simple sync
activation fast path landed, `functioncalls-lite --memory` still reported
`TwoValueArgs` / `EmptyValueArgs` helper allocations until the typed call path
preserved generic struct carriers through `SyncFunctionInvoker` and used
`Array.Empty<JsValue>()` for the runner placeholder. Future activation-overhead
work should prove both the activation proof pack and the allocation table for
helper carriers.

Related ADR:
`docs/adrs/0101-keep-function-call-argument-carriers-typed-through-hot-paths.md`.

Issue
`planitem-planmanual1779530433702731000-reduce-function-call-activation-overhead-s-8f7318b9d0`
and PR #1648 showed the slot-backed closure trap directly: after activation
slots were initialized to the planned shape, duplicate `DefineParameterFast`
calls in parameter binding appended extra parameter slots on every invocation.
The focused proof pack stayed green after writing the existing slots directly,
and `functioncalls-lite --memory` showed `JsSlot[]` sampled allocations dropping
from the previously recorded roughly 57k range to 7,781x while `arrayops`
remained stable at 170x. Future activation-overhead work should pair the proof
pack with allocation evidence for `JsSlot[]` / `GrowSlots` whenever it changes
slot-backed binding.

Related ADR:
`docs/adrs/0099-keep-function-activation-slot-shape-plan-owned.md`.

Issue `autrun-dir4p2zvmkps-4dd788bea7` and PR #1740 showed the activation
backing-capacity trap in the `classdef` profile: class constructors repeatedly
grew slot arrays while appending predictable function/parameter environment
bindings. The accepted fix kept binding order unchanged and only reserved enough
backing capacity for known appends, then proved the change with class-focused
tests, slot/environment tests, runner AST-seam scan, and `forloop --memory`.
Future activation sizing work should keep that distinction explicit.

Related performance note:
`docs/performance/classdef-ir-environment-pre-sizing.md`.

Issue `autrun-dirl74ca7a0g-8d6fc2682c` / PR #0 applied the same carrier rule
outside activation setup by routing array iteration callbacks through
`ThreeValueArgs`. The reusable lesson is unchanged: the struct is only
allocation-free while it stays concrete through the hot callback path.

Issue `autrun-dis3ezcjxsm0-238752b986` / PR #1949 showed the next trap in the
same callback path: after `ThreeValueArgs` removed array allocation, the
`classdef` profile still paid for index/array callback arguments that simple
arrow callbacks could not observe. The fix was deliberately narrower than a
generic callback-length shortcut: only non-async, non-generator arrows with zero
or one simple identifier parameter and no parameter expressions use
`SingleValueArgs`. The full three-argument path remains the semantic owner for
ordinary functions that expose `arguments`, rest parameters, and callbacks that
name the index or array. Future callback-arity optimizations should pair the
profile evidence with positive value-semantics tests and negative observable
extra-argument tests.

Related ADR:
`docs/adrs/0101-keep-function-call-argument-carriers-typed-through-hot-paths.md`.

Issue #2076 / PR #2088 showed the reducer follow-up: after `FourValueArgs`
made the full reducer path allocation-stable, the `arrayops` memory profile
still showed repeated callback-shape `HashSet<Symbol>` allocation and full
reducer carrier work for simple `(acc, value) => ...` arrows. The accepted fix
cached the constructor-stable eligibility predicate and used `TwoValueArgs`
only for guarded simple typed reducer arrows. Future callback-arity work should
prove reducer callback observability separately from map/filter callback
observability and keep ordinary/rest/default/destructured/async/generator
callbacks on the full path.

Related ADR:
`docs/adrs/0101-keep-function-call-argument-carriers-typed-through-hot-paths.md`.

Issue #1754 / PR #1759 first corrected the proof-pack trace-log trap by
renaming the affected test to match its stable negative fast-path assertions
after focused attempts to require a positive activation trace log failed.
Issue #1758 / PR #1762 then confirmed the same trap through a quality-gate
failure because
`ActivationSemanticsProofPackTests.SimpleSyncFunction_UsesIrActivationFastPath`
required an activation trace log that was no longer guaranteed after valid
activation fast-path work. The repair kept the semantic proof focused by
asserting stable negative fast-path signals instead of the optional trace.
Future activation tests should prove the behavior that must stay true and avoid
turning diagnostics into contractual runtime output.

Issue `autrun-dirquxckeg74-0fe6957821` / PR #1811 optimized the `classdef`
profile by skipping `JsArgumentsObject` allocation for functions where
`argumentsObjectNeeded` is spec-eligible but `NeedsArgumentsBinding` proves the
binding is unobservable. The durable lesson is that lazy materialization is a
profile-owned activation optimization, not a simplification of the arguments
semantics split from ADR 0100. Future work should preserve the two-decision
shape and prove the observable-binding cases explicitly before claiming an
arguments-object allocation win.

Related ADR:
`docs/adrs/0124-keep-lazy-arguments-object-materialization-observable-and-profile-owned.md`.

Issue #1866 / PR #1921 fixed Test262 `FunctionCode` execution-context rows by
treating function-declaration/parameter conflicts and recursive observable
activation reuse as narrow fast-path eligibility signals. The repair bounced
through a quality-gate stack overflow when the guard became too broad and
blocked strict same-function tail-call handling. The durable activation lesson
is to prove FunctionCode instantiation semantics and tail-call stack behavior
together whenever recursive activation reuse or script-mode IR gating changes.

Related ADR:
`docs/adrs/0146-keep-functioncode-activation-isolation-ahead-of-ir-fast-paths.md`.

Issue `autrun-dis8iqooxge8-6666a730d6` / PR #1985 showed the next `classdef`
callback activation trap: after callback arity was narrowed, the simple arrow
`dogs.map(d => d.speak())` still paid full invocation because all arrows were
rejected from simple IR activation. The accepted fix permits only arrows whose
simple return bytecode has no `this`, `new.target`, or `super` operations, and
keeps dependency-bearing arrows on the full path. Future simple-arrow
activation work should scan the lowered expression program, prove positive
callback value semantics, and pin negative lexical binding behavior.

Related ADR:
`docs/adrs/0150-keep-simple-arrow-ir-activation-lexical-dependency-guarded.md`.

Issue `autrun-dise0lhwhiaw-0099fde2a6` / PR #2027 showed the no-argument
literal-return activation trap: `activation-noargs-lite` was spending most of
the selected hot path in sync invocation context and trampoline frame setup even
though the function's lowered plan returned a literal. The accepted fix kept the
shortcut behind the existing simple IR activation guard and plan-owned
`SimpleReturnLiteral` metadata, then proved the positive literal path and the
activation semantics pack. Future direct-return work should preserve that
plan-proven boundary instead of weakening activation-observable fallbacks.

Related ADR:
`docs/adrs/0159-keep-noargs-literal-return-fast-path-plan-proven.md`.

Issue #2040 / PR #2047 widened that same boundary by exactly one shape:
`return a;` may use the pre-context direct-return path only when the lowered
plan proves a single parameter-slot load and the call has zero arguments. The
negative proof matters as much as the positive one because any supplied
argument, local/global read, `arguments` access, or broader expression would
make skipped activation setup observable. Future direct-return widenings should
extend `ExecutionPlan` metadata and `ActivationSemanticsProofPackTests` together
instead of adding invoker-local source or AST predicates.

Related ADR:
`docs/adrs/0159-keep-noargs-literal-return-fast-path-plan-proven.md`.

Issue `autrun-disk07x5rsi8-08cbb528b9` / PR #2103 showed the
observable-arguments setup trap: `activation-arguments-lite` was strict-mode
code that actively read `arguments`, so the accepted optimization retained the
arguments object and instead removed descriptor resize churn plus strict-mode
Annex B/no-hoist setup work. Future activation-arguments work should preserve
observable arguments materialization, keep descriptor pre-sizing capacity-only,
and skip hoist work only from strict-mode or cached-plan facts.

Related ADR:
`docs/adrs/0173-keep-observable-arguments-setup-profile-owned-and-plan-proven.md`.

Issue #2123 / PR #2132 showed the follow-up lexical-template trap: after ADR
0173 removed descriptor and no-op hoist setup costs, the focused CPU profile
still showed `HashSet<Symbol>.ConstructFrom` under activation environment setup.
The accepted fix used cached `HoistPlan` templates, treated
`BodyLexicalTemplate` as the body-lexical source of truth, and built mutable
hoist working sets only when `HoistableDeclarationsPlan` proved a hoist
consumer. Future activation-arguments work should not reintroduce unconditional
lexical/simple-catch set cloning on the no-hoist path.

Related ADR:
`docs/adrs/0183-keep-activation-lexical-name-templates-hoist-owned.md`.

Issue #2080 / PR #2121 showed the sync IR activation pooling trap: the
`closures-lite` and `recursion-lite` allocation gap was largely transient
activation environment/context setup, but returning every environment created
during activation would corrupt captured closure state or callee context state.
The accepted fix pooled only invocation-owned ordinary sync activation
environments, stopped before the caller closure root, reused the caller context
only when no private-name scope replay was involved, and pinned the touched
closure path with a captured counter regression. Future activation pooling work
should prove ownership and capture boundaries before treating environment
creation as mechanically returnable.

Related ADR:
`docs/adrs/0176-keep-sync-ir-activation-environment-pooling-ownership-guarded.md`.

Issue #2083 / PR #2128 showed the parameter binary-chain direct-return trap:
`activation-params-lite` looked like a simple numeric benchmark, but the safe
shortcut had to preserve three boundaries at once. `ExecutionPlan` owns the
five-op parameter-chain proof, the number-only caller fast path owns only the
exact numeric three-argument runtime case, and the coercing path still reuses
the shared binary operator helpers behind the existing trampoline eligibility
guard. Future activation-param return-chain work should prove those positive
and negative paths in the focused activation pack before claiming it can skip
context or trampoline setup.

Related ADR:
`docs/adrs/0178-keep-activation-params-binary-chain-fast-path-plan-and-runtime-guarded.md`.

Issue `autrun-disrk4l293k0-661ecf4a43` / PR #2175 showed the home-object
method trap in the `classdef` profile: after simple arrows were already allowed
onto simple IR activation, the `dogs.map(d => d.speak())` tail still paid full
typed method invocation because any `_homeObject` rejected the shortcut. The
accepted fix kept the semantic guard bytecode-owned: methods with a home object
may use simple activation only when their simple return program contains no
`super` operations. Future class-method activation work should preserve that
receiver-versus-super distinction and prove class/super behavior before
claiming a retained optimization.

Related ADR:
`docs/adrs/0193-keep-class-method-simple-ir-activation-super-guarded.md`.

Issue `autrun-dit4lwfdshqo-b26070bcec` / PR #2266 later tested removing only
the class-method home-object fast-base invalidation and relying on the existing
super-free guard from ADR 0193. The semantic guard remained important, but the
focused `classdef` timings regressed and the runtime edit was reverted. Future
class-method activation work should preserve ADR 0193's receiver-versus-super
distinction while still proving that any change to `SetHomeObject` invalidation
or simple activation fast-base state is a current performance win. WHY: a
correct eligibility boundary is not proof that a specific fast-base invalidation
removal pays for itself in the current runner.

Related ADR:
`docs/adrs/0214-keep-classdef-homeobject-and-construct-retries-profile-proven.md`.

Issue `autrun-disu408v4ns8-3128f4f119` / PR #2198 showed the final
activation-arguments root-TDZ trap in the same optimizer chain: after ADR 0173
and ADR 0183 removed observable-arguments descriptor, no-op hoist, and lexical
template cloning costs, `activation-arguments-lite` still spent time under
`ResetSlotLayoutForPlan -> MarkSlotsLexicalUninitialized` enumerating immutable
lexical symbol sets. The accepted fix moved root lexical slot indices into
`ActivationSlotShape` and used direct index marking, while also gating
`YieldResumeContext` / generator-instance bindings so ordinary sync functions
do not pay for generator-only activation state. Future work should keep this
plan-owned metadata boundary and prove generator paths separately.

Related ADR:
`docs/adrs/0199-keep-activation-tdz-slot-indices-plan-owned-and-generator-state-gated.md`.

Issue `autrun-diuvwweuwsrs-e67e789465` / PR #2646 found the spec-vs-IR fast-path
eligibility trap: `_argumentsObjectNeeded` was blocking closures from
`CanUseSimpleIrActivationFastPath` because it is true for all non-arrow
functions, even those whose IR plan never accesses `arguments`. The CPU profiler
showed `CastHelpers.Box` consuming 85.7% of `InvokeWithContextSlow` time —
every closure call boxed a `SingleValueArgs` struct to `IReadOnlyList<JsValue>`
when falling through to `ExecutionPlanRunner`. The fix was a 1-line removal:
`_argumentsObjectNeeded` is the spec creation flag, not an IR observability
guard; `_usesArguments` and `_needsArgumentsBinding` already own all observable
cases. Removing it yielded a 6.77x speedup for `activation-closures-lite` and
improvements across `activation-arguments-lite` and `activation-evalscope-lite`.
The durable lesson is that `_argumentsObjectNeeded` must not be used as a
conservative fast-path eligibility guard when the IR observability flags are
already in place.

Related ADR:
`docs/adrs/0280-keep-simple-ir-fast-path-eligibility-ir-observability-guarded.md`.
