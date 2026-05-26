# Performance Profiling Guardrails

When planning or implementing performance work, start from profiles that isolate
the target cost instead of reading a broad benchmark as proof of the next
optimization.

## Rules

1. For function-call activation overhead, use the activation profiles in
   `tools/profile-manifest.json` and `tools/performance-profiler.sh`:
   `activation-noargs`, `activation-params`, `activation-arguments`,
   `activation-closures`, and `activation-evalscope`.
2. Keep CPU and memory evidence separate. A CPU hotspot in call entry does not
   prove an allocation win, and an allocation type table does not prove exact
   allocation provenance without a call tree.
2a. When one performance issue groups multiple benchmark profiles, prove and
    report the owner for each profile independently before choosing an
    implementation surface. Similar domain labels or allocation types are not
    enough evidence for a shared fix. Issue #2079 / PR #2097 grouped `json` and
    `stringops`, but profiling showed `JsonHelper` storage and
    `StringPrototype.Split` consumer materialization were separate owners, so
    the delivery changed only the string split consumer and left JSON as a
    JsonHelper-specific residual.
3. Preserve `PERF_PROFILES` override behavior when changing aggregate profiler
   defaults. Default guardrails may expand, but operators must still be able to
   run a narrowed profile set.
4. Keep activation guardrails as profiler/tooling surfaces unless the issue
   explicitly asks for runtime optimization. Do not modify invoker,
   environment, arguments-object, or parameter-binding code without current
   activation-profile evidence.
5. When reporting activation findings, name the owner surface being measured:
   environment/context creation, arguments object creation, parameter binding,
   slot growth, closure capture, eval-sensitive scope behavior, or invoker
   overhead.
6. Preserve comparable profiler baselines when a performance issue asks for a
   reduction claim. A current activation-profile run is useful evidence, but it
   only proves improvement when the matching prior profile output or committed
   baseline numbers are still available. If the baseline is missing, report
   stable/no-regression or current-hotspot evidence instead of claiming a win.
6a. For recurring optimizer work with an explicit improvement threshold, do not
    commit marginal or noisy experiments that fail repeated focused timing.
    Revert the attempted code, keep the build update evidence-only, and do not
    add performance docs that imply a retained optimization when no successful
    change remains. When a no-win run isolates concrete failed experiments,
    name them in the build update so future optimizer agents do not retry the
    same micro-slices without fresh profile evidence.
6b. Treat a cleaner CPU profile as supporting evidence, not a retained
    performance win. If an experiment removes a sampled subtree such as
    `CastHelpers.Box` but repeated selected-profile timings regress or miss the
    issue threshold, revert the code and record the failed shape. Runner-level
    argument storage is separate from ADR 0101/0171 call and construct
    boundary carriers; do not replace `ExecutionPlanRunner` argument storage
    with a copied value container unless current repeated benchmark timings
    prove that storage shape pays for itself. Issue
    `autrun-disltaszb6mo-e6c0f409f5` / PR #2122 caused this rule after the
    classdef runner argument-container attempt removed the sampled boxing
    subtree but regressed focused timings.
6c. When a performance issue's acceptance criteria require before/after
    selected-profile evidence, report the comparable rows before review/deploy:
    command, baseline source, current source, timing, allocation, and whether
    the signal is noisy. Also name the changed branch or workload shape that the
    selected profile actually executed; if the profile does not execute a
    changed branch, add a dedicated selected workload or label that branch as
    regression-covered rather than profile-proven. If the build stage omits that
    evidence, review must reconstruct it before accepting the performance claim.
    WHY: issue #2084 / PR #2127 delivered the stringops split/join allocation
    reduction, but review had to rerun PR/base `stringops` allocation rows
    because the build updates only reported semantic tests. Issue #2150 / PR
    #2155 then needed a build-back wording fix because `stringops` exercised
    `split("")`, while the non-empty separator split branch was only covered by
    focused regressions.
7. For expression-bytecode arithmetic optimization, narrow from a broad
   benchmark table to the profile that actually owns the hot path before
   changing the runner. Use `rtk ./tools/profile <profile> --cpu` to confirm
   `EvaluateExpressionProgram` and the owning helper are hot, keep uncommon
   or mixed-type operands on the generic operator path, and compare against the
   committed/current baseline conservatively when profiler runs are noisy. If a
   whole-expression-program fast path is added, treat any compile-time marker as
   a candidate hint only: the runner must still guard identifier-cache
   eligibility, `with`, `arguments`, static slot reads, and runtime number
   operands before bypassing the generic expression interpreter.
8. For expression-program buffer optimizations, prove that the profile cost is
   repeated small `EvaluateExpressionProgram` execution before changing runner
   storage. Pair the CPU call tree with a `MaxStackDepth` distribution or
   equivalent diagnostic, keep packed flag side-state intact, and preserve the
   pooled fallback for larger programs. Do not raise the inline-buffer threshold
   without current profile evidence and a frame-size/semantics proof.
9. For array dense storage optimizations, keep shortcuts at the `JsArray`
   storage boundary and preserve ordinary property fallbacks. Dense writes must
   keep descriptor/extensibility/length-writability semantics and the
   `2^32 - 1` boundary pinned. Dense iteration reads may bypass string-index
   conversion only for ordinary `JsArray` receivers with no custom indexed
   properties and a proven present own dense element; holes, inherited
   prototype values, proxies, sparse/custom descriptors, non-array receivers,
   and ordinary `HasProperty` observability must stay on the fallback path.
   Future `arrayops` or array built-in work must prove the hot owner with a CPU
   profile before widening either read or write shortcuts. Numeric helpers may
   fast-path only indices `< uint.MaxValue`; `"4294967295"` is an ordinary
   property and must not grow array length.
9a. For dense array destructuring fast paths, prove the iterator protocol is
    unobservable before bypassing the generic iterator driver. A direct
    identifier-only binding shortcut may require dense own elements and the
    native `Array.prototype.values`, but it must also guard
    `%ArrayIteratorPrototype%.next` and fall back when
    `%ArrayIteratorPrototype%.return` is present and non-nullish. Holes,
    indexed descriptors, defaults, rest elements, nested targets, custom array
    iterators, and generator/suspending contexts must stay on the semantic
    iterator path so abrupt completion still performs `IteratorClose`.
