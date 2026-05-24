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
7. For expression-bytecode arithmetic optimization, narrow from a broad
   benchmark table to the profile that actually owns the hot path before
   changing the runner. Use `rtk ./tools/profile <profile> --cpu` to confirm
   `EvaluateExpressionProgram` and the operator helper are hot, keep uncommon
   or mixed-type operands on the generic operator path, and compare against the
   committed/current baseline conservatively when profiler runs are noisy.
8. For expression-program buffer optimizations, prove that the profile cost is
   repeated small `EvaluateExpressionProgram` execution before changing runner
   storage. Pair the CPU call tree with a `MaxStackDepth` distribution or
   equivalent diagnostic, keep packed flag side-state intact, and preserve the
   pooled fallback for larger programs. Do not raise the inline-buffer threshold
   without current profile evidence and a frame-size/semantics proof.
9. For array dense-write optimizations, keep the shortcut at the `JsArray`
   storage boundary and preserve the ordinary property-definition fallback.
   Future `arrayops` or array built-in work must prove the hot owner with a CPU
   profile, then pin descriptor/extensibility/length-writability semantics and
   the `2^32 - 1` boundary. Numeric helpers may fast-path only indices
   `< uint.MaxValue`; `"4294967295"` is an ordinary property and must not grow
   array length.
10. For object-literal default data-property optimizations, keep the shortcut at
    the `JsObject` storage boundary and preserve the ordinary descriptor
    fallback. Future `objectcreation` or object-literal work must prove the hot
    owner with a CPU profile, then pin descriptor visibility, insertion order,
    extensibility, virtual-provider/private-field exclusions, and later
    `Object.defineProperty` promotion semantics.

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

Issue `autrun-diqwn50g1d08-a853a50d18` / PR #1682 selected `ir-arithmetic` from
a broader `rtk ./benchmark.sh` table because that profile mapped directly to
expression bytecode numeric binary operators. The accepted slice added a
number-number fast path in `ApplyProgramBinaryOperator`, kept non-number and
uncommon operators on the existing generic path, and reported the win using the
slowest final run because the profiling environment was noisy. The durable
lesson is that expression-bytecode CPU work should be profile-owned and
semantics-preserving, not a broad benchmark chase or a blanket operator rewrite.

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
