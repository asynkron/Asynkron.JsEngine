# Asynkron.JsEngine Roadmap

## Purpose
This roadmap aligns near-term implementation work with the long-term goal of building a strong Node.js competitor on .NET, while staying explicit about what is currently proven versus what is still directional.

## Current State (2026-05-28)

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
- Classdef simple-arrow execution now has explicit IR-activation evidence with lexical-dependency guardrails, keeping activation fast-path expansion proof-driven and semantics-safe (`docs/performance/classdef-arrow-simple-ir-activation.md`, `docs/adrs/0150-keep-simple-arrow-ir-activation-lexical-dependency-guarded.md`).
- Prototype constructor deduplication boundaries now keep `newTarget` callable extraction and return-value construction hook-split by constructor family, preserving observable semantics while reducing structural duplication (`docs/adrs/0149-keep-prototype-constructor-newtarget-hooks-split.md`).
- Destructuring now has explicit dense-array fast-path evidence with a JsValue-native ToObject coercion boundary; this reduces setup overhead on proven dense shapes while keeping coercion semantics and fallback ownership explicit (`docs/performance/destructuring-dense-array-fast-path.md`, `docs/adrs/0153-keep-destructuring-toobject-coercion-jsvalue-native.md`).
- `fib` now has explicit simple numeric self-recursion evidence with guarded call-shape and binding boundaries, adding a Node.js-competitive hotspot proof point without widening semantics-sensitive recursion paths (`docs/performance/fib-simple-numeric-self-recursion.md`, `docs/adrs/0152-keep-simple-numeric-self-recursion-fast-path-shape-and-binding-guarded.md`).
- No-argument literal-return call activation now has plan-proven direct-return evidence and guardrails, reducing sync activation overhead while preserving dynamic/observable fallback seams (`docs/performance/activation-noargs-literal-return-fast-path.md`, `docs/adrs/0159-keep-noargs-literal-return-fast-path-plan-proven.md`).
- JSON parse/stringify now has profile-owned default data-property and compact quote fast-path evidence with explicit semantics boundaries for `__proto__`, escaping, surrogates, reviver, and replacer behavior (`docs/performance/json-default-data-properties-and-quote-fast-path.md`, `docs/adrs/0158-keep-json-default-property-and-quote-fast-paths-profile-owned.md`).
- Activation call trampoline frame capacity now has focused activation-params evidence, with remaining activation-params-lite setup/trampoline overhead explicitly tracked as follow-up work rather than treated as solved (`docs/performance/activation-params-trampoline-frame-capacity.md`, [#2083](https://github.com/asynkron/Asynkron.JsEngine/issues/2083)).
- Sync IR trampoline frame capacity is now explicitly shallow-first and pool-owned, with future capacity growth constrained to new profile evidence (`docs/adrs/0167-keep-sync-ir-trampoline-frame-capacity-shallow-first.md`).
- `StringPrototype.Split`/join follow-through now has explicit consumer-materialization ownership evidence under ADR 0163, with remaining stringops materialization cost tracked as a narrow next-step issue (`docs/adrs/0163-keep-stringops-follow-up-consumer-materialization-owned.md`, `src/Asynkron.JsEngine/StdLib/String/StringPrototype.cs`, [#2084](https://github.com/asynkron/Asynkron.JsEngine/issues/2084)).
- Direct-eval program caching now removes repeated parse/plan rebuild on the `activation-evalscope-lite` profile while preserving eval observability boundaries; remaining evalscope-lite overhead is tracked as a bounded follow-up rather than treated as full parity (`docs/performance/activation-evalscope-eval-program-cache.md`, `src/Asynkron.JsEngine/EvalHostFunction.cs`, [#2149](https://github.com/asynkron/Asynkron.JsEngine/issues/2149)).
- ADR 0184 now fixes split/join consumer ownership boundaries with an explicit semantics-first join fallback for side-effectful coercion, while requiring comparable selected-profile rows for future reduction claims (`docs/adrs/0184-keep-stringops-split-join-consumers-guarded-and-observable.md`, `src/Asynkron.JsEngine/StdLib/String/StringPrototype.cs`, `src/Asynkron.JsEngine/StdLib/Array/ArrayPrototype.Transformations.cs`, [#2150](https://github.com/asynkron/Asynkron.JsEngine/issues/2150)).
- Empty-separator `split("")` character reuse is now explicitly consumer-owned and cache-bounded for ASCII-heavy workloads without widening observable split semantics (`docs/adrs/0172-keep-split-empty-character-cache-consumer-owned.md`, `src/Asynkron.JsEngine/StdLib/String/StringPrototype.cs`).
- Super-constructor private resolver routing is now explicitly JsValue-native and captured as a durable ownership boundary for future constructor-path optimization slices (`docs/adrs/0164-keep-super-constructor-resolver-jsvalue-native.md`, `src/Asynkron.JsEngine/Ast/JsEnvironmentExtensions.cs`).
- No-spread expression-program `new`/`super(...)` argument-carrier fast paths are now explicitly separated from spread/generic construction, while preserving spread-order and constructor-error semantics (`docs/adrs/0171-keep-no-spread-construct-argument-carriers-and-super-spread-order.md`).
- Observable arguments setup now has an explicit profile-owned and plan-proven boundary: descriptor/hoist improvements are accepted, while residual lexical-name setup remains an evidence-backed follow-up seam (`docs/adrs/0173-keep-observable-arguments-setup-profile-owned-and-plan-proven.md`, `docs/performance/activation-arguments-descriptor-and-hoist-fast-path.md`).
- Jint comparison policy now keeps the comparison version centralized and explicit, so benchmark deltas remain interpretable across optimization slices (`docs/adrs/0174-keep-jint-comparison-version-centralized.md`).
- Unified bytecode prototype boundaries are now explicitly IR-owned and all-or-nothing under ADR 0181, so production routing still requires separate parity/performance proof before any broader runtime claim (`docs/adrs/0181-keep-unified-bytecode-prototype-ir-owned-and-all-or-nothing.md`).
- Unified bytecode prototype control-flow now proves branch shapes plus one canonical condition-first loop back-edge IR shape with explicit unsupported-loop-control-flow declines, while still keeping production routing unchanged (`docs/adrs/0192-keep-unified-bytecode-acyclic-control-flow-compiler-owned.md`).
- Unified bytecode production routing boundary is now captured under ADR 0210 with explicit compiler-shape ownership, operator-owned Binary subset semantics, and no-mixed-execution constraints for accepted production programs (`docs/adrs/0210-keep-unified-bytecode-control-flow-production-routing-operator-and-shape-owned.md`, `src/Asynkron.JsEngine/Ast/TypedAstEvaluator.SyncFunctionInvoker.cs`, `src/Asynkron.JsEngine/Execution/UnifiedBytecode/UnifiedBytecodeProductionEligibility.cs`).
- Unified bytecode property access now executes through the current accepted production boundary: direct named/computed reads, exact two-hop named reads, direct named/computed writes, direct named/computed compound writes, and named/computed prefix/postfix updates. Accepted programs stay inside owned unified opcodes (`LoadSlot`, `LoadLiteral`, `StoreSlot`, `Binary`, `RequireObjectCoercible`, `ResolvePropertyKey`, `GetNamedProperty`, `GetComputedProperty`, `GetNamedPropertyForCompoundSet`, `GetComputedPropertyForCompoundSet`, `SetNamedProperty`, `SetComputedProperty`, `UpdateNamedProperty`, `UpdateComputedProperty`, `Jump`, `JumpIfFalse`, `Return`) with string-constant/operand-table ownership in `UnifiedBytecodeProgram`, and no bridge back to `ExpressionProgram`, `ExecutionPlanRunner`, or AST evaluation (`docs/adrs/0221-keep-unified-bytecode-property-reads-vm-owned-and-observable.md`, `docs/adrs/0222-keep-unified-bytecode-two-hop-named-property-read-boundary-owned.md`, `docs/adrs/0234-keep-unified-bytecode-property-writes-strict-and-directive-owned.md`, `docs/adrs/0238-keep-unified-bytecode-compound-property-writes-get-for-set-owned.md`, `src/Asynkron.JsEngine/Execution/UnifiedBytecode/UnifiedBytecodeCompiler.cs`, `src/Asynkron.JsEngine/Execution/UnifiedBytecode/UnifiedBytecodeVirtualMachine.cs`, `tests/Asynkron.JsEngine.Tests/UnifiedBytecodeProductionEligibilityTests.cs`, `tests/Asynkron.JsEngine.Tests/UnifiedBytecodeProductionInvocationTests.cs`).
- Unified bytecode completion-lane ownership now includes VM-executed `ReturnUndefined`, `Throw`, and accepted discarded-expression forms, while call-target preparation stays typed/non-executable and call invocation still declines before VM execution (`docs/adrs/0252-keep-unified-bytecode-completion-lane-vm-owned.md`, `docs/adrs/0250-keep-unified-bytecode-call-target-prep-boundary-non-executable.md`, `src/Asynkron.JsEngine/Execution/UnifiedBytecode/UnifiedBytecodeCompiler.cs`, `src/Asynkron.JsEngine/Execution/UnifiedBytecode/UnifiedBytecodeVirtualMachine.cs`, `src/Asynkron.JsEngine/Execution/UnifiedBytecode/UnifiedBytecodeProductionEligibility.cs`).
- Unified bytecode current-state documentation boundary is now explicitly synchronized across contract, roadmap, ADRs, and agent rules: supported opcode/decline inventories remain enum-backed, no-mixed-execution stays mandatory for accepted production programs, and next unsupported buckets remain executable call invocation, iterator/destructuring driver-state, label-dependent control flow, and dynamic lookup families (`docs/adrs/0256-keep-unified-bytecode-coverage-matrix-and-boundary-docs-synchronized.md`, `docs/unified-bytecode-expansion-contract.md`, `.claude/rules/unified-bytecode-prototypes.md`).
- Noncapturing `for`-`let` loop-scope elision now has a plan-proven/runtime-consumed boundary under ADR 0239, so activation follow-through can target the remaining measurable loop-scope seams without reopening already-proven noncapturing behavior (`docs/adrs/0239-keep-noncapturing-for-let-loop-scope-elision-plan-proven.md`, `docs/performance/failed-activation-arguments-loop-scope-template.md`).
- JsOps property lookup now has a durable receiver-preservation boundary under ADR 0240: private lookup helpers keep the original `JsValue` receiver instead of reconstructing it from object payloads, which narrows future cleanup slices to adjacent compatibility seams only (`docs/adrs/0240-keep-jsops-property-lookup-receivers-jsvalue-native.md`, `src/Asynkron.JsEngine/Runtime/JsOps.cs`).
- Recursion linear self-recursion follow-through is now explicitly evidence-backed and guardrailed under ADR 0241, extending strict simple numeric self-recursion coverage while preserving shape/binding safety constraints (`docs/performance/recursion-linear-self-recursion.md`, `docs/adrs/0241-keep-simple-numeric-self-recursion-fast-paths-shape-and-binding-guarded.md`).
- Class-method simple IR activation is now explicitly super-guarded so methods with `super` dependencies do not take the shortcut (`docs/adrs/0193-keep-class-method-simple-ir-activation-super-guarded.md`, `docs/performance/classdef-homeobject-simple-ir-activation.md`).
- Runner breakable-frame stack capacity is now explicitly profile-owned under ADR 0195, with evidence showing improved `activation-arguments-lite` startup cost while leaving lexical/environment setup as a bounded follow-up seam (`docs/adrs/0195-keep-runner-breakable-stack-capacity-profile-owned.md`, `docs/performance/activation-arguments-breakable-stack-presizing.md`, [#2201](https://github.com/asynkron/Asynkron.JsEngine/issues/2201)).
- Intl receiver brand validation is now explicitly JsValue-native under ADR 0196, with remaining private object-carrier helper cleanup constrained as a narrow standard-library follow-up (`docs/adrs/0196-keep-intl-receiver-brand-validation-jsvalue-native.md`, `src/Asynkron.JsEngine/StdLib/Intl/IntlBrandExtensions.cs`, [#2202](https://github.com/asynkron/Asynkron.JsEngine/issues/2202)).
- Runner symbol stores are now explicitly JsValue-native under ADR 0203, removing the private `object?` compatibility seam and keeping intentional state-object wrapping local at callsites (`docs/adrs/0203-keep-runner-symbol-stores-jsvalue-native.md`, `src/Asynkron.JsEngine/Ast/TypedAstEvaluator.ExecutionPlanRunner.Helpers.cs`).
- Direct-eval program-cache follow-through now includes a lock-free last-entry cache in front of the bounded LRU; selected `activation-evalscope-lite` evidence shows a large median reduction while remaining activation/environment overhead is still tracked as follow-up (`docs/performance/activation-evalscope-eval-program-last-entry-cache.md`, `src/Asynkron.JsEngine/EvalHostFunction.cs`).
- Async generators still carry a known weaker seam: invoker wiring currently runs through the shared `ExecutionPlanRunner.ExecuteAsyncStep` bridge rather than a dedicated async-generator IR executor; this remains a bounded follow-up surface rather than a settled runtime contract (`src/Asynkron.JsEngine/Ast/TypedAstEvaluator.AsyncGeneratorInvoker.cs`).
- Recent failed-trial documentation now explicitly captures two open optimization seams so recurring passes can stay evidence-first: `simplearithmetic` ProfileRunner/benchmark-shape parity and `classdef` constructor/`super(...)` dispatch follow-through (`docs/performance/failed-simplearithmetic-profiler-sync-evaluate-trials.md`, `docs/performance/failed-classdef-homeobject-and-construct-trials.md`, [#2281](https://github.com/asynkron/Asynkron.JsEngine/issues/2281), [#2282](https://github.com/asynkron/Asynkron.JsEngine/issues/2282)).
- Late 2026-05-27 ADR/current-state slices now keep three seams explicitly bounded for recurring follow-through: typed module execution helper ownership (`docs/adrs/0212-keep-typed-module-execution-helper-jsvalue-native.md` with current owner surface in `src/Asynkron.JsEngine/JsEngine.cs`), Math host fast dispatch ownership (`docs/adrs/0227-keep-math-host-function-fast-dispatch-marked-and-arity-specific.md` with owner surface in `src/Asynkron.JsEngine/StdLib/Math/MathPrototype.cs`), and splice delete-count argument-presence normalization ownership (`docs/adrs/0228-keep-splice-delete-count-argument-presence-shared.md` with owner surface in `src/Asynkron.JsEngine/StdLib/Array/StandardLibrary.Array.Helpers.cs`). Current production evidence for this boundary set remains `docs/performance/propertyaccess-unified-bytecode-production-profile-evidence.md`, and the next bounded follow-up tasks are tracked in [#2374](https://github.com/asynkron/Asynkron.JsEngine/issues/2374) and [#2375](https://github.com/asynkron/Asynkron.JsEngine/issues/2375).
- Sync function `this` binding in the private activation slow path is now explicitly JsValue-native and accepted under ADR 0232 (`docs/adrs/0232-keep-sync-function-this-binding-jsvalue-native.md`, owner surface `src/Asynkron.JsEngine/Ast/TypedAstEvaluator.SyncFunctionInvoker.cs`), with strict/sloppy and derived-constructor semantics proof held as the follow-through gate.
- Activation loop-scope template optimization remains directional, not accepted runtime behavior: the 2026-05-27 trial was reverted after a 6.1% `activation-arguments-lite` improvement that missed the 10% acceptance threshold (`docs/performance/failed-activation-arguments-loop-scope-template.md`), and follow-up is tracked in [#2402](https://github.com/asynkron/Asynkron.JsEngine/issues/2402).
- Module dependency evaluation task/fault propagation is now hardened under ADR 0212: PR #2406 keeps stored `ModuleEntry.EvaluationTask` as the normal async dependency drain owner while awaiting the returned `EnsureModuleEvaluatedAsync(...)` task as a fallback when no stored task exists, with focused sync and async dependency fault tests in `ModuleTests`.

### What Works Worse
- Statement execution still relies on record-backed `ExecutionPlan.Instructions`; compact statement storage is diagnostics-oriented rather than runtime-active today.
- Unsupported statement-family buckets remain in migration diagnostics, so compact runtime-routing and record-backed removal are not yet low-risk.
- Dynamic/eval seams and activation-sensitive behavior still require careful handling to avoid correctness regressions while optimizing hot paths.
- Async-generator resume flow still depends on the shared async-step bridge seam, so full async-generator IR support is not complete yet.
- ProfileRunner `simplearithmetic` still needs explicit workload-shape parity against `benchmark.sh` before reduction claims are treated as durable.
- Property-access production coverage remains intentionally narrow beyond the accepted direct boundary: logical assignments, optional chaining, richer computed-key expressions, nested/complex member chains, `super`, private fields, `delete`, call/construct families, `this`-bound dynamic member access, captured activation shapes, and out-of-boundary computed/member forms still decline before unified VM execution under the current selector boundary (`src/Asynkron.JsEngine/Execution/UnifiedBytecode/UnifiedBytecodeProductionEligibility.cs`, `docs/adrs/0218-keep-unified-bytecode-property-read-production-boundary-selector-owned.md`, `docs/adrs/0234-keep-unified-bytecode-property-writes-strict-and-directive-owned.md`, `docs/adrs/0238-keep-unified-bytecode-compound-property-writes-get-for-set-owned.md`).
- Activation lexical/environment setup is still a measurable seam on `activation-arguments-lite`; the latest slot-template loop-scope attempt is documented as a failed/reverted trial and should not be treated as accepted optimization until a larger semantic-safe reduction lands (`docs/performance/failed-activation-arguments-loop-scope-template.md`, follow-up [#2402](https://github.com/asynkron/Asynkron.JsEngine/issues/2402)).

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
22. Continue simple-arrow IR activation follow-through only behind proven lexical-dependency guards, and require focused profile plus semantics proof before widening to broader arrow/function shapes under ADR 0150.
23. Preserve constructor-family `newTarget` and return-value hook boundaries while pursuing prototype-constructor deduplication so observable constructor semantics remain explicit under ADR 0149.
24. Continue destructuring and recursive hot-path follow-through only where dense-array shape and simple numeric self-recursion guards are proven; keep any future Promise constructor/combinator spec-order slices evidence-first and cite dedicated Promise evidence surfaces before adding roadmap claims.
25. Continue activation no-args fast-path follow-through only through plan-owned eligibility widening and focused activation proof-pack evidence under ADR 0159 constraints (tracked in [#2040](https://github.com/asynkron/Asynkron.JsEngine/issues/2040)).
26. Continue JSON parse/stringify optimization only through profile-owned `JsonHelper` owner surfaces with focused JSON tests and repeated selected-profile timing under ADR 0158 constraints (tracked in [#2041](https://github.com/asynkron/Asynkron.JsEngine/issues/2041)).
27. Continue activation follow-through by reducing remaining activation-params-lite call setup/trampoline overhead only through focused profile-backed slices that preserve current eligibility/semantics boundaries (tracked in [#2083](https://github.com/asynkron/Asynkron.JsEngine/issues/2083)).
28. Continue stringops split/join follow-through under ADR 0163 by reducing remaining consumer materialization overhead with focused profile plus regression proof before any broader widening (tracked in [#2150](https://github.com/asynkron/Asynkron.JsEngine/issues/2150); comparable row evidence in `docs/performance/stringops-split-join-consumer-follow-through.md`).
29. Continue activation-arguments follow-through after ADR 0195 by reducing remaining lexical/environment setup overhead on `activation-arguments-lite` with selected-profile proof while preserving observable semantics boundaries (tracked in [#2123](https://github.com/asynkron/Asynkron.JsEngine/issues/2123), with latest lexical/environment setup slice evidence in [#2201](https://github.com/asynkron/Asynkron.JsEngine/issues/2201) and `docs/performance/activation-arguments-breakable-stack-presizing.md`).
30. Continue async-generator follow-through by removing sync-IR resume shim coupling and tightening callback ownership toward full async-generator IR support with focused semantics/profile proof (tracked in [#2124](https://github.com/asynkron/Asynkron.JsEngine/issues/2124)).
31. Continue direct-eval follow-through after the eval program-cache and last-entry-cache slices by reducing remaining `activation-evalscope-lite` activation/environment overhead with focused profile plus activation/eval proof-pack evidence while preserving eval observability boundaries (tracked in [#2257](https://github.com/asynkron/Asynkron.JsEngine/issues/2257); prior related slices in [#2228](https://github.com/asynkron/Asynkron.JsEngine/issues/2228) and baseline context in [#2149](https://github.com/asynkron/Asynkron.JsEngine/issues/2149)).
32. Continue ADR 0184 stringops split/join consumer follow-through by reducing remaining consumer materialization overhead with comparable selected-profile before/after rows and semantics-first fallback proof (tracked in [#2150](https://github.com/asynkron/Asynkron.JsEngine/issues/2150)).
33. Preserve the unified bytecode production-routing boundary from ADR 0210 while widening sync-production eligibility only through narrow parity/profile-proven selector expansions beyond the current accepted opcode/control-flow subset. Current proven production coverage includes direct named/computed reads, exact two-hop named reads, direct named/computed writes, direct named/computed compound writes, direct named/computed updates, completion-lane `ReturnUndefined`/`Throw`/accepted discarded-expression VM execution, and integrated completed-lane composition under ADR 0218/0221/0222/0234/0238/0250/0252/0253/0255/0258 ownership; call-target preparation may compile as typed/non-executable metadata under ADR 0250, but call invocation remains a decline boundary until a dedicated proof slice lands. Keep next unsupported buckets explicit in every widening slice: executable call invocation, iterator/destructuring driver-state, label-dependent control flow, and dynamic lookup families. Follow-up remains split between refreshed evidence in [#2340](https://github.com/asynkron/Asynkron.JsEngine/issues/2340), first executable no-spread call-invocation proof in [#2495](https://github.com/asynkron/Asynkron.JsEngine/issues/2495), and model-first for-in/destructuring driver-state planning in [#2496](https://github.com/asynkron/Asynkron.JsEngine/issues/2496). Long-term Node.js-competitor alignment for module/runtime/host seams is tracked in [#2342](https://github.com/asynkron/Asynkron.JsEngine/issues/2342) (also tracked in [#2256](https://github.com/asynkron/Asynkron.JsEngine/issues/2256); prior related slices in [#2227](https://github.com/asynkron/Asynkron.JsEngine/issues/2227), with control-flow prototype boundary context in [#2182](https://github.com/asynkron/Asynkron.JsEngine/issues/2182), current production evidence in `docs/performance/unified-bytecode-branch-production-routing.md`, `docs/performance/propertyaccess-unified-bytecode-production-profile-evidence.md`, and ADR 0210/0218/0234/0238/0250/0252/0258). Keep this widening effort aligned with the #2342 architecture-alignment milestones below.
34. Reduce remaining `classdef` constructor and `super(...)` dispatch cost in one narrow evidence-backed slice while preserving ADR 0193 super-guarded activation boundaries (tracked in [#2183](https://github.com/asynkron/Asynkron.JsEngine/issues/2183); failed-trial follow-through task in [#2282](https://github.com/asynkron/Asynkron.JsEngine/issues/2282)).
35. Continue Intl/stdlib brand-helper cleanup after ADR 0196 by removing remaining private object-carrier overload use in one bounded helper cluster with focused Intl regression proof (tracked in [#2202](https://github.com/asynkron/Asynkron.JsEngine/issues/2202)).
36. Align ProfileRunner `simplearithmetic` workload shape with `benchmark.sh` and isolate remaining IIFE overhead before claiming further simplearithmetic reductions (tracked in [#2281](https://github.com/asynkron/Asynkron.JsEngine/issues/2281)).
37. Continue typed module evaluation follow-through by shrinking `ModuleEntry.LastValue` object-shaped compatibility seams and keeping module execution helpers JsValue-native under ADR 0212 boundaries (tracked in [#2374](https://github.com/asynkron/Asynkron.JsEngine/issues/2374)).
38. Continue host/stdlib follow-through by reducing nearby overhead around Math host fast dispatch and splice-family delete-count ownership without weakening ADR 0227/0228 semantics constraints (tracked in [#2375](https://github.com/asynkron/Asynkron.JsEngine/issues/2375)).
39. Continue activation-arguments follow-through with one narrow semantic-safe slice that targets non-capturing loop-scope environment overhead on `activation-arguments-lite`, using selected-profile evidence and focused activation proofs before any acceptance claim (tracked in [#2402](https://github.com/asynkron/Asynkron.JsEngine/issues/2402)).
40. Continue recursion follow-through after ADR 0241 with one narrow profile-backed slice that widens strict simple numeric self-recursion only where shape/binding guards remain compiler/runtime-proven (tracked in [#2450](https://github.com/asynkron/Asynkron.JsEngine/issues/2450)).
41. Continue JsOps/property cleanup after ADR 0240 by removing one bounded adjacent object-carrier compatibility seam while preserving receiver identity and accessor/prototype observability (tracked in [#2451](https://github.com/asynkron/Asynkron.JsEngine/issues/2451)).

## Node.js-Competitor Architecture Alignment Milestones (#2342)

### Milestone A: Module/runtime compatibility boundary hardening
- Owner surfaces:
  - `src/Asynkron.JsEngine/JsEngine.cs` (`EvaluateSync`, `EvaluateModule`, module registry, top-level await wiring, host scheduler hooks).
  - `tests/Asynkron.JsEngine.Tests/ModuleTests.cs`.
  - `tests/Asynkron.JsEngine.Tests/AsyncModuleTryAwaitTests.cs`.
- Currently proven:
  - ESM load/evaluate and async module behavior are available with explicit runtime entry points and focused tests.
  - Dynamic import and module orchestration exist as runtime-owned seams, not host-demo-only behavior.
- Aspirational (not yet parity claim):
  - Broader Node.js-competitor module ergonomics and compatibility should be widened only through subsystem-scoped proof slices.
  - No claim of full Node module parity or broad CommonJS/interop equivalence yet.
- Evidence gates:
  - Focused module test packs (`ModuleTests`, `AsyncModuleTryAwaitTests`) stay green for each slice.
  - Roadmap/ADR updates must preserve explicit module boundary wording in `docs/dreaming.md` and architecture docs.

### Milestone B: Host interop boundary clarity (engine vs host layer)
- Owner surfaces:
  - `src/Asynkron.JsEngine/JsTypes/HostFunction.cs` (host callable bridge contract).
  - `src/Asynkron.JsEngine/JsEngine.cs` (host global and callback integration seams).
  - `tests/Asynkron.JsEngine.Tests/CommonjsModuleTests.cs` (host/CommonJS compatibility behavior checks).
- Currently proven:
  - Host callables and globals flow through explicit bridge/runtime seams with test-owned behavior.
  - Node-style host execution remains an integration layer, not evidence of core evaluator parity.
- Aspirational (not yet parity claim):
  - Improve Node.js-competitive host ergonomics while preserving explicit compatibility boundaries.
  - Keep CommonJS interoperability progression evidence-first and host-layer scoped.
- Evidence gates:
  - Focused host/CommonJS test surfaces remain green (`CommonjsModuleTests` and adjacent host bridge checks).
  - Any Node-style capability expansion must document engine-vs-host ownership in roadmap/architecture notes before parity claims.

### Milestone C: Async runtime seam closure for predictable delivery
- Owner surfaces:
  - `src/Asynkron.JsEngine/Ast/TypedAstEvaluator.AsyncGeneratorInvoker.cs` (known async-generator seam).
  - `src/Asynkron.JsEngine/JsEngine.cs` async scheduling/event-loop integration points.
  - `tests/Asynkron.JsEngine.Tests/PendingAsyncWorkTrackingTests.cs`.
- Currently proven:
  - Pending async-work tracking and async scheduling surfaces are test-owned and exposed in runtime APIs.
  - Async generators still include a bounded sync-wrapper/callback seam that is explicitly tracked as incomplete.
- Aspirational (not yet parity claim):
  - Remove the sync-generator shim coupling and tighten async-generator runtime ownership without changing JS semantics.
  - Reach more predictable Node.js-competitor async behavior only through narrow, proof-backed seam removal slices.
- Evidence gates:
  - Focused async runtime tests stay green (`PendingAsyncWorkTrackingTests` plus owning async-generator packs).
  - Each seam-change slice must include profile evidence (`rtk ./tools/profile forloop --memory` where relevant) and explicit seam-scan updates before widening claims.

## Long-Term Goals
1. Raise and sustain Test262 compatibility toward full compliance through spec-owned, subsystem-driven slices.
2. Reach Node.js-competitive runtime behavior and developer ergonomics on .NET, including module/runtime compatibility and predictable host integration seams, with near-term delivery anchored by the #2342 architecture-alignment milestones in this roadmap.
3. Keep benchmark breadth and profiling discipline (CPU/allocation trends across representative scenarios) so speed/memory work remains evidence-driven over time.
4. Land compact statement-bytecode execution paths only when unsupported-family and parity evidence show safe readiness.

## Evidence Surfaces
- Current migration direction and runtime/storage boundary: `docs/expression-bytecode-migration-report-2026-05-23.md`
- Expression bytecode capability and failure taxonomy: `docs/expression-bytecode-coverage.md`
- Activation loop-scope failed-trial evidence and current directional seam: `docs/performance/failed-activation-arguments-loop-scope-template.md`
- Recent loop-scope / receiver / recursion guardrail ADRs:
  - `docs/adrs/0239-keep-noncapturing-for-let-loop-scope-elision-plan-proven.md`
  - `docs/adrs/0240-keep-jsops-property-lookup-receivers-jsvalue-native.md`
  - `docs/adrs/0241-keep-simple-numeric-self-recursion-fast-paths-shape-and-binding-guarded.md`
- Accepted sync function `this`-binding boundary and module-task/value boundary context: `docs/adrs/0232-keep-sync-function-this-binding-jsvalue-native.md`, `docs/adrs/0212-keep-typed-module-execution-helper-jsvalue-native.md`
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
  - `docs/performance/classdef-arrow-simple-ir-activation.md`
  - `docs/performance/destructuring-dense-array-fast-path.md`
  - `docs/performance/fib-simple-numeric-self-recursion.md`
  - `docs/performance/recursion-linear-self-recursion.md`
  - `docs/performance/activation-noargs-literal-return-fast-path.md`
  - `docs/performance/json-default-data-properties-and-quote-fast-path.md`
  - `docs/performance/activation-params-trampoline-frame-capacity.md`
  - `docs/performance/activation-arguments-descriptor-and-hoist-fast-path.md`
  - `docs/performance/activation-arguments-breakable-stack-presizing.md`
  - `docs/performance/activation-evalscope-eval-program-cache.md`
  - `docs/performance/activation-evalscope-eval-program-last-entry-cache.md`
  - `docs/performance/failed-simplearithmetic-profiler-sync-evaluate-trials.md`
  - `docs/performance/failed-classdef-homeobject-and-construct-trials.md`
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
  - `docs/adrs/0149-keep-prototype-constructor-newtarget-hooks-split.md`
  - `docs/adrs/0150-keep-simple-arrow-ir-activation-lexical-dependency-guarded.md`
  - `docs/adrs/0151-keep-error-constructor-shared-initialization-argument-mapped.md`
  - `docs/adrs/0152-keep-simple-numeric-self-recursion-fast-path-shape-and-binding-guarded.md`
  - `docs/adrs/0153-keep-destructuring-toobject-coercion-jsvalue-native.md`
  - `docs/adrs/0158-keep-json-default-property-and-quote-fast-paths-profile-owned.md`
  - `docs/adrs/0159-keep-noargs-literal-return-fast-path-plan-proven.md`
  - `docs/adrs/0163-keep-stringops-follow-up-consumer-materialization-owned.md`
  - `docs/adrs/0164-keep-super-constructor-resolver-jsvalue-native.md`
  - `docs/adrs/0167-keep-sync-ir-trampoline-frame-capacity-shallow-first.md`
  - `docs/adrs/0171-keep-no-spread-construct-argument-carriers-and-super-spread-order.md`
  - `docs/adrs/0172-keep-split-empty-character-cache-consumer-owned.md`
  - `docs/performance/stringops-split-join-consumer-follow-through.md`
  - `docs/adrs/0173-keep-observable-arguments-setup-profile-owned-and-plan-proven.md`
  - `docs/adrs/0174-keep-jint-comparison-version-centralized.md`
  - `docs/adrs/0181-keep-unified-bytecode-prototype-ir-owned-and-all-or-nothing.md`
  - `docs/adrs/0184-keep-stringops-split-join-consumers-guarded-and-observable.md`
  - `docs/adrs/0192-keep-unified-bytecode-acyclic-control-flow-compiler-owned.md`
  - `docs/adrs/0193-keep-class-method-simple-ir-activation-super-guarded.md`
  - `docs/adrs/0195-keep-runner-breakable-stack-capacity-profile-owned.md`
  - `docs/adrs/0196-keep-intl-receiver-brand-validation-jsvalue-native.md`
  - `docs/adrs/0203-keep-runner-symbol-stores-jsvalue-native.md`
  - `docs/adrs/0204-keep-unified-bytecode-sync-production-routing-slot-bridged.md`
  - `docs/adrs/0210-keep-unified-bytecode-control-flow-production-routing-operator-and-shape-owned.md`
  - `docs/performance/unified-bytecode-branch-production-routing.md`
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
  - `src/Asynkron.JsEngine/Ast/TypedAstEvaluator.AsyncGeneratorInvoker.cs`
  - `src/Asynkron.JsEngine/Execution/StatementInstructionStorageDiagnostics.cs`
  - `src/Asynkron.JsEngine/Execution/StatementInstructionDiagnosticsCodec.cs`
  - `src/Asynkron.JsEngine/Ast/TypedAstEvaluator.ExecutionPlanRunner.Completion.cs`
  - `src/Asynkron.JsEngine/Ast/ControlFlowSyntaxValidator.cs`

## Baseline Notes
- Historical Test262 counts in `docs/remaining-test262-gaps.md` are directional context only and must not be treated as current regression results.
- This roadmap is a documentation slice and does not itself assert new runtime behavior.