10. For object-literal default data-property optimizations, keep the shortcut at
    the `JsObject` storage boundary and preserve the ordinary descriptor
    fallback. Future `objectcreation` or object-literal work must prove the hot
    owner with a CPU profile, then pin descriptor visibility, insertion order,
    extensibility, virtual-provider/private-field exclusions, and later
    `Object.defineProperty` promotion semantics.
10a. For named-property read performance work, keep shortcuts at the runtime
     property-access and `JsObject` storage boundaries while preserving
     observable `[[Get]]` semantics. A direct `JsValueKind.Object` ->
     `JsObject.TryGetProperty` route may skip generic object-carrier dispatch
     only when it keeps the original `JsValue` receiver. `JsObject` may return a
     stored own property directly only when no accessor descriptor owns that
     name; accessors, virtual providers, prototype traversal, non-`JsObject`
     property accessors, primitive prototype reads, private-field boundaries,
     and JavaScript throw propagation stay on semantic fallbacks. Compound `+=`
     slot shortcuts that evaluate a property RHS must evaluate that RHS exactly
     once, handle pending awaits/throws through the existing runner paths, and
     use generic addition when the profiled direct add helper does not apply.
     Prove retained changes with repeated selected-profile timing and focused
     tests for own data shadowing, prototype getter receiver binding, and getter
     read count. WHY: issue `autrun-disqa6r1nle8-d1809e0cbe` / PR #2157 added
     the `propertyaccess` named-read fast path after the hot subtree was
     `HandleCompoundAssignmentSlot -> EvaluateExpressionProgram ->
     GetProgramNamedPropertyValue -> JsOps.TryGetPropertyValue ->
     JsObject.TryGetProperty*`; the win depended on ordinary-object and
     own-data proofs, not a generic property cache.
11. For JSON parse/stringify optimizations, keep shortcuts in `JsonHelper` or
    the owning runtime storage helper and preserve semantic fallbacks. Default
    data-property shortcuts may use `JsObject.DefineDefaultDataProperty` only
    when JSON is creating fresh ordinary own properties with default
    writable/enumerable/configurable attributes, including `__proto__` as data
    instead of setter behavior. Compact serialization and quoting shortcuts
    must stay on the compact/no-gap or proven no-escape path and preserve the
    semantic fallback for replacers, `toJSON`, raw JSON, pretty-printing,
    proxy-aware key enumeration, circular checks, surrogate escaping, and
    reviver source tracking. When eliminating quoted-key temporaries, append
    directly to the active compact builder only after the property value has
    been serialized and kept, and route escape-required keys through the shared
    `QuoteString` path instead of duplicating escape/surrogate handling. Future
    `json` work must prove the hot owner with a CPU profile, then separate
    avoidable intermediate string/list/descriptor allocation from metadata that
    is required for observable JSON semantics.
12. For activation environment capacity work, prove that the hot owner is slot
    growth or slot-array copying on the selected profile before changing runner
    sizing. Capacity helpers may reserve space for known activation appends, but
    reports must distinguish backing capacity from logical slot count, binding
    order, and observable environment semantics.
12a. For execution-runner control-flow side-state capacity work, prove the
     selected profile is paying stack/list growth for that side state before
     changing initial capacities. Pre-size only the owning state object, such as
     `BreakableState.BreakableStack`, and keep active-frame count, label
     matching, `break` / `continue`, cleanup / `IteratorClose`, and suspension
     semantics unchanged. Do not turn a `Stack<T>.Grow` subtree into broader
     lowering or control-flow rewrites. WHY: issue
     `autrun-dissu2eufu0w-31ee6d51c0` / PR #2189 showed first-push
     `BreakableFrame` stack growth in `activation-arguments-lite`; the retained
     fix was a capacity-only stack pre-size backed by repeated timings and a
     follow-up CPU profile.
13. For repeated RegExp matcher performance work, keep shared .NET `Regex`
    reuse keyed by the runtime bridge shape that actually executes: capped
    normalized pattern plus `RegexOptions`. Preserve per-instance `_compiledRegex`
    reuse as the first fast path, bound the shared cache, and do not convert a
    construction-cache win into broader RegExp construction laziness without a
    separate invalid-pattern timing and metadata proof.
14. For array built-in call-boundary optimizations, prove that generic
    host-call argument materialization is the hot owner before bypassing it.
    Keep shortcuts limited to generated native built-in identities and delegate
    the receiver semantics back to the owning runtime type, such as `JsArray`.
    Do not turn a one-argument `Array.prototype.push` win into a generic
    single-argument host-call shortcut or a user-visible function-name
    heuristic. Pin descriptor, prototype, proxy, extensibility, length
    writability, and maximum-length fallback behavior with focused tests.
14a. For Array iteration callback arity optimizations, prove that callback
     argument materialization is the selected profile owner before reducing
     `(value, index, array)` or reducer
     `(accumulator, value, index, array)` to a narrower carrier. Keep the arity
     predicate owned by the typed callback/invoker shape, not by a generic
     callback length heuristic. Preserve the full argument path for callbacks
     that can observe omitted arguments through callback-local `arguments`, rest
     parameters, parameter expressions, async/generator execution, or explicit
     index/array parameters, and pin both value semantics and observable
     extra-argument behavior with focused tests. For reducer callbacks that must
     expose `(accumulator, value, index, array)`, keep the four-argument path
     observable but carry it through a concrete typed carrier such as
     `FourValueArgs` when the typed JavaScript invoker can consume it; do not
     allocate a temporary `JsValue[]` just to preserve the full arity. When a
     profile names repeated callback-shape predicate allocation, cache only
     constructor-stable predicate results on the invoker owner and keep mutable
     invocation-state checks out of that cache.
