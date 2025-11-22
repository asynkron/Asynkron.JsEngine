# Unified Control-Flow Building Blocks

## Goals
- Give loops, iterator handling, yield/await lowering, and async plumbing one set of reusable primitives that can be used by the lowerer, IR builders, the generator interpreter, and the direct typed evaluator.
- Eliminate duplicated AST walks and ad-hoc loop rewrites while preserving strict vs sloppy semantics and the pending-completion model for generators/async generators.
- Make it easy to extend coverage (e.g. new yield shapes, non-blocking awaits) without touching multiple bespoke implementations.

## Current repetition/pain points
- Yield/await detection is duplicated: `Execution/GeneratorYieldLowerer.cs` and `Execution/SyncGeneratorIrBuilder.cs` each have large `ContainsYield`/`StatementContainsYield` and single-yield rewriters; `TypedCpsTransformer` and `TypedAstSupportAnalyzer` walk the same node shapes with slightly different switches.
- Loop scaffolding is repeated in multiple layers: the lowerer rewrites while/do/for to infinite-loop + break forms, the IR builder reimplements jump/branch/loop-scope wiring for while/do/for/for-of/for-await, and the typed evaluator has its own break/continue handling for the same constructs.
- For-of/for-await logic exists in three places with minor differences: `TypedAstEvaluator` (`EvaluateForEach`, `EvaluateForAwaitOf`, iterator protocol helpers), generator IR builder (`TryBuildForOfStatement`, `TryBuildForAwaitStatement`), and generator executor (`ForOfInit/MoveNextInstruction` handling + `ForOfState`/`YieldStarState`-style drivers).
- Await plumbing is split between blocking (`TryAwaitPromise`) and async-aware (`TryAwaitPromiseOrSchedule`) helpers, plus manual resume-slot tracking in `TypedGeneratorInstance` and bespoke code in `EvaluateAwait`. The async iterator paths also duplicate the “pending await” bookkeeping.
- Rollback/loop-scope/label handling is open-coded in many builder methods (repeated instruction checkpoints, push/pop loop scopes, manual failure cleanup), making consistency fixes noisy.

## Proposed building blocks

### 1) AST visitors for shape analysis and rewriting
- Add a reusable `AstShapeVisitor` that can count yields/awaits, detect nested try/catch/switch, and optionally rewrite the first matching yield/await into a placeholder node. Provide flags for “stop on unsupported shape” vs “collect stats”.
- Implement `AstRewriter` helpers for single-yield substitution (resume-slot placeholder) and simple expression rewrites so `GeneratorYieldLowerer`, `SyncGeneratorIrBuilder`, `TypedCpsTransformer`, and `TypedAstSupportAnalyzer` all share the same traversal logic.
- Output a lightweight `YieldUsage`/`AwaitUsage` model (count, delegated flag, hasNestedYield/Await) so call sites can validate policy without re-walking the tree.

### 2) Loop normalization + loop plan
- Introduce a `LoopPlan` abstraction that flattens while/do/for into phases: initializer, test (optional), body, increment (optional), plus metadata for labels, strictness, and required resume slots (e.g. yield in condition/increment).
- Move the existing lowerer rewrites into a dedicated `LoopNormalizer` that emits `LoopPlan` instances (while(true)+break lowering, resume-slot declarations/assignments, condition substitution). This replaces bespoke rewrites in `GeneratorYieldLowerer` and the builder.
- Teach the IR builder to consume `LoopPlan` objects via a shared `LoopBuilder` helper that wires jump/branch instructions, pushes loop scopes, and inserts `StoreResumeValueInstruction`/resume-slot loads as dictated by the plan. This removes duplicated jump/branch setup across while/do/for.
- Provide a matching `LoopRunner` helper for the typed evaluator that runs a `LoopPlan` with standard break/continue handling and (for generators) plumbs pending completions, so the evaluator and IR executor share the same normalized view of loops.

### 3) Iterator/for-of/for-await driver
- Define an `IteratorDriver` state type that owns iterator/enumerator handles, the current stage (init, awaiting `next`, awaiting value), and pending abrupt completion flags. Support both sync and async iterators (including promise-returning `next/throw/return` results).
- Build a single `ForEachPlan` atop `LoopPlan` for `for...of` / `for await...of` that describes how to bind the iteration value (identifier vs destructuring, var/let/const vs assignment) and which await behavior to use.
- Implement shared helpers for protocol discovery (`@@iterator` / `@@asyncIterator`), iterator method invocation (`next/throw/return`), and result object validation used by both the typed evaluator and generator IR executor. Replace the mirrored helpers (`TryGetIteratorFromProtocols`, `IterateIteratorValues`, `ForOfState`, `YieldStarState` iterator setup) with this driver.
- Let `SyncGeneratorIrBuilder`/`AsyncGeneratorIrBuilder` emit generic iterator instructions that consume `IteratorDriver` logic so `TryBuildForOfStatement` and `TryBuildForAwaitStatement` collapse into one templated path.

### 4) Await scheduler and resume plumbing
- Replace `TryAwaitPromise` / `TryAwaitPromiseOrSchedule` split with a single `AwaitScheduler` that either resolves immediately (non-promise) or registers a pending promise with the engine’s event queue, returning a resumable token. No thread-blocking waits.
- Standardize await-site state (`AwaitSiteState` keyed by source span or synthetic symbol) so both the generator executor and plain async functions can stash resolved values without re-evaluating side-effecting expressions.
- Thread the scheduler through `EvaluateAwait`, async iterator drivers, and generator `YieldStar`/`for await...of` paths so pending awaits always surface as resumable completions instead of throwing `NotSupportedException` or blocking.

### 5) IR builder infrastructure helpers
- Add small utilities: `InstructionScope` (checkpoint/rollback), `LoopScopeGuard` (push/pop loop targets), `YieldOperandValidator` (shared nested-yield/delegated checks), and a single `InvalidIndex` sentinel to replace repeated `-1` literals.
- Move yield-star state management (`YieldStarState` creation/reset, pending abrupt completion handling) behind a helper used by both builder and executor, so delegated yields reuse the same bookkeeping regardless of who is driving execution.

## Rollout steps
1. ✅ Land `AstShapeAnalyzer`/rewriter and port `ContainsYield`/`StatementContainsYield`/single-yield rewrites in the lowerer + builder to it. `TypedCpsTransformer` now uses the shared visitor for await detection.
2. ⚙️ Add `LoopPlan` + `LoopNormalizer`, refactor generator yield lowering to emit plans, and teach the IR builder to consume them (including resume-slot declarations). Mirror a `LoopRunner` for the typed evaluator using the same plan shape. (LoopPlan/Normalizer are in place; SyncGeneratorIrBuilder now consumes LoopPlan. Lowerer/runner wiring still pending.)
3. Introduce `IteratorDriver` + `ForEachPlan`, refactor `for...of`/`for await...of` in both the evaluator and generator executor to use it, and collapse IR builder iterator loop code to the shared template.
4. Replace blocking awaits with `AwaitScheduler` wiring in generators, async generators, async functions, and iterator drivers; integrate pending-resume state (`AwaitSiteState`) with the generator executor’s existing resume-slot handling.
5. Apply infrastructure helpers (`InstructionScope`, `LoopScopeGuard`, `YieldOperandValidator`, `InvalidIndex`) across builder code to eliminate ad-hoc rollback/label logic and make future IR extensions less error-prone.
