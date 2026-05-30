# Asynkron.JsEngine Roadmap

_Last updated: 2026-05-30_

## Current State

Asynkron.JsEngine is a tree-walking JavaScript interpreter for .NET with broad ECMAScript coverage. Recent work has concentrated on the typed-AST evaluator and allocation reduction in hot paths:

- Per-`FunctionExpression` caching of `SyncFunctionInvoker` static analysis (PR #2661, ~15% faster `forofiteration`).
- Typed-AST member-access caching, hardened against poisoned/stale cache entries (PRs #2646, #2659, #2644).
- Cached typed-AST property keys to cut `ToPropertyKey` churn (PR #2658) and cached argument handling on call expressions (PRs #2654, #2650).
- Cached resolved variable slots in typed-AST identifiers and skipped redundant typed-AST conversion in cached invokers (PRs #2651, #2656).
- `JsValue` struct adoption across numeric/string coercion paths (`ToNumericCore`, `ToJsStringFromObjectValue`, commit bd70ca63).

A typed-AST/"dreaming" evaluator vision is being refined in docs (PRs #2652, #2655, #2660, #2663), covering a staged migration with allocation budgets and escape hatches.

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

_Generated and maintained by the recurring Roadmapper run._