15. For repeated string append optimizations, prove whether the selected
    `stringops` cost is append-loop flattening, consumer flattening, or another
    string built-in before changing the rope or addition path. Keep primitive
    `string + string` slot compound-add shortcuts limited to operands already
    tagged as JavaScript strings, and keep object coercion, BigInt, symbols,
    and mixed operands on the generic addition path. Do not lower the rope
    flattening depth or force append-loop flattening without current CPU
    evidence and a consumer correctness proof. When the current owner is
    `StringPrototype.Split` or another string consumer's result
    materialization, keep the optimization at that consumer boundary: pre-size
    result storage only after proving the constructor is capacity-only, avoid
    intermediate element arrays where observable order is unchanged, reuse
    cached single-code-unit strings only on a profiled and semantics-pinned
    path such as ASCII `split("")`, and do not turn a split/join hotspot into a
    rope or generic addition policy change. Do not claim the standard
    `stringops` selected profile proves non-empty separator split throughput
    unless the profile script actually exercises that branch; use a dedicated
    workload or keep the non-empty separator claim to semantic regression proof.
    WHY: issue #2150 / PR #2155 had to narrow its final evidence because the
    selected `stringops` rows covered `split("")` plus dense join, not the
    changed non-empty separator split branch. For `Array.prototype.join`
    stringops work, a dense fast path may use only ordinary `JsArray` receivers
    with no custom indexed properties, every index present as an own element,
    and every element already tagged as a primitive JavaScript string. Do not
    call element `ToString` in the guard or fast path; objects, holes,
    inherited values, `null`/`undefined`, proxies, array-like receivers,
    sparse/custom descriptors, length mismatches, and side-effectful coercions
    must fall back to the generic join loop. WHY: issue #2084 / PR #2127 first
    needed a repair so side-effectful element `toString` could delete a later
    element and let the generic path observe the prototype value.
16. For sync IR trampoline performance work, separate semantically tail-position
    calls from calls the current trampoline executor can actually complete.
    Before widening eligibility, prove the selected profile's call shape
    (final return-expression call, non-final operand call, branch-condition
    call, or another shape) and update the executor before accepting broader
    eligibility. Non-tail recursive operand calls must stay on ordinary
    invocation instead of paying repeated failed trampoline setup.
16a. For sync IR trampoline frame-capacity work, prove whether the selected
     owner is shallow setup, failed eligibility, or real deep-stack growth
     before changing the initial frame array size. The common shallow path
     should stay small and growth-driven; do not restore a broad eager frame
     capacity unless a current profile shows repeated growth is the cost being
     optimized. Capacity tuning must not change eligibility, active frame
     count, argument/receiver binding, expression stack state, or fallback
     behavior.
16b. For sync IR trampoline frame-pooling work, keep frame ownership explicit.
     Do not use `[ThreadStatic]`, `AsyncLocal<T>`, or shared scratch frame
     caches to avoid allocation. If top-level `SyncIrFrame[]` storage is pooled,
     growth cleanup and final cleanup must stay separate: after copying live
     frames to a new rented array, reset only the old frame structs before
     returning the old backing array, because the copied frames still own their
     nested slot, expression-stack, and flag arrays. Final cleanup must clear
     those nested arrays and non-scratch references such as `Plan` and
     `ActivationSlots` before returning the invocation's backing array.
17. For lexical scope-entry TDZ performance work, prove that the selected
    profile is paying repeated scope-entry metadata work before changing the
    runner. If slot layout is already known by lowering or stamping, carry
    direct lexical slot-index payloads on the environment-push instruction and
    let the runtime mark TDZ state by index. Keep symbol/set iteration and
    `SlotMap` probing as an unstamped diagnostic or compatibility fallback, not
    the ordinary hot path. When the payload is already `LexicalSlotIndices`,
    consume it through the environment-owned batch helper
    `SetSlotsLexicalUninitialized` instead of reintroducing a runner-local
    per-index method-call loop. WHY: issue #2201 / PR #2208 showed
    `activation-arguments-lite` still paying `HandlePushEnvironment` overhead
    after slot indices were plan-stamped because the hot path looped over each
    index instead of using the existing batch helper.
18. For object-literal known-new property optimizations, keep freshness as a
    compiler-carried proof and keep the storage shortcut owned by `JsObject`.
    A static property may skip duplicate checks only while all earlier literal
    keys are statically known and distinct. Stop the known-new proof after
    computed keys, spreads, or other unknown key-producing members, and exclude
    `__proto__` prototype mutation, accessors, methods, duplicate static names,
    and non-default property shapes. Preserve generic duplicate suppression,
    insertion order, descriptor promotion, and the ordinary
    `DefineDefaultDataProperty` fallback.
19. For recursive typed JavaScript call dispatch work, prove that the selected
    profile is paying generic callable-helper overhead before bypassing shared
    dispatch. Single-argument `SyncFunctionInvoker` calls may route directly to
    `InvokeWithContext1` only when the call context is already available and the
    callable identity is the typed JavaScript invoker. Keep host functions,
    direct eval, debug-aware host functions, spread calls, constructor
    rejection, and uncommon arities on their existing semantic paths. Because
    `fib`-style timings are noisy, report repeated before/after selected-profile
    runs plus the follow-up CPU profile shape instead of claiming a win from one
    benchmark row.
20. For simple-arrow activation work, prove that the selected profile is paying
    full invocation for a simple arrow before widening IR activation
    eligibility. Keep the eligibility predicate bytecode-owned: a simple arrow
    may use simple IR activation only when its lowered simple return program
    has no lexical `this`, lexical `new.target`, or `super` op. Preserve the
    full invocation path for dependency-bearing arrows, and report repeated
    selected-profile timings plus a follow-up CPU profile that shows the fast
    path is actually taken.
21. For simple numeric self-recursion work, do not generalize from a benchmark
    name or from the presence of self-calls. Prove the selected profile is
    still paying the full recursive invocation tree, then keep the shortcut
    shape- and runtime-guarded: strict/simple one-parameter function,
    exact base-case-plus-two-self-calls recurrence shape, live recursive-name
    binding still pointing at the same `SyncFunctionInvoker`, finite numeric
    argument, bounded integer fast input, and ordinary invocation fallback for
    non-integers, `NaN`, infinity, rebound names, class, async, generator,
    private-name, `super`, home-object, and instance-field cases. Pin both the
    positive recurrence and negative fallback behavior with focused tests.
22. For no-argument direct-return activation work, prove that the selected
    profile is paying invocation context, environment, or sync trampoline frame
    setup before bypassing it. Keep literal and omitted-parameter direct returns
    owned by `ExecutionPlan` return-shape metadata and the existing simple IR
    activation guard, not source text or benchmark names. When widening a
    retained shortcut, keep baseline/final samples tied to the widened slice and
    label them as no-regression evidence if the selected profile still mostly
    exercises the earlier shape. Report repeated focused timing, the CPU owner,
    and the activation proof-pack result before claiming the shortcut is
    retained.
