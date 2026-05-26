# Asynkron.JsEngine Roadmap

## Purpose
This roadmap aligns near-term implementation work with the long-term goal of building a strong Node.js competitor on .NET, while staying explicit about what is currently proven versus what is still directional.

## Current State (2026-05-26)

### What Works Well
- The engine has a clear fast-path direction: typed AST parse/analyze, lowered statement IR, and expression payloads compiled into `ExpressionProgram` bytecode.
- Expression bytecode coverage and failure-code taxonomy are documented and test-linked (`docs/expression-bytecode-coverage.md`).
- Recent profile-driven slices improved hot-path behavior in measurable ways:
  - inline small expression buffers reduced `classdef` profile cost and allocation pressure (`docs/performance/classdef-inline-expression-buffers.md`)
  - numeric binary expression-bytecode fast paths improved `ir-arithmetic` throughput (`docs/performance/ir-arithmetic-binary-fast-path.md`)
- Recent JSON compact-serialization tuning shows measurable benchmark wins while keeping runtime behavior stable and explicitly documented (`docs/performance/json-compact-serialization.md`).
- Object-literal static key normalization and backlog tracking are documented with explicit supported/unsupported boundaries (`docs/unsupported-expression-program-backlog-2026-05-21.md`, `docs/expression-bytecode-coverage.md`).
- Object and dense-array storage policy boundaries are captured as durable ADR constraints that keep follow-up optimization slices runtime-safe (`docs/adrs/0106-keep-object-literal-default-data-properties-implicit.md`, `docs/adrs/0103-keep-array-dense-writes-storage-owned.md`).
- Recent migration reporting keeps runtime/storage boundaries explicit and avoids overclaiming execution-path changes (`docs/expression-bytecode-migration-report-2026-05-23.md`).
- Activation-slot ownership and observable arguments binding seams are now documented as plan-owned/runtime-consumed boundaries (`docs/adrs/0099-keep-function-activation-slot-shape-plan-owned.md`, `docs/adrs/0100-keep-observable-arguments-binding-eval-aware-through-arrows.md`).
- Activation-focused quality guardrails are now explicit: dedicated profile entries and focused activation proof-pack coverage exist to keep optimization work evidence-first (`tools/profile-manifest.json`, `.claude/rules/function-activation-proof-pack.md`, `.claude/rules/performance-profiling-guardrails.md`).
- Function-call activation overhead closeout evidence is now captured with focused semantics, Test262 activation-adjacent coverage, profiler output, and canonical quality-gate results (`docs/function-call-activation-overhead-final-report-2026-05-23.md`).
- Typed argument-carrier ownership boundaries are now explicitly durable in ADR form, keeping optimization scope narrow and evidence-linked (`docs/adrs/0101-keep-function-call-argument-carriers-typed-through-hot-paths.md`).
- Constant-folding and expression simplification follow-up work remains constrained by existing storage and activation ADR boundaries plus the linked performance evidence in this roadmap.
- Self-referential arithmetic assignment lowering is now explicitly bounded by slot-proof guardrails: proven static-slot shapes can route through compound-slot instructions, while dynamic `with`/proxy-observable assignment semantics stay on generic assignment-reference paths (`docs/performance/ir-arithmetic-self-assignment-compound-slot.md`, `docs/adrs/0107-keep-self-referential-assignment-slot-optimization-slot-proven.md`, `docs/adrs/0108-keep-self-referential-with-assignment-dynamic.md`).
- Test262-oriented profile support now has first-class runner and manifest coverage, so slow/crash-cluster work can run through a stable profile surface instead of ad-hoc probes (`tools/ProfileRunner/Test262ProfileRunner.cs`, `tools/profile-manifest.json`).
- `JsArray.SetLength` is now explicitly JsValue-native on the hot path, with the legacy object overload retained only as an obsolete compatibility tripwire while follow-up slices remove remaining object callsites (`src/Asynkron.JsEngine/JsTypes/JsArray.cs`, `docs/adrs/0114-keep-array-length-helper-jsvalue-native.md`).
- RegExp property-escape matcher/cache handling has recent targeted optimization work, which gives roadmap follow-up a concrete runtime hotspot surface to track with profile evidence (`src/Asynkron.JsEngine/JsTypes/JsRegExp.cs`).
- RegExp runtime ownership boundaries are now explicitly captured for both the bounded instance cache shape and anchored property-escape matcher scope (`docs/adrs/0112-keep-regexp-instance-cache-bounded-and-keyed-by-runtime-shape.md`, `docs/adrs/0113-keep-anchored-regexp-property-escape-single-codepoint-runtime-owned.md`).
- Additional 2026-05 evidence slices now document simple numeric expression fast-path tuning and class-definition environment pre-sizing, tightening the current-state link between runtime changes and measured profile outcomes (`docs/performance/ir-arithmetic-simple-numeric-expression-fast-path.md`, `docs/performance/classdef-ir-environment-pre-sizing.md`).
- Recent string-append rope follow-through reduced `stringops` cost while keeping flattening consumer-driven and runtime-owned; the remaining gap is explicitly tracked in evidence rather than inferred (`docs/performance/stringops-rope-append-fast-path.md`, `docs/adrs/0120-keep-string-append-rope-flattening-consumer-driven.md`).
- Recent compliance and helper-surface slices tightened spec-order and runtime-boundary ownership in hot paths: Array reduce/reduceRight/some result helpers are JsValue-native, computed-member nullish read order is explicitly spec-ordered, and descriptor-backed delete strictness behavior is runtime-owned with focused regression proof (`docs/adrs/0118-keep-array-reduce-some-result-helpers-jsvalue-native.md`, `docs/adrs/0119-keep-computed-member-nullish-read-order-spec-ordered.md`, `docs/adrs/0121-keep-descriptor-delete-result-semantics-strictness-owned.md`).
- Recent lazy `JsArgumentsObject` materialization follow-through for class definitions is now explicitly captured with profile evidence and a durable activation-owned ADR boundary, so recurring maintenance runs can reference an already-settled seam without re-reading PR history (`docs/performance/classdef-lazy-arguments-object.md`, `docs/adrs/0124-keep-lazy-arguments-object-materialization-observable-and-profile-owned.md`).
- Tail-call completion handling now explicitly preserves restart semantics through branch-expression and `finally` completion handoff paths, with a durable ADR boundary for follow-up optimization work (`docs/adrs/0139-keep-tail-restarts-through-expression-branches-and-finally-completions.md`, `src/Asynkron.JsEngine/Ast/TypedAstEvaluator.ExecutionPlanRunner.Completion.cs`).
- Sync IR call trampoline eligibility is now explicitly executor-exact and evidence-pinned, keeping recursive-call optimization claims narrow while preserving runtime correctness boundaries (`docs/performance/fib-trampoline-eligibility.md`, `docs/adrs/0140-keep-sync-ir-trampoline-eligibility-executor-exact.md`, `src/Asynkron.JsEngine/Ast/TypedAstEvaluator.SyncIrCallTrampoline.cs`).
- Scope-entry lexical TDZ slot-index stamping is now plan-owned and runtime-consumed before symbol fallback, reducing repeated lookup cost on proven shapes while keeping dynamic semantics guarded (`docs/performance/destructuring-lexical-slot-tdz.md`, `docs/adrs/0142-keep-scope-entry-tdz-slot-marking-plan-owned.md`, `src/Asynkron.JsEngine/Ast/TypedAstEvaluator.ExecutionPlanRunner.Handlers.Scope.cs`).
- HTMLDDA string coercion precedence is now explicitly guardrailed so `IIsHtmlDda` classification remains ordered before callable/accessor object string coercion paths (`docs/adrs/0141-keep-htmldda-string-coercion-precedence-in-jsops.md`, `src/Asynkron.JsEngine/Runtime/JsOps.cs`).
- Break/continue static validation now shares one label-aware validator path, reducing drift between statement owners while keeping syntax-rejection behavior explicit and test-owned (`docs/adrs/0138-keep-break-continue-static-validation-shared-and-label-aware.md`, `src/Asynkron.JsEngine/Ast/ControlFlowSyntaxValidator.cs`).
- Revoked proxy `apply`/`construct` behavior now explicitly routes TypeError creation through the current realm with operation-specific messaging, tightening cross-realm correctness boundaries for future runtime slices (`docs/adrs/0137-keep-revoked-proxy-apply-construct-errors-current-realm.md`).
- Objectcreation now has explicit known-new object-literal property fast-path evidence, with compiler-proven boundaries captured in ADR 0145 to keep optimization claims narrow and runtime-safe (`docs/performance/objectcreation-known-new-property-fast-path.md`, `docs/adrs/0145-keep-known-new-object-literal-property-fast-path-compiler-proven.md`).
- Classdef now has explicit single-argument array callback fast-path evidence; remaining callback invocation and boxing cost is documented as follow-up work rather than treated as solved (`docs/performance/classdef-array-callback-single-arg.md`).
- Array iteration callback-argument setup now has explicit dense-array follow-through evidence, keeping callback semantics stable while reducing `arrayops` callback callsite overhead (`docs/performance/arrayops-dense-iteration-callback-args.md`).
- Recursive one-argument typed call dispatch now has explicit `fib`-profile evidence, narrowing hot-path dispatch cost without widening host-call shortcuts (`docs/performance/fib-single-argument-typed-call-dispatch.md`).
- Resizable TypedArray smoke coverage now has a shared fixture-policy ADR boundary, reducing cross-suite drift while keeping semantics unchanged (`docs/adrs/0147-keep-resizable-typedarray-smoke-fixtures-shared.md`).

