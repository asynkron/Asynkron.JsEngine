# ADR 0380: Retire class-constructor runner fallback through dynamic constructor evaluator

## Status

Accepted

## Context

Faktorial issue
`planitem-planitem-gh3495-shared-context-e5b-runner-entry-point-tombstones-after-e-e3f725d87e`
and delivery PR #3554 retired the E5d class-constructor
`ExecutionPlanRunner.RunSync()` fallback residue from
`SyncFunctionInvoker`.

Before the delivery, class constructors still had a classified
`CreateClassifiedClassConstructorFallbackRunner(...)` bridge and simple
base/derived constructor paths that could execute an `ExecutionPlanRunner`
after production unified bytecode declined. The parent proof plan wanted that
bridge converted from a source-presence allowlist into a source-absence
tombstone, while preserving base and derived constructor semantics:
`this`, `new.target`, private brands, instance fields, default derived
constructor rest arguments, `super()`, and constructor return normalization.

The first delivery slice removed the runner bridge and let production
unified-bytecode constructor attempts fall through to the existing dynamic-scope
constructor evaluator when production declined. It also made lowered expression
execution inherit `Symbol.NewTarget` so the dynamic constructor path preserved
`new.target` through standalone lowered expression evaluation.

The quality repair exposed an adjacent FunctionCode trap. A broad script-mode
IR-seam bypass could route production unified bytecode for shapes that still
needed FunctionCode isolation: observable `arguments`, `NeedsArgumentsBinding`,
legacy tail-restart var resets, and strict block-scoped function declarations.
Those are activation semantics boundaries, not class-constructor tombstone
proof. The repair narrowed the bypass so production IR stays available for
safe strict hoistable functions, while the observable FunctionCode shapes keep
the older isolation route.

## Decision

Retire the class-constructor fallback runner by splitting constructor fallback
ownership from ordinary sync-function fallback ownership.

- Class constructors may attempt production unified bytecode first.
- If production unified bytecode declines, class constructors fall through to
  the existing dynamic-scope constructor evaluator; they must not construct a
  class-constructor `ExecutionPlanRunner` or call `runner.RunSync()`.
- The ordinary sync-function declined-body fallback runner remains classified
  separately. It is not evidence that class-constructor runner residue remains.
- The proof manifest should keep the class-constructor row as
  `source-absence` / `retired-fallback` for
  `CreateClassifiedClassConstructorFallbackRunner`, while the remaining
  `runner.RunSync();` allowlist belongs only to the ordinary sync fallback row.

Keep FunctionCode script-mode IR routing guarded separately from constructor
tombstone work. A FunctionCode seam can be bypassed for production unified
bytecode only when the plan is production-eligible and the function has no
observable arguments path, no needed arguments binding, no legacy tail-restart
reset vars, and no strict block-scoped function-declaration instruction.

## Consequences

- E5d class-constructor runner proof now has a tombstone instead of a live
  runner construction allowlist.
- Dynamic constructor evaluation remains the semantic fallback for constructor
  shapes that production unified bytecode does not own yet.
- Future class-constructor runner cleanup must not collapse ordinary sync
  function fallback, constructor fallback, and script-mode FunctionCode seams
  into one broad "IR can run" decision.
- Future FunctionCode repairs need paired proof for the positive production
  route and the negative activation-isolation shapes, especially observable
  `arguments`, legacy restart reset, and strict block-scoped function
  declarations.

## Evidence

- ADR allocation note: local `rtk faktorial-api adr-next` was unavailable in
  this worker (`No such file or directory`), so this learn pass used the
  runtime allocator endpoint `POST /api/adrs/next`, which returned
  `{"adr_id":380}`. The prefix `0380` was checked free before writing.
- Delivery PR #3554 merged as commit
  `ca22064c5e5bfc497d4c0467cde794537a687028`.
- Delivery commits recorded in the Faktorial issue comments:
  - `60c44e61b Retire class constructor runner fallback`
  - `e8e70615f Repair class constructor runner tombstone tests`
  - `37c29e0b0 Preserve production IR for strict hoistable functions`
  - `42903b471 Repair FunctionCode runner tombstone quality gate`
- The delivery changed:
  - `docs/plans/bytecode-proof-manifest.json`
  - `src/Asynkron.JsEngine/Ast/LoweredExpressionProgramCache.cs`
  - `src/Asynkron.JsEngine/Ast/TypedAstEvaluator.SyncFunctionInvoker.cs`
  - `tests/Asynkron.JsEngine.Tests/AstFreeExecutionAssertionTests.cs`
  - `tests/Asynkron.JsEngine.Tests/BytecodeProofManifestTests.cs`
  - `tests/Asynkron.JsEngine.Tests/ClassStatementTests.cs`
  - `tests/Asynkron.JsEngine.Tests/UnifiedBytecodeProductionConstructCallTests.cs`
  - `tests/Asynkron.JsEngine.Tests/UnifiedBytecodeProductionInvocationTests.cs`
- Build-stage verification recorded the required
  `BytecodeProofManifestTests` proof passing with 234 tests, focused
  FunctionCode quality repairs passing, the broader class-constructor/manifest
  pack passing with 267 tests, and `git diff --check` passing.

## Related

- `docs/plans/bytecode-proof-manifest.json`
- `docs/rules/function-activation-proof-pack.md`
- ADR 0146:
  `docs/adrs/0146-keep-functioncode-activation-isolation-ahead-of-ir-fast-paths.md`
- ADR 0225:
  `docs/adrs/0225-keep-base-class-constructor-ir-activation-binder-guarded.md`
- ADR 0230:
  `docs/adrs/0230-keep-derived-class-constructor-ir-activation-super-owned.md`
- ADR 0243:
  `docs/adrs/0243-keep-class-constructor-fast-path-dispatch-shape-gated.md`
- ADR 0353:
  `docs/adrs/0353-keep-private-name-class-constructors-off-production-bytecode-until-lexical-state-is-owned.md`