23. For parameter-only binary or binary-chain activation work, prove that the
    selected profile is paying call setup, context setup, or sync trampoline
    frames before bypassing it. Keep the return shape owned by `ExecutionPlan`
    metadata over the lowered `ExpressionProgram`, not source text or benchmark
    names. A pre-context caller shortcut may cover only exact arity and runtime
    operands it directly models, such as number-only `JsValue` arguments for
    `(a + b) ^ c`; coercing or omitted-argument cases must use shared
    JavaScript operator helpers behind the existing simple IR/trampoline
    eligibility guard. Unsupported operators, locals, globals, `arguments`,
    dynamic/private/super/home-object, class, async, generator, and captured
    activation cases must stay on existing fallback paths. When wall-clock
    timings are noisy, report the CPU owner shape, such as disappearance of
    `InvokeWithContextSlow` or trampoline frames, plus the focused activation
    proof pack.
24. For repeated direct-eval program-cache performance work, prove that the
    selected `activation-evalscope` profile is paying parse and plan-build cost
    under `EvalHostFunction` before caching. Cache parsed `ProgramNode`
    instances and warmed AST caches, not eval environments, declaration
    instantiation, execution results, caller bindings, or validation outcomes.
    Key the cache by source text plus forced strictness, keep it per engine and
    bounded, and keep private-name validation, `super` / `new.target`
    validation, and current caller-environment execution after the cache
    lookup. A lock-free last-entry front cache may bypass the LRU lock only
    when it mirrors that exact key and stores only the immutable `ProgramNode`;
    keep misses and less stable source patterns on the bounded LRU path. Prove
    retained wins with repeated selected-profile timings plus the focused
    activation/eval/private-name proof pack.
25. For class constructor and `super()` dispatch performance work, prove the hot
    owner with a current `classdef` CPU profile before editing
    `ExecuteProgramSuperConstruct`, super-constructor resolution, or construct
    dispatch. Keep argument and spread evaluation order, constructor and
    `new.target` validation, proxy behavior, derived-`this` initialization,
    double-super errors, and class-field initialization on the existing
    semantic paths. Internal sentinel slots such as `Symbol.ThisInitialized`
    may use a direct `JsValue` kind fast path only when the expected kind is
    checked first and the prior `JsOps.ToBoolean(...)` or semantic fallback is
    preserved for every other value. Do not turn a sampled constructor/super
    subtree into broad activation widening or constructor-dispatch bypass
    without focused class/super and lowering proof.
26. For activation-arguments TDZ or generator-state performance work, prove the
    selected profile is paying activation setup under
    `InvokeWithContextSlow -> CreateExecutionEnvironment` before editing root
    slot layout, `JsEnvironment.ResetSlotLayoutForPlan`, or generator-internal
    activation bindings. Keep TDZ marking metadata compiler/plan-owned through
    `ActivationSlotShape` lexical slot indices, with the symbol-set path only as
    a fallback for layouts without index metadata. Keep generator-only bindings
    gated by the actual runner kind; do not infer the shortcut from the
    benchmark name, source text, or an ordinary-function profile alone. Report
    repeated selected-profile timing, the follow-up CPU owner shape, the
    activation proof pack, and generator-focused coverage.

## Why

Issue
`planitem-planmanual1779530433702731000-reduce-function-call-activation-overhead-p-d4fdf29166`
and PR #1635 added activation-focused profiling guardrails after the
investigation separated tooling from runtime optimization. The durable lesson is
that activation work needs profiles shaped around activation variants before an
agent can safely claim a function-call optimization target or regression.

Without this rule, future performance agents can overread generic
`functioncalls` or `forloop` profiles, mix CPU and memory conclusions, or edit
runtime activation code before proving which activation owner is actually hot.

Issue
`planitem-planmanual1779530433702731000-reduce-function-call-activation-overhead-p-0b25b4f88b`
and PR #1665 showed the baseline-retention failure mode during closeout. The
activation profiles and full LanguageTests throughput were captured, but the
handoff's comparable activation baseline directory was not present in the
worktree, so the report could only make a no-focused-regression/current-hotspot
claim rather than a strong activation-overhead reduction claim.

Issue `autrun-dis78sutx3qw-ee0780e761` had a bounded optimizer build pass that
tried class-construction and expression-bytecode experiments after capturing
`classdef` and `ir-arithmetic` timings. Release builds passed, but repeated
focused timings stayed below the required 10% improvement threshold and were
too noisy to justify a retained optimization. The experimental edits were
reverted and the build update stayed evidence-only. The durable lesson is that
optimizer automation should prefer no code commit over preserving marginal
changes or writing performance documentation for an optimization that was not
kept.

Issue `autrun-disf6pv2ztqw-bd150c7cbc` repeated that no-win pattern on
`closures-lite`. The CPU and memory profiles pointed at simple closure
invocation/return and `CreateExecutionEnvironment` pressure, but try/finally
state null probing, captured simple IR activation, and direct captured
update-return experiments did not clear the repeated 10% timing bar. The
experimental edits were reverted and PR creation was deferred because the branch
had no retained commits. The durable lesson is that failed micro-slices are
useful evidence only when named explicitly; they are not performance
documentation and should not be retried unchanged without new owner evidence.

Issue `autrun-disltaszb6mo-e6c0f409f5` / PR #2122 repeated the no-win pattern
with a subtler signal split. The `classdef` CPU profile showed
`CastHelpers.Box` under constructor and `super(...)` invocation, and the runner
argument-container experiment removed that sampled subtree, but repeated
focused timings regressed to 1939 ms and 2143 ms. The runtime code was reverted
and the retained delivery became a failed-attempt note. The durable lesson is
that removing a sampled call-tree cost is not enough: runner storage changes
need repeated selected-profile timing proof before they become code. Related
ADR: `docs/adrs/0177-keep-runner-argument-storage-benchmark-proven.md`.