### What Works Worse
- Statement execution still relies on record-backed `ExecutionPlan.Instructions`; compact statement storage is diagnostics-oriented rather than runtime-active today.
- Unsupported statement-family buckets remain in migration diagnostics, so compact runtime-routing and record-backed removal are not yet low-risk.
- Dynamic/eval seams and activation-sensitive behavior still require careful handling to avoid correctness regressions while optimizing hot paths.

### What Could Improve Next
- Continue lowering-time normalization in the unsupported statement families so more instruction shapes become execution-ready without mixed AST/IR fallback seams.
- Keep removing allocation-heavy or indirect hot-path seams before broader storage/routing flips.
- Strengthen recurring evidence loops (focused diagnostics, seam scans, memory checks) so optimization progress is measurable and comparable across runs.

## Short-Term Goals
1. Burn down unsupported statement instruction families identified by current storage diagnostics.
2. Use Test262 profile packs to prioritize the highest-impact slow/crash clusters, then convert findings into narrow, evidence-backed runtime slices.
3. Continue JsValue/object-overload hot-path cleanup in targeted slices, proving each step with profile and allocation evidence before widening.
4. Extend proven expression-bytecode optimization slices (inline expression buffers, numeric binary fast paths, static object-key normalization, and JSON serialization follow-through) using profile and coverage evidence as the acceptance gate.
5. Track activation/eval-sensitive changes with narrow, repeatable proof packs before broadening to larger sweeps.
6. Keep activation profile loops current for each optimization slice so allocation and hot-path movement are measured rather than inferred.
7. Keep function-call activation slices aligned to ADR 0101 ownership boundaries and report-backed proof signals before broad runtime rewrites.
8. Preserve runtime truth boundaries while improving compact storage readiness and honoring object/array storage ADR constraints (no premature claim that compact storage is the runtime contract).
9. Continue assignment-lowering slices by prioritizing slot-proven self-referential arithmetic and bitwise paths, while keeping dynamic/no-cache identifier semantics explicitly non-negotiable under ADR 0107/0108.
10. Keep Test262 profile-runner coverage and profile-manifest entries aligned as new clusters are added, so recurring diagnostics remain reproducible.
11. Remove remaining obsolete object-overload tripwires only after each callsite migration stays profile-neutral and semantic-safe under ADR 0114 boundaries.
12. Continue `stringops` follow-up by isolating remaining append-loop versus consumer-flattening cost with profile-backed slices under ADR 0120 constraints.
13. Keep compliance-oriented runtime slices narrow and evidence-first for computed-member nullish ordering and descriptor-delete strictness boundaries while expanding nearby proof packs only after focused checks pass.
14. Keep tail-call/completion follow-up slices focused on preserving branch/finally restart semantics while removing only proven non-observable overhead.
15. Continue control-flow syntax validation maintenance by extending shared label-aware coverage instead of reintroducing split break/continue validator paths.
16. Preserve proxy revoked-operation realm correctness while expanding nearby proxy/runtime proof packs in narrow, operation-specific slices.
17. Continue sync IR call trampoline follow-through by prioritizing evidence-backed recursive-call overhead reductions without widening executor eligibility beyond ADR 0140 constraints.
18. Continue destructuring follow-through by targeting binding-pattern and iterator-protocol overhead only where lexical slot indices are proven, while preserving dynamic fallback semantics under ADR 0142 guardrails.
19. Keep JsOps coercion maintenance explicit: preserve HTMLDDA precedence ordering while optimizing adjacent string-conversion paths only with focused regression proof under ADR 0141 boundaries.
20. Keep FunctionCode activation work aligned with ADR 0146: preserve activation isolation as the gate for IR fast-path eligibility, and require focused proof signals before widening activation-path optimization scope.
21. Keep argument-carrier and callback-argument follow-through aligned to ADR 0101 boundaries, proving one-argument typed-dispatch and dense-array callback changes with narrow profile evidence before widening call-shape shortcuts.

