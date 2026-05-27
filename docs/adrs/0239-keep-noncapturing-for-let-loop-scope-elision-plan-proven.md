# ADR 0239: Keep noncapturing for-let loop-scope elision plan-proven

## Status

Accepted

## Context

Issue #2402 / PR #2412 targeted the remaining `activation-arguments-lite`
overhead where `HandlePushEnvironment` stayed hot after earlier activation
optimizations. ADR 0233 had already rejected retrying loop-scope slot-template
clear/stamp micro-optimizations unless fresh selected-profile evidence cleared
the performance gate. The next viable boundary was semantic: avoid creating the
extra parent loop-scope environment only when lowering proves that environment
identity is unobservable.

The accepted delivery added a narrow `for (let ...)` loop-scope elision path in
loop emission. Eligibility is owned by the `LoopPlan` shape and supporting
detectors rather than benchmark names or source-text assumptions. The retained
shape is an ordinary `for` loop with one simple `let` declarator, per-iteration
bindings, no condition prologue, no closure-bearing body, no direct eval or
`with`, no suspension, and a reusable iteration-environment plan. The first
iteration environment owns the initializer, and the extra parent loop-scope
environment is omitted only for that proven non-capturing shape.

Review found one missing closure-bearing construct after the first pass:
`ClassDeclaration` had been skipped by `InnerFunctionBlockDetector`, even
though class methods can capture the loop binding just like class expressions.
That let the elision reuse the iteration environment for a loop where a class
method should have retained the final per-iteration binding. The repair treats
class declarations as closure-bearing and pins the class-method capture shape.

## Decision

Keep noncapturing `for (let ...)` loop-scope elision plan-proven and
conservative.

Future loop-environment elision or reuse work must preserve this boundary:

1. The emitter may elide the parent loop-scope environment only from explicit
   lowering/plan eligibility, not from profile names, source text, benchmark
   shape, or runtime inference.
2. The eligible shape remains narrow: ordinary `for`, one simple `let`
   declarator that matches the single per-iteration binding, no condition
   prologue, no direct eval or `with`, no await/yield suspension, no
   destructuring or multi-binding initializer, and reusable iteration
   environment metadata.
3. Closure-bearing syntax must keep the parent loop scope. This includes
   function declarations, function expressions, arrows, object literal methods,
   class expressions, and class declarations because class methods can capture
   the loop binding.
4. If a later slice widens the shape, it must widen the semantic detectors and
   focused loop-environment proofs in the same delivery.

## Consequences

- The retained performance win comes from changing an unobservable environment
  lifetime boundary, not from reattempting the reverted slot-template
  initialization shape from ADR 0233.
- `InnerFunctionBlockDetector` is now part of the loop-scope elision contract.
  Omitting a closure-bearing AST node can turn a performance optimization into
  a JavaScript scoping bug.
- Class declarations must be treated like class expressions for capture
  conservatism in this context, even if the visitor does not descend into their
  method bodies for ordinary traversal.
- Focused proof for future edits should pair the positive non-capturing elision
  test with negative cases for function closure capture, class declaration
  method capture, direct eval / `with`, and suspension.

## Evidence

- Delivery PR #2412 merged as commit
  `b101b5f6 Agent: issue #2402 (#2412)`.
- Build-stage final repair commit
  `6a1af51a Fix class declaration loop capture elision` treated
  `ClassDeclaration` as closure-bearing in `InnerFunctionBlockDetector`.
- The review probe before the repair expected `2` but observed `3` for a class
  method capturing a loop `let` binding; after the repair,
  `SyncForLoop_ClassDeclarationMethodCapture_KeepsParentLoopScope` verifies
  result `2` and both parent and per-iteration environments in the plan.
- Focused build-stage proof passed:
  `rtk dotnet test tests/Asynkron.JsEngine.Tests -c Release --filter "FullyQualifiedName~IrLoopEnvironmentTests|FullyQualifiedName~ActivationSemanticsProofPackTests" -- xUnit.MaxParallelThreads=1 -timeout 20000`
  with 82 tests passing and existing nullable warnings.
- Review-stage verification reran the same focused proof pack with 82 tests
  passing, ran `rtk dotnet build -c Release`, and found no review findings.

## Related

- `docs/adrs/0130-keep-for-statement-lexical-head-closures-bound-to-loop-head.md`
- `docs/adrs/0233-keep-activation-loop-scope-template-retries-performance-gated.md`
- `.claude/rules/ir-control-flow-cleanup.md`
- `.claude/rules/function-activation-proof-pack.md`
- `src/Asynkron.JsEngine/Ast/ScopeAnalysisVisitors.cs`
- `src/Asynkron.JsEngine/Execution/Emitters/LoopEmitter.cs`
- `src/Asynkron.JsEngine/Execution/Emitters/LoopEmitterHelpers.cs`
- `tests/Asynkron.JsEngine.Tests/ActivationSemanticsProofPackTests.cs`
- `tests/Asynkron.JsEngine.Tests/IrLoopEnvironmentTests.cs`
