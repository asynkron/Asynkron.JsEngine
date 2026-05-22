# Expression Bytecode AST Seam Classification

When changing expression bytecode, statement lowering, or IR execution to
remove AST evaluation, classify each AST-seam hit before designing a new
fallback or cleanup.

## Rules

1. Start from the focused runner seam scan:
   `rg "EvaluateExpression\\(|EvaluateExpressionSlow\\(|ProfileEvaluateExpression\\(" src/Asynkron.JsEngine/Ast/TypedAstEvaluator.ExecutionPlanRunner*`.
   Treat direct raw-evaluator hits there as active runtime debt. Do not count
   `EvaluateExpressionProgram(` as a raw AST evaluator; that is the lowered
   expression bytecode execution path.
2. Search historical seam markers with
   `rg "StatementInstruction|AST-evaluated|AstPayloadLeak|AstReentry" src/Asynkron.JsEngine`,
   but classify each hit before acting on it.
3. Do not infer that `StatementInstruction` exists from comments. Confirm a
   concrete instruction type or an `InstructionKind` member before treating it
   as active runtime behavior.
4. Treat `AstPayloadLeak` and `AstReentryDetected` as diagnostic or
   compatibility markers unless a current call site proves they are reachable.
5. Keep legacy AST evaluators, dynamic operand evaluation, and profiling bridges
   separate in reports and implementation plans. A dynamic-only or profiling
   boundary does not justify adding a normal-path AST fallback.
6. If a suspending or nested shape still needs runtime AST evaluation, prefer
   emit-time or lowering-time normalization into existing bytecode/IR
   instructions when JavaScript evaluation order can be proven.
7. Treat runner-file `CS0618` pragmas and old "legacy AST fallback" wording as
   compatibility/comment evidence unless the focused runner scan or a concrete
   call site proves direct AST evaluation. Outer execution fallback ownership
   belongs to planning/lowering diagnostics, not to the runner compatibility
   overload comments.
8. For dynamic JavaScript boundary audits, classify direct eval, `with`,
   Function/AsyncFunction constructors, generated function bodies, and modules
   separately before picking a migration target. Do not group
   `dynamic-but-lowered` eval/with/generated-function paths with the remaining
   module-body dispatch leak unless a current call site proves the same AST
   runtime behavior.
9. When turning a source seam scan into an automated test, assert that the
   expected source files were discovered before asserting zero forbidden calls.
   A source gate that can pass with zero scanned files is not a guardrail.
10. When documenting or planning direct `ExpressionProgramCompiler` coverage,
   start from `docs/expression-bytecode-coverage.md` and keep it complete for
   every concrete `ExpressionNode`. New concrete expression nodes must be added
   to the map with a status (`supported`, `shape-dependent`, `unsupported`, or
   `not-compiled-directly`), the owning failure-code/compiler restriction where
   applicable, and representative test evidence.
11. When reporting or planning `UnsupportedExpressionProgram` backlog work,
   derive the bucket list from the current compiler/diagnostic surfaces and
   explicitly rank catch-all buckets such as `UnsupportedExpressionNode`.
   Treat catch-all buckets as high-risk/deferred until narrower typed buckets
   have been burned down, so one implementation slice does not mix unrelated
   semantic risks.
12. When a source gate proves enum or symbol coverage across generated,
    runtime, diagnostic, or printer surfaces, match exact language tokens rather
    than substrings. For `ExpressionOpKind` coverage, require an
    `ExpressionOpKind.<Name>` token boundary so a longer member such as
    `LoadIdentifierCallTarget` cannot satisfy coverage for `LoadIdentifier`.
13. When expanding `ObjectExpression` bytecode support for static property
    names, normalize syntax key nodes through ECMAScript property-key semantics
    instead of diagnostic formatting. Identifier key nodes should use the
    identifier symbol name; literal key nodes that carry a `JsValue` must route
    through `JsOps.ToPropertyName(...)`, not `JsValue.ToString()` or broad
    `object.ToString()` fallback. Keep computed-key shape validation separate
    from static-key normalization, and prove both the new accepted shapes and
    the still-invalid computed-key shape.