## Long-Term Goals
1. Raise and sustain Test262 compatibility toward full compliance through spec-owned, subsystem-driven slices.
2. Reach Node.js-competitive runtime behavior and developer ergonomics on .NET, including module/runtime compatibility and predictable host integration seams.
3. Keep benchmark breadth and profiling discipline (CPU/allocation trends across representative scenarios) so speed/memory work remains evidence-driven over time.
4. Land compact statement-bytecode execution paths only when unsupported-family and parity evidence show safe readiness.

## Evidence Surfaces
- Current migration direction and runtime/storage boundary: `docs/expression-bytecode-migration-report-2026-05-23.md`
- Expression bytecode capability and failure taxonomy: `docs/expression-bytecode-coverage.md`
- Recent expression-bytecode optimization evidence:
  - `docs/performance/ir-arithmetic-simple-numeric-expression-fast-path.md`
  - `docs/performance/classdef-ir-environment-pre-sizing.md`
  - `docs/performance/classdef-inline-expression-buffers.md`
  - `docs/performance/ir-arithmetic-binary-fast-path.md`
  - `docs/performance/json-compact-serialization.md`
  - `docs/performance/stringops-rope-append-fast-path.md`
  - `docs/performance/classdef-lazy-arguments-object.md`
  - `docs/performance/fib-trampoline-eligibility.md`
  - `docs/performance/destructuring-lexical-slot-tdz.md`
  - `docs/performance/objectcreation-known-new-property-fast-path.md`
  - `docs/performance/classdef-array-callback-single-arg.md`
  - `docs/performance/arrayops-dense-iteration-callback-args.md`
  - `docs/performance/fib-single-argument-typed-call-dispatch.md`
  - `docs/unsupported-expression-program-backlog-2026-05-21.md`
