# Asynkron.JsEngine Roadmap

_Last updated: 2026-05-30_

## Current State

Asynkron.JsEngine is a JavaScript engine for .NET with broad ECMAScript coverage. Execution runs through a multi-tier typed-AST evaluator rather than a single tree-walker: a `UnifiedBytecodeVM` as the target hot route, an `ExpressionProgram` expression-bytecode VM, a lowered statement-IR runner (`ExplicitExecutionPlan`), and an AST tree-walking bridge retained only as a correctness fallback. Recent work has concentrated on the typed-AST evaluator and allocation reduction in hot paths:

- Per-`FunctionExpression` caching of `SyncFunctionInvoker` static analysis (PR #2661, ~15% faster `forofiteration`).
- Skipping the redundant arguments guard in the simple-IR closure-activation fast path (PR #2646).
- `JsValue` struct adoption across numeric/string coercion paths — `ToNumericCore` and `ToJsStringFromObjectValue` (commit bd70ca63); the evaluation-pipeline `JsValue` migration boundary is recorded in ADR 0281 / rule 20 (PR #2651).
- Unified-bytecode production routing widened to label-dependent control flow (gh2679, ADR 0285): labeled statements, labeled loops, labeled block `break`, and labeled `break`/`continue` now route through the compiler-owned resolved-target path; only a labeled abrupt that crosses an enclosing iterator/for-in driver loop still declines (`LabelControlFlow`). This clears "Ranked Next Unsupported Buckets" #5 in `docs/unified-bytecode-expansion-contract.md`.
- Unified-bytecode production routing widened to admit simple **object destructuring** driver shapes (`const { a, b } = obj`, optional identifier rest) via a VM-owned `ObjectDestructuring*` opcode family mirroring the array precedent; computed keys, defaults, and nested patterns still decline model-first. Decision recorded in ADR 0284 (issue gh2677).

Architecture direction is tracked in docs rather than as runtime parity claims: unified-bytecode primary sync-route coverage is recorded (PR #2644), the ordinary sync `this`-binding route is recorded in ADR 0279 (issue #2633), its resumable async/generator counterpart accepting `this`-dependent suspendable functions is recorded in ADR 0283 (issue #2675), and the typed-AST/"dreaming" target is refined in `docs/dreaming.md` — a 4-tier execution model (PR #2647) followed by a self-critique revision (PR #2663), with tier-numbering disambiguation captured as rule 14 (PR #2650). These describe a staged migration with allocation budgets and escape hatches, not current parity.

Conformance against Test262 is tracked via a custom testrunner with baselines in `.testrunner/`. Most reported failures are crash collateral rather than true correctness gaps; real correctness failures are typically under 20.

## Short-Term Goals

- [ ] Continue reducing allocations in the evaluator hot paths (closures, call frames, argument arrays).
- [ ] Expand `JsValue` struct adoption to remaining boxed numeric/string paths.
- [ ] Improve Test262 pass rate by triaging real correctness failures (target: <10 true failures).
- [ ] Land the typed-AST evaluator migration described in the dreaming docs, gated by allocation budgets.

## Long-Term Goals

- [ ] Position Asynkron.JsEngine as a credible Node.js-competitor runtime for .NET hosts (embeddable, fast startup, low allocation).
- [ ] Provide a stable embedding API with host interop (modules, async, timers).
- [ ] Achieve high Test262 conformance (>95% on Language + BuiltIns suites).
- [ ] Offer ahead-of-time / cached compilation of hot scripts for faster warm starts.

## Next Steps

- [ ] (open, gh2665) Triage and fix the top real Test262 correctness failures in the Language suite (target: <10 true failures).
- [ ] (open, gh2668) Add an allocation-regression benchmark gate to CI to guard the evaluator hot-path gains above.
- [ ] (open, gh2678) Continue the unified-bytecode driver-state widening: Slice A (TDZ head environments for sync iterator/for-in drivers) landed via `TdzHeadInit` and ADR 0283; Slice B (awaited iterator/for-in sources) and Slice C (async iterator driver kind) remain declined.

_Generated and maintained by the recurring Roadmapper run._