14. When a dynamic JavaScript boundary is classified as lowered or
    dynamic-but-lowered, back that classification with
    `EvaluationContext.AssertNoAstEvaluation` behavior tests for each boundary
    family being claimed. Include direct and indirect eval, `with`,
    `Function`/`AsyncFunction` generated-code constructors, and any adjacent
    generated body shape in the affected slice. A seam scan alone proves only
    that the runner files have no obvious direct call; it does not prove that a
    dynamic entry point still reaches the IR/function pipeline under runtime
    execution.
15. Keep dynamic expression-program bridges explicitly named and source-gated.
    Quarantined legacy or dynamic callers should route through
    `EvaluateDynamicExpressionProgram`, which lowers/caches the dynamic
    expression and throws on lowering failure. Already-lowered payloads should
    continue to use `EvaluateLoweredExpressionProgram`; do not blur these
    surfaces under generic cached-helper naming or add raw AST expression
    fallback on compile failure.
16. When retiring dynamic operand AST seams, migrate one operand family at a
    time through the dynamic `ExpressionProgram` bridge when the operand only
    needs a value. Dynamic return operands are the reference shape: evaluate
    the return expression with `EvaluateDynamicExpressionProgram(...)`, preserve
    precise unsupported-bytecode failures, and prove the path with a focused
    DEBUG `EvaluationContext.AssertNoAstEvaluation` regression. Keep
    suspending operands (`await`, `yield`), assignment/name-inference operands,
    and delete-default operands as separate slices until their evaluation order,
    resume, and side-effect semantics are proven.
17. When renaming, removing, or splitting expression-program bridge APIs, search
    both the primary IR/runtime files and the quarantined legacy AST evaluator
    directory for stale bridge calls. Legacy dynamic boundaries may still be the
    caller that routes AST-owned statements into lowered `ExpressionProgram`
    execution; a stale call there can make `main` fail to compile even when the
    normal runner path and focused AST-free tests look correct.
18. For `UnsupportedExpressionProgram` backlog issues, prove the selected bucket
    on the current worktree before patching. If focused compiler/lowering tests
    plus the AST-seam scan already satisfy the issue intent, direct-close it
    with concise evidence instead of adding nearby speculative code churn.
19. Source-gate tests for runner AST expression seams must match every raw AST
    evaluator entry point by explicit name: `EvaluateExpression(`,
    `EvaluateExpressionSlow(`, and `ProfileEvaluateExpression(`. The regex must
    not accidentally match `EvaluateExpressionProgram(`, because bytecode
    execution through `ExpressionProgram` is the intended non-dynamic fast path.
20. When refreshing an AST-seam baseline without changing runtime code, record
    the current remaining owner surfaces, not just the clean runner scan result.
    For issue #1479, the required owner surfaces were runner await-state
    handling via `EvaluateAwaitInGenerator(...)`, the dynamic bridge via
    `EvaluateDynamicExpressionProgram(...)`, and legacy await/yield operand
    evaluation in `Ast/Legacy/ExpressionNodeExtensions.cs`. Future refreshes
    should update that owner list when it changes so downstream bytecode work
    starts from the right ownership boundary.
21. When expanding `docs/expression-bytecode-coverage.md`, keep the per-node
    family rows and the global `ExpressionProgramFailureCode` bucket index as
    separate source-of-truth surfaces. A bucket can belong in the global index
    even when no current per-node row should claim that compiler path directly;
    do not "fix" a row-level overclaim by dropping the enum value from the
    global index.
22. When expanding static dot-access member-expression bytecode support,
    normalize the property node by its syntax-owned static name. Non-computed
    `MemberExpression` property nodes may be represented as either string
    `LiteralExpression` or `IdentifierExpression`; support both in the compiler
    normalization path and keep computed-member validation separate. Do not add
    a runtime AST fallback to cover a static-name shape gap. Include a focused
    lowering test for the accepted property-node shape, especially when the gap
    was found through diagnostic/backlog surfaces rather than ordinary parser
    output.

## Dynamic Boundary Classification (#1405 Retry)

Use this boundary map when documenting or planning expression-bytecode vs AST
work. Do not collapse these seams into one generic "AST fallback" bucket.

1. Direct `eval`: dynamic-but-lowered. `EvalHostFunction` parses source, builds
   eval environments, and executes through
   `EvaluateProgram(..., ExecutionKind.Eval, ...)`.
2. `with` statements: IR-only for supported shapes. `WithEmitter` lowers the
   object expression into expression bytecode and emits
   `EnterWithInstruction`/`LeaveWithInstruction`; dynamic lookup is handled by
   runtime scope behavior, not AST expression walking.