Issue `autrun-diqwn50g1d08-a853a50d18` / PR #1682 selected `ir-arithmetic` from
a broader `rtk ./benchmark.sh` table because that profile mapped directly to
expression bytecode numeric binary operators. The accepted slice added a
number-number fast path in `ApplyProgramBinaryOperator`, kept non-number and
uncommon operators on the existing generic path, and reported the win using the
slowest final run because the profiling environment was noisy. The durable
lesson is that expression-bytecode CPU work should be profile-owned and
semantics-preserving, not a broad benchmark chase or a blanket operator rewrite.

Issue `autrun-dir3mbor11cg-7a927e0289` / PR #1731 repeated the `ir-arithmetic`
profile after the binary helper and self-assignment slices. The accepted slice
added an `ExpressionProgram.IsSimpleNumericCandidate` marker plus a runner fast
path for literal/identifier/numeric-binary programs, but the marker remained a
candidate only: the runner rechecks identifier-cache eligibility, `with`,
`arguments`, slot-read availability, and number operands before bypassing the
generic interpreter. The durable lesson is that expression-program CPU fast
paths must be runtime-guarded against JavaScript's dynamic lookup and coercion
surfaces, not only shape-filtered from source or bytecode. Related ADR:
`docs/adrs/0110-keep-simple-numeric-expression-program-fast-path-runtime-guarded.md`.

Issue `autrun-diqxxf5b04kw-a516a2bc0a` / PR #1685 selected `classdef` from the
benchmark table and then proved the hot owner with a `classdef` CPU call tree:
many short expression programs were repeatedly paying expression stack/flag
buffer acquisition and ArrayPool rent/return costs. The storage diagnostic
showed all selected-program max stack depths fit within eight slots, so the
accepted slice used stack-local inline buffers for small programs and kept the
existing pooled path for larger programs. The durable lesson is that
expression-program buffer work should be driven by both runtime profile shape
and stack-depth distribution, with fallback capacity and packed flag semantics
left intact. Related ADR:
`docs/adrs/0102-keep-small-expression-program-buffers-inline.md`.

Issue `autrun-diqyh2msb568-aa393696f2` / PR #1687 selected `arrayops` from the
benchmark table and proved the hot owner with an `arrayops` CPU call tree:
ordinary dense result-array writes in `map`/`filter` and length updates in
`push` were paying descriptor setup, index string construction, and boxed length
storage costs. The accepted slice kept the optimization in `JsArray` storage,
but the build-back fix showed why the semantic guard matters: `uint.MaxValue`
is still a representable numeric value, while ECMAScript array indices stop
before `2^32 - 1`. The durable lesson is that array fast paths must stay
profile-owned and storage-owned while preserving ordinary property behavior at
`"4294967295"`. Related ADR:
`docs/adrs/0103-keep-array-dense-writes-storage-owned.md`.

Issue `autrun-dis5yv0qlsr4-6ad7f476ff` / PR #1961 selected `arrayops` again
after earlier dense-write work and proved the remaining hot owner with an
`arrayops` CPU call tree: dense iteration still paid callback dispatch plus
string-index/generic property lookup for every present element. The accepted
slice added a `TryGetExistingElement(long)` fast path only for ordinary
`JsArray` receivers with no custom indexed properties and a proven own dense
element, while a focused regression pinned inherited prototype values for
holes. The same slice introduced `FourValueArgs` for reducer callbacks so the
full four observable arguments stay available without allocating a temporary
argument array. The durable lesson is that array iteration optimization must
separate storage-owned dense element proof from observable property fallback,
and must treat full callback arity as a typed-carrier problem rather than an
argument-omission shortcut. Related ADRs:
`docs/adrs/0101-keep-function-call-argument-carriers-typed-through-hot-paths.md`
and `docs/adrs/0103-keep-array-dense-writes-storage-owned.md`.

Issue `autrun-diqzaewea814-77aafdbdd9` / PR #1692 selected `objectcreation`
from the benchmark table and proved the hot owner with an `objectcreation` CPU
call tree: ordinary object-literal data properties were paying full descriptor
setup and descriptor-dictionary writes even though their observable descriptor
is the default writable/enumerable/configurable shape. The accepted slice kept
the optimization in `JsObject.DefineDefaultDataProperty`, falling back for
non-extensible objects, virtual providers, private fields, and existing explicit
descriptors. The durable lesson is that object-literal fast paths must stay
profile-owned and storage-owned while preserving descriptor materialization and
promotion semantics. Related ADR:
`docs/adrs/0106-keep-object-literal-default-data-properties-implicit.md`.

Issue `autrun-disqa6r1nle8-d1809e0cbe` / PR #2157 selected `propertyaccess`
from the benchmark table and proved the hot owner with a CPU call tree under
compound slot assignment, expression-program named property reads, `JsOps`, and
`JsObject`. The accepted slice kept the shortcut at two runtime-owned
boundaries: direct ordinary `JsObject` reads with the original `JsValue`
receiver, and stored own data-property reads that explicitly fall back for
accessors, virtual providers, and prototypes. The flat-slot compound-add path
was likewise limited to a proven accumulator slot and a non-awaiting RHS
expression program. The durable lesson is that named-property read performance
work must stay profile-owned, storage-owned, and receiver-preserving, with
focused tests proving accessor/prototype/read-count semantics. Related ADR:
`docs/adrs/0188-keep-named-property-read-fast-paths-storage-owned-and-receiver-preserving.md`.

Issue `autrun-diqzs4qq1p5s-4e9bf90752` / PR #1696 selected `json` from the
benchmark table and proved the hot owner with a `json` CPU call tree:
`JsonHelper` was paying avoidable compact-stringify member/element list
construction and no-reviver parse-array index string construction. The accepted
slice streamed compact objects and arrays directly into a `StringBuilder` and
skipped parent metadata only when no reviver source tracker needs those keys.
The durable lesson is that JSON fast paths must be profile-owned and limited to
the non-observable compact/no-reviver cases while leaving the semantic
replacer, raw JSON, pretty-printing, proxy, circular-check, and reviver paths on
their existing fallbacks. Detailed measurement note:
`docs/performance/json-compact-serialization.md`.

