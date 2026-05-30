# Asynkron.JsEngine Roadmap

_Last updated: 2026-05-30_

## Current State

Asynkron.JsEngine is a JavaScript engine for .NET with broad ECMAScript coverage. Execution runs through a multi-tier typed-AST evaluator rather than a single tree-walker: a `UnifiedBytecodeVM` as the target hot route, an `ExpressionProgram` expression-bytecode VM, a lowered statement-IR runner (`ExplicitExecutionPlan`), and an AST tree-walking bridge retained only as a correctness fallback. Recent work has concentrated on the typed-AST evaluator and allocation reduction in hot paths:

- Per-`FunctionExpression` caching of `SyncFunctionInvoker` static analysis (PR #2661, ~15% faster `forofiteration`).
- Skipping the redundant arguments guard in the simple-IR closure-activation fast path (PR #2646).
- `JsValue` struct adoption across numeric/string coercion paths — `ToNumericCore` and `ToJsStringFromObjectValue` (commit bd70ca63); the evaluation-pipeline `JsValue` migration boundary is recorded in ADR 0281 / rule 20 (PR #2651).
- Unified-bytecode production routing widened to label-dependent control flow (gh2679, ADR 0285): labeled statements, labeled loops, labeled block `break`, and labeled `break`/`continue` now route through the compiler-owned resolved-target path; only a labeled abrupt that crosses an enclosing iterator/for-in driver loop still declines (`LabelControlFlow`). This clears "Ranked Next Unsupported Buckets" #5 in `docs/unified-bytecode-expansion-contract.md`.
- Unified-bytecode production routing widened to admit simple **object destructuring** driver shapes (`const { a, b } = obj`, optional identifier rest) via a VM-owned `ObjectDestructuring*` opcode family mirroring the array precedent; computed keys, defaults, and nested patterns still decline model-first. Decision recorded in ADR 0284 (issue gh2677).
- Unified-bytecode production routing widened to admit **synchronous spread calls** (`f(...args)`, `obj.method(...args)`, multi-spread, mixed positional+spread). Spread mask packed into existing `CallInvocationBoundary` operand; VM flattens spreads via `EnumerateSpread` helper preserving iteration order and side-effects. Optional calls, construct/super, and direct eval still decline. Decision recorded in ADR 0287 (issue gh2676).
- Unified-bytecode production routing widened to admit **optional member calls** (`box?.read(args)`, `box.read?.(args)`, `box[key]?.(args)`). Two new opcodes (`PrepareNamedOptionalCallTarget`, `PrepareComputedOptionalCallTarget`) encode the nullish short-circuit target in the high 16 bits of the operand; the VM checks nullish before proceeding and yields `undefined` on short-circuit. Receiver-as-`this` contract preserved identically to non-optional member calls. Decision recorded in ADR 0289 (issue gh2689).

- Unified-bytecode production routing widened to admit **synchronous non-spread construct calls** (`new F(...)`). New `ConstructInvocationBoundary` opcode invokes `[[Construct]]` with the constructor as `new.target`, mirroring the spec-conformant construct reference helper (`new.target` propagation, not-a-constructor `TypeError`, left-to-right argument order). Spread-onto-construct, member-target/non-simple constructs, and the **super call family** (`super(...)`, super-member call targets) still decline — super is activation-gated by the derived-constructor decline in `SyncFunctionInvoker.CanUseProductionUnifiedBytecode` and so is unreachable. Decision recorded in ADR 0286 (issue gh2690).

- Unified-bytecode production routing widened to admit **`this`-based named-receiver chains and binary expressions**. `LoadThis`/`LoadNewTarget` base nodes are now accepted by the boundary-property-read classifier (`TryIsFirstBoundaryPropertyReadBinaryExpressionCandidate`), allowing call sites like `this.method(this)` and guard expressions like `this.prop === value` to compile through the unified route (commit `64a914f6`).

- Unified-bytecode resumable generator route **corrected for spec-conformant generator instances and iterator results**. Two correctness fixes: (1) `TryCreateUnifiedBytecodeGenerator` now resolves the generator function's own `.prototype` property via `ResolveInstanceGeneratorPrototype` so `g() instanceof g` holds; (2) `CreateIteratorResult` threads the `EvaluationContext` and sets `%Object.prototype%` on each `{value, done}` result, per `CreateIterResultObject`. Fixes Test262 generators `has-instance`, `prototype-value`, `Symbol.toStringTag`, and `GeneratorPrototype/next/result-prototype` (commit `69707f8d`).

- Unified-bytecode resumable generator route **gated on simple-identifier parameters**. Generator functions with destructuring, default, or rest parameters were silently skipping `FunctionDeclarationInstantiation` — parameter errors (e.g. `TypeError` for `*m([[x]]){}` called with `[null]`) were never thrown. `TryCreateUnifiedBytecodeGenerator` now gates on `HasOnlySimpleIdentifierParameters`, falling back to the runner path which evaluates `FunctionDeclarationInstantiation` eagerly. Fixes ~520 Test262 `gen-meth` destructuring regressions (commit `330c1eb0`).

- **AnnexB blocked-names HashSet skipped when body has no function declarations** (commit `8be3852f`, PR #2702). `CreateExecutionEnvironment` formerly allocated a `HashSet<Symbol>` on every non-strict function call to track Annex B B.3.3 var-scope hoisting. That set is only used by `HandleFunctionDeclaration`, which never fires when no block-level function declarations exist. Guarding on `hoistPlan.HasFunctionDeclarations` eliminates the allocation in the common case: `simplearithmetic` −37% (447→281 ms), `classdef` −43%.

- **`StringifyValue(JsValue)` overload added to `JsonHelper`; legacy `object?` path marked `[Obsolete]`** (PR #2704). `ConsolePrototype` call sites updated to use the `JsValue`-native overload directly. `TryGetObject(object?, ...)` in `StandardLibrary.Helpers` also marked `[Obsolete]`, advancing the JsValue migration boundary described in ADR 0281 / rule 20.

- **`docs/dreaming.md` expanded with shape/IC system, event loop lifecycle, performance SLOs, and active GC allocation budget** (commit `80514626`, PR #2698). Added: component 10 (Shape/IC — hidden-class layout, transition table, mono/poly/mega IC, IC invalidation); full event-loop lifecycle diagram distinguishing microtask drain from host macrotask scheduling; Performance SLOs section with measurable targets (startup latency, allocation per hot loop, microtask drain, Test262 failures, Tier 0 coverage); active GC allocation budget table with per-site Gen 0/1/2 targets and boxing-avoidance rules. Component numbers renumbered (DevTools → 12, Security → 13).

- **Array- and object-literal argument widening via span-based operand recognition** (commit `cf7d4924`, PR #2719). The compiler's span-walk now admits array-literal and object-literal expression arguments at call boundaries, closing the gap tracked in gh2705. ADR 0290 records the design; holes, spread, computed keys, and name-inference forms still decline model-first.

- **Object literal property shorthand in unified bytecode production** (PR #2738). Shorthand properties (`{ x }` equivalent to `{ x: x }`) are now compiled through the unified route. Rule 6 captured in PR #2739 establishes that `DefineObjectProperty` calls with `AllowNameInference` must be wrapped with `EnsureHasName` to preserve function-name signals for debuggers and `toString`.

- **10 missing binary operator arms added to `UnifiedBytecodeVirtualMachine`** (commit `f4ed0c9a`, PR #2730). Arithmetic, bitwise, comparison, and shift operators that previously fell through the VM dispatch table now have explicit fast-path arms. Rule 39 (binary-operator widening checklist, PR #2735/#2736) mandates that the compiler gate and VM eligibility gate stay in sync across all four affected surfaces.

- **Spread bulk-copy optimization attempt documented** (commit `af145ffe`, PR #2727). A dense-array spread pre-size and bulk-copy path was prototyped and A/B measured; the approach was found to be below threshold, so the decision and measurements are recorded rather than left as a recurring optimization candidate (rule 9b).

- **`docs/dreaming.md` rev 3 expands architecture direction** (commit `9308b48f`, PR #2725). Additions: `.NET Platform Advantage` section (JsValue struct, Span<JsValue>, NativeAOT, ValueTask zero-alloc async, meta-JIT, SIMD intrinsics); `Component 11 — Embedding / Host API` (CreateRealm, HostFunction bridge, first-class embedding contract); register-based VM directional note; positive completion conditions for each component.

Architecture direction is tracked in docs rather than as runtime parity claims: unified-bytecode primary sync-route coverage is recorded (PR #2644), the ordinary sync `this`-binding route is recorded in ADR 0279 (issue #2633), its resumable async/generator counterpart accepting `this`-dependent suspendable functions is recorded in ADR 0283 (issue #2675), and the typed-AST/"dreaming" target is refined in `docs/dreaming.md` — a 4-tier execution model (PR #2647) followed by a self-critique revision (PR #2663), with tier-numbering disambiguation captured as rule 14 (PR #2650). These describe a staged migration with allocation budgets and escape hatches, not current parity.

Conformance against Test262 is tracked via a custom testrunner with baselines in `.testrunner/`. Most reported failures are crash collateral rather than true correctness gaps; real correctness failures are typically under 20.

## Short-Term Goals

- [x] Skip redundant arguments guard in simple-IR closure-activation fast path (PR #2646).
- [x] Add allocation-regression benchmark gate to CI to guard evaluator hot-path gains (gh2668).
- [x] Fix generator instance prototype and iterator-result prototype in the resumable unified-bytecode route (commit `69707f8d`).
- [x] Gate resumable generator route on `HasOnlySimpleIdentifierParameters` to restore spec-conformant `FunctionDeclarationInstantiation` for destructuring/default/rest params (commit `330c1eb0`).
- [x] Skip AnnexB blocked-names `HashSet` allocation when body has no function declarations; 37% `simplearithmetic` improvement (PR #2702).
- [x] Widen unified bytecode to cover array/object literal argument and property-shorthand shapes (PR #2719, #2738; issue gh2705 landed); simple computed-key object literal properties admitted as call args and binary-expression RHS (gh2742).
- [ ] Continue widening unified bytecode to cover template literal expression shapes.
- [ ] Reduce allocations in async/generator resumption paths (active GC budget target: Gen 0 only per resumption cycle — see `docs/dreaming.md` allocation budget table).
- [ ] Continue reducing allocations in the evaluator hot paths (call frames, argument arrays).
- [ ] Expand `JsValue` struct adoption to remaining boxed numeric/string paths (follow `[Obsolete]` markers added in PR #2704).
- [ ] Improve Test262 pass rate by triaging real correctness failures (target: <10 true failures — SLO defined in `docs/dreaming.md`).
- [ ] Establish performance SLO baseline measurements in the ProfileRunner matrix (startup latency, allocation per hot loop, microtask drain — targets defined in `docs/dreaming.md`).
- [ ] Land the typed-AST evaluator migration described in the dreaming docs, gated by allocation budgets (2-stratum target: delete Tier 1 + Tier 2).

## Long-Term Goals

- [ ] Position Asynkron.JsEngine as a credible Node.js-competitor runtime for .NET hosts: achieve Node.js-comparable throughput on `fibonacci`, `forofiteration`, and `objectcreation` workloads (embeddable, fast startup, low allocation).
- [ ] Provide a stable embedding API with host interop: deliver full ECMAScript module graph with import/export host API, async, and timers.
- [ ] Achieve startup-time/compile-time parity with comparable .NET-hosted runtimes on representative workloads.
- [ ] Achieve high Test262 conformance (>95% on Language + BuiltIns suites).
- [ ] Offer ahead-of-time / cached compilation of hot scripts for faster warm starts.
- [ ] Reach the 2-stratum greenfield target (delete Tier 1 ExpressionProgram VM + Tier 2 Statement IR Runner; retain only Stratum 0 Compiled VM and Stratum F correctness fallback — see `docs/dreaming.md`).
- [ ] Implement Shape/IC system (directional): hidden-class property layout, shape transition table, and mono/poly/megamorphic inline-cache dispatch at the compiled VM layer, once Stratum 0 coverage is proven — see component 10 in `docs/dreaming.md`.

## Next Steps

- [ ] (open, gh2665) Triage and fix the top real Test262 correctness failures in the Language suite (target: <10 true failures).
- [x] (landed, gh2668) Add an allocation-regression benchmark gate to CI to guard the evaluator hot-path gains above.
- [ ] (open, gh2678) Continue the unified-bytecode driver-state widening: Slice A (TDZ head environments for sync iterator/for-in drivers) landed via `TdzHeadInit` and ADR 0288; Slice B (awaited iterator/for-in sources) and Slice C (async iterator driver kind) remain declined.
- [x] (landed, gh2705) Expanded unified bytecode to cover array-literal and object-literal argument shapes via span-based operand recognition (commit `cf7d4924`, PR #2719); property shorthand (PR #2738) and `EnsureHasName` rule 6 (PR #2739) added as follow-on.
- [ ] (open, gh2706) Add Node.js comparison benchmark suite (`fibonacci`, `object-creation`, `string-ops`) to CI — aligned with long-term Node.js-competitor goal.
- [ ] (open, gh2711) Establish performance SLO baseline measurements in ProfileRunner/CI — instrument the SLO targets defined in `docs/dreaming.md` (startup latency, allocation per hot loop, microtask drain, Test262 failures, Tier 0 coverage) and wire into the CI gate.
- [ ] (open, gh2712) Reduce allocations in the unified-bytecode resumable generator resumption path — profile and reduce per-cycle Gen 1/2 escapes under `forofiteration`; target Gen 0 only per resume cycle per `docs/dreaming.md` allocation budget table.
- [ ] (open, gh2741) Widen unified bytecode to cover template literal expression shapes — natural follow-on to array/object literal widening (gh2705).
- [x] (landed, gh2742) Widened unified bytecode span scanner to admit simple-computed-key object literal properties (`{ [k]: v }`, `{ ["name"]: v }`, `{ [0]: v }`) in call-argument and binary-expression-RHS positions; complex key expressions remain declined (ADR 0291).

_Generated and maintained by the recurring Roadmapper run._
