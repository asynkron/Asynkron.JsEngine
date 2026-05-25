# Asynkron.JsEngine Roadmap

## Purpose
This roadmap aligns near-term implementation work with the long-term goal of building a strong Node.js competitor on .NET, while staying explicit about what is currently proven versus what is still directional.

## Current State (2026-05-25)

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
- RegExp property-escape matcher/cache handling has recent targeted optimization work, which gives roadmap follow-up a concrete runtime hotspot surface to track with profile evidence (`src/Asynkron.JsEngine/JsTypes/JsRegExp.cs`).
- Additional 2026-05 evidence slices now document simple numeric expression fast-path tuning and class-definition environment pre-sizing, tightening the current-state link between runtime changes and measured profile outcomes (`docs/performance/ir-arithmetic-simple-numeric-expression-fast-path.md`, `docs/performance/classdef-ir-environment-pre-sizing.md`).

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
  - `docs/unsupported-expression-program-backlog-2026-05-21.md`
- Storage/semantics guardrail ADRs for hot-path follow-through:
  - `docs/adrs/0103-keep-array-dense-writes-storage-owned.md`
  - `docs/adrs/0106-keep-object-literal-default-data-properties-implicit.md`
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
- Assignment-lowering optimization proof and semantic guardrails:
  - `docs/performance/ir-arithmetic-self-assignment-compound-slot.md`
  - `docs/adrs/0107-keep-self-referential-assignment-slot-optimization-slot-proven.md`
  - `docs/adrs/0108-keep-self-referential-with-assignment-dynamic.md`
- Historical Test262 gap inventory (not a current pass/fail baseline): `docs/remaining-test262-gaps.md`
- Active diagnostics/runtime implementation surfaces:
  - `src/Asynkron.JsEngine/Execution/StatementInstructionStorageDiagnostics.cs`
  - `src/Asynkron.JsEngine/Execution/StatementInstructionDiagnosticsCodec.cs`

## Baseline Notes
- Historical Test262 counts in `docs/remaining-test262-gaps.md` are directional context only and must not be treated as current regression results.
- This roadmap is a documentation slice and does not itself assert new runtime behavior.