Issue `autrun-discqnniowqw-45a6add5dd` / PR #2018 selected `json` again and
proved the next bounded owner with a `JsonHelper` CPU call tree: JSON-created
fresh objects and reviver wrappers were paying full descriptor setup for
ordinary default data properties, while compact string quoting still paid the
escape builder for strings that required no escaping. The accepted slice reused
`JsObject.DefineDefaultDataProperty` only for JSON-created fresh own data
properties and added a `QuoteString` no-escape fast path that still falls back
for quotes, backslashes, controls, and surrogate code units. The durable lesson
is that JSON performance work should reuse the storage-owned default-property
boundary and no-escape quoting proof only where the ECMAScript JSON algorithm
cannot observe the shortcut. Related ADR:
`docs/adrs/0158-keep-json-default-property-and-quote-fast-paths-profile-owned.md`.

Issue #2041 / PR #2048 continued the same `JsonHelper` owner surface after ADR
0158: compact object-key emission still allocated a quoted key string before
appending it to the active object builder. The accepted slice appended
no-escape keys directly to that builder, but kept escaping-required keys on the
shared `QuoteString` path so quote, backslash, control-character, surrogate-pair,
and lone-surrogate semantics stayed single-owned. The durable lesson is that
JSON key-quoting performance work may remove intermediate strings, but it must
not fork escape semantics or write keys before the existing property/value
serialization rules decide the member is observable. Related ADR:
`docs/adrs/0160-keep-json-compact-key-quoting-append-shared.md`.

Issue `autrun-dir4p2zvmkps-4dd788bea7` / PR #1740 selected `classdef` from the
benchmark table and proved the hot owner with a `classdef` CPU call tree:
constructor activation repeatedly reached
`BindFunctionParameters -> DefineSlot -> GrowSlots -> Array.Copy`. The accepted
slice pre-sized IR runner function and parameter environments for known
activation appends, while keeping the same logical slot count and binding order.
The durable lesson is that activation capacity fixes should be profile-owned and
capacity-only, then backed by activation/class semantics tests, slot/environment
tests, runner AST-seam scans, and an allocation-stability memory check. Detailed
measurement note: `docs/performance/classdef-ir-environment-pre-sizing.md`.

Issue `autrun-dissu2eufu0w-31ee6d51c0` / PR #2189 selected
`activation-arguments-lite` and proved a narrower runner side-state owner:
`HandleBreakableEnter -> Stack<BreakableFrame>.PushWithResize -> Grow` under
`InvokeWithContextSlow`. The accepted slice initialized the breakable-frame
stack with capacity four, kept break/continue frame semantics unchanged, and
confirmed the subtree disappeared from the follow-up CPU profile. The durable
lesson is that runner side-state capacity work should be profile-owned and
capacity-only, not a reason to rewrite control-flow lowering or cleanup
semantics. Related ADR:
`docs/adrs/0195-keep-runner-breakable-stack-capacity-profile-owned.md`.

Issue `autrun-dis3ezcjxsm0-238752b986` / PR #1949 selected the `classdef`
profile and found `ArrayPrototype.InvokeArrayIterationCallback` still paying
for full `(value, index, array)` callback argument materialization in the
`dogs.map(d => d.speak())` subtree. The accepted slice reduced that carrier
only for simple arrows whose extra arguments are unobservable and kept ordinary
functions, rest, index/array parameters, parameter expressions, async functions,
and generators on the full path. The durable lesson is that callback-arity
performance work needs both profile ownership and an observability predicate,
not just callback length or benchmark pressure. Related ADR:
`docs/adrs/0101-keep-function-call-argument-carriers-typed-through-hot-paths.md`.

Issue `autrun-dis4ox6n39q8-5cc99c3db3` / PR #1955 selected `fib` from the
benchmark table, then confirmed with a CPU call tree that recursive
single-argument typed JavaScript calls were still passing through
`ExecuteProgramCall -> InvokeCallableSingleArg -> InvokeCallableJsValueGeneric`
before reaching `SyncFunctionInvoker`. The accepted slice bypassed that generic
helper layer only for `SyncFunctionInvoker` calls with an available context and
left host/eval/debug/spread/constructor/multi-argument paths untouched. The
durable lesson is that recursive typed-call dispatch optimizations should be
profile-owned and arity-specific, with repeated timing evidence because the
selected profile is noisy. Detailed measurement note:
`docs/performance/fib-single-argument-typed-call-dispatch.md`.

Issue `autrun-dirl74ca7a0g-8d6fc2682c` / PR #0 optimized repeated Test262
matcher execution by caching equivalent .NET `Regex` instances after RegExp
normalization and quantifier capping. The durable lesson is that RegExp cache
keys are semantic: raw source text is not enough, cache growth must be bounded,
and construction laziness remains a separate observable-timing decision. Related
ADR: `docs/adrs/0112-keep-regexp-instance-cache-bounded-and-keyed-by-runtime-shape.md`.

Issue `autrun-dirmopwawwc8-6c6cc5263e` / PR #1786 selected `arrayops` again
after dense array writes had already been optimized. The CPU call tree showed
the remaining owner under `arr.push(i)` was single-argument generated native
host invocation, with boxing below the generic `IReadOnlyList<JsValue>` call
boundary. The accepted slice added an expression-runner shortcut only for the
native one-argument `push` shape, then delegated the observable append decision
to `JsArray.TryPushSingleFast`, which falls back for indexed descriptors,
prototype overrides or proxies, non-extensible arrays, non-writable length, and
maximum length. The durable lesson is that call-boundary fast paths must stay
profile-owned and runtime-guarded; the semantic owner still has to be the
receiver runtime type. Related ADR:
`docs/adrs/0116-keep-array-push-single-arg-host-call-shortcut-runtime-guarded.md`.

Issue `autrun-dirph659s868-e8df189b62` / PR #1799 selected `stringops` from the
benchmark table and proved the hot owner with a `stringops` CPU call tree:
repeated `result += "x"` reached `JsRopeString.Flatten` through the slow
compound-add path because the rope depth guard flattened every 32 appends. The
accepted slice kept flattening consumer-driven by raising the explicit-stack
rope depth limit and adding a primitive string/string compound-add fast path,
while leaving object coercion, BigInt, symbols, and mixed operands on generic
addition. The durable lesson is that string append fast paths must stay
profile-owned and primitive-tag guarded; widening them into coercive addition
or append-loop flattening changes JavaScript semantics and the cost model.
Related ADR:
`docs/adrs/0120-keep-string-append-rope-flattening-consumer-driven.md`.