- Storage/semantics guardrail ADRs for hot-path follow-through:
  - `docs/adrs/0103-keep-array-dense-writes-storage-owned.md`
  - `docs/adrs/0106-keep-object-literal-default-data-properties-implicit.md`
  - `docs/adrs/0114-keep-array-length-helper-jsvalue-native.md`
  - `docs/adrs/0118-keep-array-reduce-some-result-helpers-jsvalue-native.md`
  - `docs/adrs/0119-keep-computed-member-nullish-read-order-spec-ordered.md`
  - `docs/adrs/0120-keep-string-append-rope-flattening-consumer-driven.md`
  - `docs/adrs/0121-keep-descriptor-delete-result-semantics-strictness-owned.md`
  - `docs/adrs/0124-keep-lazy-arguments-object-materialization-observable-and-profile-owned.md`
  - `docs/adrs/0137-keep-revoked-proxy-apply-construct-errors-current-realm.md`
  - `docs/adrs/0138-keep-break-continue-static-validation-shared-and-label-aware.md`
  - `docs/adrs/0139-keep-tail-restarts-through-expression-branches-and-finally-completions.md`
  - `docs/adrs/0140-keep-sync-ir-trampoline-eligibility-executor-exact.md`
  - `docs/adrs/0141-keep-htmldda-string-coercion-precedence-in-jsops.md`
  - `docs/adrs/0142-keep-scope-entry-tdz-slot-marking-plan-owned.md`
  - `docs/adrs/0145-keep-known-new-object-literal-property-fast-path-compiler-proven.md`
  - `docs/adrs/0146-keep-functioncode-activation-isolation-ahead-of-ir-fast-paths.md`
  - `docs/adrs/0147-keep-resizable-typedarray-smoke-fixtures-shared.md`