3. `Function` / `AsyncFunction` constructors: dynamic-but-lowered. Generated
   source is parsed and executed through the normal program/function execution
   pipeline.
4. Normal/generated sync function bodies: IR runner path. Supported functions
   execute cached `ExecutionPlan` via `ExecutionPlanRunner`; expected plan
   failures throw instead of silently falling back to AST evaluation.
5. Module body dispatch: AST-runtime leak / unclear migration target.
   Non-import/export module statements still run through per-statement wrapper
   execution in `JsEngine` and should be treated as the next migration slice.
6. Expression payloads on inspected IR paths: bytecode-backed via
   `ExpressionProgram` and `EvaluateExpressionProgram`, not raw AST expression
   walking.

## Why

Issue #1391 audited AST runtime seams before bytecode expansion. The audit found
no direct `EvaluateExpression(` or `ProfileEvaluateExpression(` hits in
`TypedAstEvaluator.ExecutionPlanRunner*`, while broader searches found stale
`StatementInstruction` comments, diagnostic enum values, legacy evaluators,
dynamic-only boundaries, and a profiling bridge. Future bytecode work needs
that classification discipline so stale references do not create new mixed
AST/IR fallback paths and real legacy boundaries remain visible follow-up work.

Issue #1405, retried by #1414, applied the same lesson to dynamic boundaries.
Direct eval and Function constructors parse dynamic source and then lower
through script or function IR; supported `with` execution is already lowered
while deliberately using dynamic environment lookup instead of user slot fast
paths. The durable next slice is module body execution, where non-import/export
statements still pass through a per-statement wrapper rather than an explicit
module-body plan/cache. Future agents need this split so eval/with work does not
absorb the module-body migration or accidentally remove dynamic-scope
safeguards.

Issue #1408 added execution-plan diagnostics drift gates. Review found the
runner seam source-gate test could pass vacuously if no
`TypedAstEvaluator.ExecutionPlanRunner*.cs` files were found, so source-scan
tests must prove discovery before they claim absence of forbidden AST seams.

Issue #1435 reran the focused runner seam scan and confirmed the direct runner
call surface was still clean. The durable lesson was not a runtime change: stale
runner comments and `CS0618` rationale can make compatibility/resume overloads
look like active AST fallbacks, while actual outer fallback classification lives
in `ExecutionPlanBuilder`, emitter diagnostics, and
`ExpressionProgramCompileFailure` / `SetExpressionProgramFailure(...)`.

Issue #1437 added `docs/expression-bytecode-coverage.md` and
`ExpressionProgramCoverageMapTests.CoverageMap_ListsEveryConcreteExpressionNodeType`
after the bytecode audit needed a durable handoff. The risk was future agents
claiming coverage from spot checks while newly added `ExpressionNode` types
silently escaped the map. The reflection guard keeps the map complete, while
the map keeps support status and failure-code ownership explicit.

Issue #1436 / PR #1443 added the first durable
`UnsupportedExpressionProgram` backlog report for bytecode expansion. Review
found that the initial report ranked the narrower buckets but did not explicitly
rank the broad `UnsupportedExpressionNode` catch-all, which could have made a
future agent pick a mixed-risk implementation slice. Future backlog reports
must classify and defer catch-all buckets until narrower diagnostics make the
remaining work specific.

Issue #1487 / PR #1495 burned down the current third high-value expression
bucket, `UnsupportedDotAccessPropertyName`. The friction was that static
dot-access property nodes could be identifier-shaped even though the expression
compiler only accepted string literal property nodes. The durable lesson is to
normalize static member property-name syntax at compile time, prove the new
accepted shape with lowering coverage, and preserve computed-member validation
as a separate unsupported/shape-dependent path.

Issue #1440 / PR #1449 added an allowlist-free `ExpressionOpKind` drift gate
across runner dispatch, stack-depth analysis, and execution-plan printer
formatting. Review found the first implementation used substring matching, so a
longer enum member could hide missing coverage for a shorter enum member. Future
source gates must encode the token shape they claim to prove, otherwise a
guardrail can pass while the runtime or diagnostic surface has still drifted.