Issue #2053 / PR #2067 repeated `stringops` after ADR 0120 and found that the
current CPU and memory owners had shifted from append-loop flattening to
consumer-side split/join work, including `CreateArrayFromStrings` list growth
under `StringPrototype.Split`. The accepted slice pre-sized split result arrays
and removed the temporary `string[]` for empty-separator split while preserving
`@@split`, limit, separator conversion, and result array ordering semantics.
The durable lesson is that a follow-up `stringops` profile can move the owner to
consumer materialization; future agents must keep that win at the consumer
instead of reopening rope or addition policy. Related ADR:
`docs/adrs/0163-keep-stringops-follow-up-consumer-materialization-owned.md`.

Issue `autrun-dirtf01zpmv4-17122917c9` / PR #1909 selected `fib` from the
benchmark table and proved the hot owner with a `fib` CPU call tree: ordinary
recursive invocation dominated, but repeated failed `SyncIrCallTrampoline`
setup still appeared under non-tail recursive operands such as
`fib(n - 1) + fib(n - 2)`. The accepted slice made trampoline eligibility
purpose-aware, keeping final return-expression calls eligible while rejecting
branch expressions and non-final return-expression calls before frame setup.
The durable lesson is that trampoline performance work must match eligibility
to executable shapes, not merely to the presence of recursive calls. Related
ADR: `docs/adrs/0140-keep-sync-ir-trampoline-eligibility-executor-exact.md`.

Issue `autrun-disgkh65fjdk-eae1e197e9` / PR #2072 selected
`activation-params-lite` and found the inverse sync-trampoline cost: the calls
were shallow and eligible, but `SyncIrCallTrampoline` eagerly allocated backing
storage for 64 frames before the first push. Reducing the initial capacity to
four frames kept deep-stack growth intact while cutting repeated selected
profile timings by about 42% on average. The durable lesson is that trampoline
capacity work should be profile-owned and capacity-only: optimize the shallow
startup case without changing eligibility, active frame semantics, or fallback
behavior. Related ADR:
`docs/adrs/0167-keep-sync-ir-trampoline-frame-capacity-shallow-first.md`.

Issue #2075 / PR #2091 extended that same owner surface from frame capacity to
frame-array pooling. Review rejected a `[ThreadStatic]` scratch cache because
repo policy disallows thread-static shared state and nested trampoline use can
reuse the same scratch storage. The follow-up lifecycle fix separated growth
cleanup from final cleanup: growth only resets old frame structs after copying
live frames, while final invocation cleanup clears nested scratch arrays and
retained `Plan` / `ActivationSlots` references before pool return. The durable
lesson is that pooled trampoline frame storage is an ownership change, not just
an allocation micro-optimization. Related ADR:
`docs/adrs/0167-keep-sync-ir-trampoline-frame-capacity-shallow-first.md`.

Issue `autrun-dirzemwhwz7s-0f70b9a325` / PR #1915 selected `destructuring`
from the benchmark table and proved the hot owner with a `destructuring` CPU
call tree: repeated block/loop scope entry was still iterating lexical binding
sets and probing `SlotMap` for TDZ marking even though slot layout was already
known. The accepted slice added direct lexical slot-index payloads to
`PushEnvironmentInstruction` for stamped block and loop scopes, leaving the
symbol/set path only for unstamped diagnostic or compatibility records. The
durable lesson is that scope-entry TDZ performance work should be profile-owned
and plan-owned: compute slot-index metadata before execution and keep the runner
on an index-marking path for known layouts. Related ADR:
`docs/adrs/0142-keep-scope-entry-tdz-slot-marking-plan-owned.md`.

Issue `autrun-dis251i1ddvc-f6f277664b` / PR #1941 selected `objectcreation`
again after ADR 0106 moved ordinary object-literal properties onto implicit
default-data storage. The CPU call tree showed the remaining cost was duplicate
bookkeeping below `DefineDefaultDataProperty -> TrackPropertyInsertion`. The
accepted slice added a compiler proof for static properties that are known-new
at that literal program point, then let `JsObject` own the optimized
`DefineKnownNewDefaultDataProperty` write and small-object insertion-order
tracking. The durable lesson is that object-literal freshness is not a generic
runtime assumption: computed keys, spreads, duplicate names, accessors, methods,
and `__proto__` must keep the conservative path unless the compiler can prove a
later static data property cannot overwrite an earlier or unknown key. Related
ADR:
`docs/adrs/0145-keep-known-new-object-literal-property-fast-path-compiler-proven.md`.

Issue `autrun-dis8iqooxge8-6666a730d6` / PR #1985 selected `classdef` from the
benchmark table and proved the hot owner with a CPU call tree: the final simple
arrow `Array.map` callback still used full sync invocation after earlier
callback-arity work. The accepted slice allowed simple IR activation only for
arrows whose lowered simple return bytecode lacks lexical `this`, `new.target`,
and `super` operations, then reported repeated `classdef` runs averaging about
36% faster than baseline plus a follow-up CPU profile showing
`TryInvokeIrFast`. The durable lesson is that arrow activation performance
work must be profile-owned and bytecode-dependency guarded, not a broad
syntactic arrow shortcut. Related ADR:
`docs/adrs/0150-keep-simple-arrow-ir-activation-lexical-dependency-guarded.md`.

Issue `autrun-dis9soiwafzk-cc111e3a78` / PR #1994 selected `fib` again after
earlier trampoline and typed-call-dispatch slices. The remaining hotspot was no
longer failed trampoline setup or generic callable dispatch; it was the full
recursive invocation tree for a strict, one-parameter, numeric recurrence. The
accepted fast path stayed deliberately narrow: it recognizes only the
base-case-plus-two-self-calls shape, verifies the current recursive name
binding still points at the same function object, bounds integer inputs, and
falls back for non-integers and invocation shapes the local numeric evaluator
does not model. The durable lesson is that recurrence shortcuts must be
executable-shape and runtime-binding guarded, not keyed to `fib`, a function
name, or the mere presence of self-calls. Related ADR:
`docs/adrs/0152-keep-simple-numeric-self-recursion-fast-path-shape-and-binding-guarded.md`.