- Activation boundary decisions for call setup and arguments behavior:
  - `docs/adrs/0099-keep-function-activation-slot-shape-plan-owned.md`
  - `docs/adrs/0100-keep-observable-arguments-binding-eval-aware-through-arrows.md`
  - `docs/adrs/0101-keep-function-call-argument-carriers-typed-through-hot-paths.md`
- Function-call activation closeout evidence:
  - `docs/function-call-activation-overhead-final-report-2026-05-23.md`
- Activation proof/profile guardrails:
  - `.claude/rules/function-activation-proof-pack.md`
  - `.claude/rules/performance-profiling-guardrails.md`
  - `tools/profile-manifest.json`
- Test262 profiling runner surface:
  - `tools/ProfileRunner/Test262ProfileRunner.cs`
- RegExp hotspot implementation surface:
  - `src/Asynkron.JsEngine/JsTypes/JsRegExp.cs`
  - `docs/adrs/0112-keep-regexp-instance-cache-bounded-and-keyed-by-runtime-shape.md`
  - `docs/adrs/0113-keep-anchored-regexp-property-escape-single-codepoint-runtime-owned.md`
- Assignment-lowering optimization proof and semantic guardrails:
  - `docs/performance/ir-arithmetic-self-assignment-compound-slot.md`
  - `docs/adrs/0107-keep-self-referential-assignment-slot-optimization-slot-proven.md`
  - `docs/adrs/0108-keep-self-referential-with-assignment-dynamic.md`
- Historical Test262 gap inventory (not a current pass/fail baseline): `docs/remaining-test262-gaps.md`
- Active diagnostics/runtime implementation surfaces:
  - `src/Asynkron.JsEngine/Execution/StatementInstructionStorageDiagnostics.cs`
  - `src/Asynkron.JsEngine/Execution/StatementInstructionDiagnosticsCodec.cs`
  - `src/Asynkron.JsEngine/Ast/TypedAstEvaluator.ExecutionPlanRunner.Completion.cs`
  - `src/Asynkron.JsEngine/Ast/ControlFlowSyntaxValidator.cs`

## Baseline Notes
- Historical Test262 counts in `docs/remaining-test262-gaps.md` are directional context only and must not be treated as current regression results.
- This roadmap is a documentation slice and does not itself assert new runtime behavior.
