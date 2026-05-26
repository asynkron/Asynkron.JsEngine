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
    change remains.
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
10. For object-literal default data-property optimizations, keep the shortcut at
    the `JsObject` storage boundary and preserve the ordinary descriptor
    fallback. Future `objectcreation` or object-literal work must prove the hot
    owner with a CPU profile, then pin descriptor visibility, insertion order,
    extensibility, virtual-provider/private-field exclusions, and later
    `Object.defineProperty` promotion semantics.
11. For compact JSON serialization optimizations, keep streaming shortcuts on
    the compact/no-gap path and preserve the semantic fallback for replacers,
    `toJSON`, raw JSON, pretty-printing, proxy-aware key enumeration, circular
    checks, and reviver source tracking. Future `json` work must prove the hot
    owner with a CPU profile, then separate avoidable intermediate string/list
    allocation from metadata that is required for observable JSON semantics.
12. For activation environment capacity work, prove that the hot owner is slot
    growth or slot-array copying on the selected profile before changing runner
    sizing. Capacity helpers may reserve space for known activation appends, but
    reports must distinguish backing capacity from logical slot count, binding
    order, and observable environment semantics.
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
     `(value, index, array)` to a narrower carrier. Keep the arity predicate
     owned by the typed callback/invoker shape, not by a generic callback
     length heuristic. Preserve the full argument path for callbacks that can
     observe omitted arguments through `arguments`, rest parameters, parameter
     expressions, async/generator execution, or explicit index/array
     parameters, and pin both value semantics and observable extra-argument
     behavior with focused tests. For reducer callbacks that must expose
     `(accumulator, value, index, array)`, keep the four-argument path
     observable but carry it through a concrete typed carrier such as
     `FourValueArgs` when the typed JavaScript invoker can consume it; do not
     allocate a temporary `JsValue[]` just to preserve the full arity.
15. For repeated string append optimizations, prove whether the selected
    `stringops` cost is append-loop flattening, consumer flattening, or another
    string built-in before changing the rope or addition path. Keep primitive
    `string + string` slot compound-add shortcuts limited to operands already
    tagged as JavaScript strings, and keep object coercion, BigInt, symbols,
    and mixed operands on the generic addition path. Do not lower the rope
    flattening depth or force append-loop flattening without current CPU
    evidence and a consumer correctness proof.
16. For sync IR trampoline performance work, separate semantically tail-position
    calls from calls the current trampoline executor can actually complete.
    Before widening eligibility, prove the selected profile's call shape
    (final return-expression call, non-final operand call, branch-condition
    call, or another shape) and update the executor before accepting broader
    eligibility. Non-tail recursive operand calls must stay on ordinary
    invocation instead of paying repeated failed trampoline setup.
17. For lexical scope-entry TDZ performance work, prove that the selected
    profile is paying repeated scope-entry metadata work before changing the
    runner. If slot layout is already known by lowering or stamping, carry
    direct lexical slot-index payloads on the environment-push instruction and
    let the runtime mark TDZ state by index. Keep symbol/set iteration and
    `SlotMap` probing as an unstamped diagnostic or compatibility fallback, not
    the ordinary hot path.
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