Issue `autrun-dise0lhwhiaw-0099fde2a6` / PR #2027 selected
`activation-noargs-lite` from the comparison table, then confirmed with a CPU
call tree that the remaining owner was sync activation and trampoline setup for
simple literal-return functions. The accepted slice returned the plan-proven
literal before full context and frame setup, kept dynamic activation cases on
the existing path, and reported repeated focused timings roughly 56-59% faster
than the baseline. The durable lesson is that even tiny activation shortcuts
need profile ownership plus plan-owned semantic guards. Related ADR:
`docs/adrs/0159-keep-noargs-literal-return-fast-path-plan-proven.md`.

Issue #2040 / PR #2047 widened the no-argument direct-return boundary from
literals to one omitted-parameter return shape. Review sent the evidence back
until the performance note separated the original literal-return measurements
from the widening run and recorded a baseline/final `activation-noargs-lite`
pair for the selected slice. The durable lesson is that follow-up performance
docs must state which slice the numbers prove and avoid presenting a
no-regression profile pair as a fresh throughput win. Related ADR:
`docs/adrs/0159-keep-noargs-literal-return-fast-path-plan-proven.md`.

Issue #2083 / PR #2128 returned to `activation-params-lite` after the sync IR
trampoline frame-capacity work. The accepted slice kept the new `(a + b) ^ c`
shortcut split between plan-owned binary-chain metadata, a trampoline-eligible
generic direct-return path for coercion-correct semantics, and an exact
number-only three-argument caller fast path for the selected profile. Final
benchmark samples were noisy, so the durable evidence was the CPU call tree
showing `InvokeWithContextSlow` disappeared for the numeric caller path plus
the focused activation proof pack. The durable lesson is that parameter-chain
activation shortcuts must be plan-proven, runtime-guarded, and profile-owned
rather than inferred from the benchmark body. Related ADR:
`docs/adrs/0178-keep-activation-params-binary-chain-fast-path-plan-and-runtime-guarded.md`.

Issue #2076 / PR #2088 selected `arrayops` again after earlier dense callback
carrier work and proved the current owners with CPU and memory profiles:
callback invocation still sat under `Map`, `Filter`, and `Reduce`, while the
memory profile showed repeated `HashSet<Symbol>` allocation under callback
eligibility. The accepted slice cached the simple callback eligibility predicate
on `SyncFunctionInvoker` and used `TwoValueArgs` only for guarded simple typed
reducer arrows, keeping observable callbacks on the full four-argument path. The
durable lesson is that callback-arity follow-ups can move from carrier
allocation to predicate allocation; fix the current profiled owner without
widening the semantic predicate. Related ADR:
`docs/adrs/0101-keep-function-call-argument-carriers-typed-through-hot-paths.md`.

Issue `autrun-disb2md7n23s-0c3cde8865` / PR #1999 selected `destructuring`
again and proved the hot owner with a CPU call tree: dense array binding
declarations were spending most of the selected profile under
`BindArrayPatternProgram`, iterator lookup, and array iterator `next`. The
accepted slice added a direct dense `JsArray` identifier-binding path plus
standard iterator-result reuse, but review required a build-back guard for an
observable `%ArrayIteratorPrototype%.return`. The durable lesson is that dense
destructuring fast paths must be profile-owned and iterator-protocol guarded:
native `values` and dense own elements are not enough if `next` or `return` can
be observed during normal stepping or abrupt `IteratorClose`. Related ADR:
`docs/adrs/0154-keep-dense-array-destructuring-fast-path-iterator-observable.md`.

Issue `autrun-disol3tkc92g-850bc475d9` / PR #2142 selected
`activation-evalscope-lite` and proved the hot owner with a CPU call tree:
repeated stable direct eval source was reparsed and rebuilt through
`EvalHostFunction.Invoke -> JsEngine.ParseProgram -> ScriptPlanCache.Build`.
The accepted slice cached parsed `ProgramNode` instances in a bounded
per-engine LRU keyed by source text and forced strictness, then kept declaration
instantiation, private-name validation, caller-context validation, and execution
on the current eval environment. The durable lesson is that direct-eval parse
caches may reuse immutable program structure, but must not cache caller state or
use a source-only key. Related ADR:
`docs/adrs/0185-keep-direct-eval-program-cache-strictness-and-caller-context-owned.md`.

Issue `autrun-disvdy376ya8-659d67a91b` / PR #2212 returned to
`activation-evalscope-lite` after the bounded eval program LRU existed. The CPU
owner was then repeated hits still entering `EvalHostFunction.GetOrParseProgram`
through the locked LRU/dictionary path. The accepted follow-up added a
lock-free single-entry last-program cache ahead of the LRU, keyed by the same
source text and forced strictness pair and backed by the existing LRU on misses.
The durable lesson is that direct-eval cache-hit shortcuts can remove cache
machinery cost only when they preserve the same semantic key and continue to
avoid caching caller state, declaration instantiation, validation, or execution
results. Related ADR:
`docs/adrs/0185-keep-direct-eval-program-cache-strictness-and-caller-context-owned.md`.

Issue #2183 / PR #2185 continued the `classdef` constructor/super-dispatch
follow-through after ADR 0193. The retained change was a kind-guarded boolean
fast path for `Symbol.ThisInitialized` inside `ExecuteProgramSuperConstruct`;
non-boolean values still use the previous `JsOps.ToBoolean(...)` fallback. The
durable lesson is that internal sentinel fast paths can trim hot derived
constructor checks only when they are profile-owned, kind-guarded, and do not
change `super()` dispatch semantics or evaluation order. Related ADR:
`docs/adrs/0194-keep-super-constructor-thisinitialized-guard-boolean-fast-path.md`.

Issue `autrun-disu408v4ns8-3128f4f119` / PR #2198 selected
`activation-arguments-lite` after prior observable-arguments and lexical
template wins, then proved the remaining hot owner with a CPU call tree:
`CreateExecutionEnvironment -> ResetSlotLayoutForPlan ->
MarkSlotsLexicalUninitialized` still enumerated lexical symbol sets on every
ordinary function activation, and the ordinary path also paid for
generator-only internal bindings. The accepted slice moved root lexical TDZ
metadata into plan-owned `ActivationSlotShape` slot indices and gated generator
state to actual generator runners. The durable lesson is that follow-up
activation setup wins should remove the currently profiled setup owner without
widening observable arguments or generator activation policy. Related ADR:
`docs/adrs/0199-keep-activation-tdz-slot-indices-plan-owned-and-generator-state-gated.md`.