Issue #1442 / PR #1453 expanded static object literal key support for expression
bytecode. Review caught that `LiteralExpression` key nodes carrying `JsValue`
must use JavaScript property-name conversion, because diagnostic `ToString()`
can leak string quotes or a BigInt `n` suffix into lowered property names. Future
object-literal bytecode slices should keep parser-literal normalization, AST key
node normalization, and computed-key validation as separate proof points.

Issue #1445 / PR #1455 added missing AST-free boundary proofs after the dynamic
boundary classification work had already documented eval/with/generated-code
paths as lowered or dynamic-but-lowered. The gap was not a runtime fix; it was
that indirect eval plus `Function` and `AsyncFunction` constructor execution
lacked explicit `AssertNoAstEvaluation` tests. Future agents should turn
classification claims for dynamic boundaries into executable proof coverage,
not rely only on seam scans or adjacent direct-eval/with tests.

Issue #1446 / PR #1456 locked down the dynamic expression bridge after
investigation found `EvaluateCachedExpressionProgram` was semantically a
dynamic-boundary helper but named like a general cache path. The delivery
renamed it to `EvaluateDynamicExpressionProgram`, kept unsupported lowering as
an explicit throw instead of a raw AST expression fallback, and added a source
gate for approved dynamic call sites. Future AST-seam work should preserve that
classification so dynamic bridge callers cannot drift into normal
already-lowered expression-program execution, and already-lowered class or
initializer payloads do not get mislabeled as legacy fallback.

Issue #1447 / PR #1457 converted the dynamic return-expression boundary from
`EvaluateDynamicExpressionOperand(...)` to the dynamic expression-program bridge.
The important constraint is scope: a return operand only needs a `JsValue` before
setting the return completion, so it can be bytecode-backed without changing
suspension or name-inference behavior. Future dynamic operand work should repeat
that narrow proof shape instead of mixing return, await, yield, assignment, and
delete semantics in one migration.

Issue #1461 / PR #1462 repaired a `main is red` compile failure after
`EvaluateCachedExpressionProgram` was removed but the legacy dynamic
return-expression boundary still called it. The correct caller is
`EvaluateDynamicExpressionProgram`, because this seam starts in quarantined AST
statement evaluation but must execute the return expression through the dynamic
expression-program bridge. Future bridge refactors need a whole-AST bridge-call
search, including `src/Asynkron.JsEngine/Ast/Legacy`, before they claim the
rename/removal is complete.

Issue #1395 direct-closed an optional-tagged-template
`UnsupportedExpressionProgram` bucket after current-worktree proof showed the
bucket was already implemented and AST-free in focused checks. Future backlog
burn-down slices should repeat that proof-first/direct-close behavior instead
of treating adjacent unsupported buckets as automatic patch prompts.

Issue #1482 / PR #1480 strengthened the ExecutionPlanRunner AST-seam source gate
after the old guard matched `EvaluateExpression(` and
`ProfileEvaluateExpression(` but did not explicitly cover
`EvaluateExpressionSlow(`. The fix consolidated the guard around raw evaluator
names while documenting that `EvaluateExpressionProgram(` remains allowed,
because it executes already-lowered expression bytecode instead of walking AST
expressions.

Issue #1479 refreshed the ADR 0093 seam baseline after review found the first
evidence update did not name the current remaining owner surfaces. The incident
shows that a clean runner scan is necessary but insufficient for handoff:
without owner names, future bytecode agents can still lose track of which
quarantined boundaries remain intentional follow-up surfaces.

Issue #1480 / PR #1485 expanded the ExpressionProgram coverage map from node
presence into grouped family/risk/source-of-truth documentation. Review found
two distinct fidelity traps: `UnaryExpression` should not claim
`UnsupportedDeleteTarget` as a direct `TryCompileUnaryExpression` bucket, but
the global failure-code bucket index still must include `UnsupportedDeleteTarget`
because it exists in the current compiler/classification/diagnostic surface.
Future coverage-map edits need to validate row claims and global enum coverage
separately so baseline documentation stays useful for bytecode planning.

Issue #1509 refreshed the dynamic-boundary ownership map for eval, generated
function constructors, `with`, async/generator invocation, and module-body
execution. The focused seam/boundary scans stayed consistent with this rule:
eval/Function/AsyncFunction and supported `with` paths are dynamic-but-lowered
or IR-owned, while module-body per-statement dispatch in `JsEngine` remains the
primary migration-debt surface. Future bytecode slices should keep those owners
split instead of treating all dynamic entry points as one AST-fallback bucket.
