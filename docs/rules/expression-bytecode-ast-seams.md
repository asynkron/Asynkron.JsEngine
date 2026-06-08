# Expression Bytecode AST Seam Classification

When changing expression bytecode, statement lowering, or IR execution to
remove AST evaluation, classify each AST-seam hit before designing a new
fallback or cleanup.

## Rules

1. Start from the focused runner seam scan:
   `rg "EvaluateLegacyAstExpression\\(|EvaluateLegacyAstExpressionSlow\\(|ProfileEvaluateExpression\\(" src/Asynkron.JsEngine/Ast/TypedAstEvaluator.ExecutionPlanRunner*`.
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
    `UnifiedBytecodeExpressionProgramExecutor.ExecuteDynamic`, which
    lowers/caches the dynamic expression and throws on lowering failure.
    Already-lowered payloads should use
    `UnifiedBytecodeExpressionProgramExecutor.ExecuteStandalone`; do not blur
    these surfaces under generic cached-helper naming or add raw AST expression
    fallback on compile failure. `EvaluateDynamicExpressionProgram(...)` is
    tombstoned.
16. When retiring dynamic operand AST seams, migrate one operand family at a
    time through the dynamic `ExpressionProgram` bridge when the operand only
    needs a value. Dynamic return operands are the reference shape: evaluate
    the return expression with
    `UnifiedBytecodeExpressionProgramExecutor.ExecuteDynamic(...)`, preserve
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
    evaluator entry point by explicit name: `EvaluateLegacyAstExpression(`,
    `EvaluateLegacyAstExpressionSlow(`, and `ProfileEvaluateExpression(`. The regex must
    not accidentally match `EvaluateExpressionProgram(`, because bytecode
    execution through `ExpressionProgram` is the intended non-dynamic fast path.
20. When refreshing an AST-seam baseline without changing runtime code, record
    the current remaining owner surfaces, not just the clean runner scan result.
    For issue #1479, the required owner surfaces were runner await-state
    handling via `EvaluateAwaitInGenerator(...)`, the dynamic executor via
    `UnifiedBytecodeExpressionProgramExecutor.ExecuteDynamic(...)`, and legacy
    await/yield operand evaluation in `Ast/Legacy/ExpressionNodeExtensions.cs`.
    Future refreshes
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
23. When adding `EvaluationContext.AssertNoAstEvaluation` coverage for an
    intentional dynamic seam, pair it with an ordinary non-dynamic execution path
    in the same fixture. Exercise the ordinary path before the dynamic seam and
    again afterward so the test proves the seam remains quarantined and does
    not mask ordinary function, generator, or control-flow re-entry into legacy
    AST evaluation. Issue #1510 / PR #1512 added this pattern for direct `eval`
    plus an ordinary function and for `with` plus an ordinary generator.
24. When expanding tagged-template bytecode support for static member targets,
    normalize member property nodes by their syntax-owned static name at compile
    time. Non-computed tagged-template member targets may expose the property as
    either a string `LiteralExpression` or an `IdentifierExpression`; support
    both without adding a runtime AST fallback. Keep computed, optional, and
    super tagged-template validation separate, and prove the accepted member
    shape with focused lowering plus runtime receiver tests.
25. Quarantine guard source-gate tests have a two-phase lifecycle. Phase 1
    (`Assert.Single`) proves "isolated to exactly one definition site." Phase 2
    (`Assert.Empty`) proves "tombstoned — no remaining reference." Treat the
    transition from Phase 1 to Phase 2 as a mandatory companion step whenever
    the quarantined method is fully deleted; rename the test to reflect
    "IsCompletelyRemoved" instead of "FindsNoCallers" so its intent is
    unambiguous. Missing this update causes a build failure on an otherwise
    clean deletion commit.
26. Keep sync `with` admission and resumable `with` quarantine separate. Sync
    non-awaited `with` statements can route through production unified bytecode
    when ADR 0269's activation-hoist and receiver rules hold. Resumable
    generator, async, and async-generator bodies must still decline any
    reachable `EnterWithInstruction` or `LeaveWithInstruction`, including
    awaited with-object evaluation, until the VM owns active dynamic-scope
    suspension state explicitly. Closure-retained live `with` environments are
    a separate dynamic-residue boundary: a function created inside `with` and
    returned after the active statement ends must still decline production
    routing until the VM owns retained with-object environment lookup and
    receiver-sensitive call-target preparation. Do not repair these boundaries
    with an AST fallback, by rolling back sync current-environment `with`
    admission, or by treating retained `with` closures as ordinary captured
    lexical closures.
27. Keep standalone `ExpressionProgram` execution centralized behind
    `UnifiedBytecodeExpressionProgramExecutor`, and do not route it through the
    AST evaluator or IR runner.
    `ExecutionPlanRunner.EvaluateStandaloneExpressionProgram(...)` and
    `ExecutionPlanRunner.ProfileEvaluateExpressionProgramLoop(...)` are
    tombstoned. `EvaluateLoweredExpressionProgram(...)` is also tombstoned; new
    standalone expression-program callers should use the unified-bytecode
    executor directly, and new occurrences of the deleted helper must fail the
    source gate. `ExecutionPlanRunner.ApplyStandaloneBindingTargetProgram(...)`
    is tombstoned too; external lowered binding-target callers should use the
    static lowered binding-target core and route nested expression payloads
    through standalone unified bytecode instead of constructing a runner solely
    for binding-target execution. If any other quarantined helper is deleted,
    change its guard from classified allowlist to tombstone instead of leaving
    stale permission. See ADR 0345.
28. Treat D1/D2/D3 and A2 dynamic-activation residue rows as terminal dynamic
    fallback guardrails, not parking buckets for ordinary bytecode work. When a
    retrospective or rebaseline touches direct eval, `with`, retained dynamic
    environments, or `Function(...)` bodies, explicitly keep ordinary compiler,
    call-boundary, class/static-block, `ExpressionProgram`, and
    `ExecutionPlanRunner` retirement work in their A/B/E owner rows. Pair
    admitted vs open claims with exact proof-manifest rows, and split sync and
    resumable proofs when the runtime entry point differs.
29. When rebaselining standalone `ExpressionProgram` executor call sites for
    class definitions, classify each `ExecuteStandalone(...)` occurrence by the
    semantic payload owner instead of treating the whole file as one generic
    allowance. Class `extends`, computed class member names, computed class
    field names, and class field initializers are E4 class-definition standalone
    payloads when their caches expose lowered `ExpressionProgram` values.
    Preserve the adjacent static-block `ExecutionPlanRunner.RunScript(...)`
    fallback as E5/static-block runner-retirement residue unless a separate
    proof shows that body routing has moved. Issue #3377 / PR #3438 added this
    rule after the finite bytecode retirement inventory needed to separate
    class-definition payload execution from static-block body fallback
    ownership.

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
no direct `EvaluateLegacyAstExpression(` or `ProfileEvaluateExpression(` hits in
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

Issue `planitem-planmanual1780639098493226000-full-unified-bytecode-execution-burndown-b-a97892fc0c`
/ PR #3264 closed D3 by making reachable resumable `with` markers an explicit
dynamic-residue decline, including awaited with-object evaluation, while
preserving sync non-awaited `with` admission. The incident matters because
nearby B40/B43 rows looked like ordinary resumable parity work, but admitting
them safely requires VM-owned dynamic-scope suspension state rather than an
allowlist extension or AST callback. See ADR 0344.

Issue
`planitem-planitem-planmanual1780730299657353000-unified-bytecode-remaining-burndo-8acd1c43ab`
/ PR #3314 pinned closure-retained live-`with` environments as precise
dynamic residue. The incident matters because ordinary captured lexical
closures already route, and active current-environment `with` can route, but a
closure that retains a with-object environment needs a different ownership
model. Receiver-sensitive calls supplied by the retained with object are the
observable tripwire. See ADR 0351.

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

The plan-child issue
`planitem-planmanual1779454308935867000-push-bytecode-from-diagnostics-toward-runt-81dce20ea4`
and PR #1553 applied the same static member-name normalization lesson to
tagged-template member targets. The compiler gap was not a reason to add a
runtime AST fallback: tagged-template lowering needed to accept both literal
and identifier-shaped non-computed member names, preserve the existing
computed/optional/super validation buckets, and prove receiver behavior through
focused tests.

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

Later E4 bridge-retirement work moved that cache/lower/execute behavior into
`UnifiedBytecodeExpressionProgramExecutor.ExecuteDynamic(...)` and tombstoned
`EvaluateDynamicExpressionProgram(...)`. Future dynamic callers should use the
unified-bytecode executor directly.

Issue #1447 / PR #1457 converted the dynamic return-expression boundary from
`EvaluateDynamicOrSuspendingExpressionOperand(...)` to the dynamic expression-program bridge.
The important constraint is scope: a return operand only needs a `JsValue` before
setting the return completion, so it can be bytecode-backed without changing
suspension or name-inference behavior. Future dynamic operand work should repeat
that narrow proof shape instead of mixing return, await, yield, assignment, and
delete semantics in one migration.

Issue #1461 / PR #1462 repaired a `main is red` compile failure after
`EvaluateCachedExpressionProgram` was removed but the legacy dynamic
return-expression boundary still called it. The correct caller is
the dynamic expression-program bridge, now
`UnifiedBytecodeExpressionProgramExecutor.ExecuteDynamic(...)`, because this
seam starts in quarantined AST statement evaluation but must execute the return
expression through bytecode. Future bridge refactors need a whole-AST
bridge-call search, including `src/Asynkron.JsEngine/Ast/Legacy`, before they
claim the rename/removal is complete.

Issue #1395 direct-closed an optional-tagged-template
`UnsupportedExpressionProgram` bucket after current-worktree proof showed the
bucket was already implemented and AST-free in focused checks. Future backlog
burn-down slices should repeat that proof-first/direct-close behavior instead
of treating adjacent unsupported buckets as automatic patch prompts.

Issue #1482 / PR #1480 strengthened the ExecutionPlanRunner AST-seam source gate
after the old guard matched `EvaluateLegacyAstExpression(` and
`ProfileEvaluateExpression(` but did not explicitly cover
`EvaluateLegacyAstExpressionSlow(`. The fix consolidated the guard around raw evaluator
names while documenting that `EvaluateExpressionProgram(` remains allowed,
because it executes already-lowered expression bytecode instead of walking AST
expressions.

Issue #1511 / PR #1511 renamed the quarantined legacy helpers away from generic
`EvaluateExpression` / `EvaluateExpressionSlow` wording and updated the source
gates to match explicit raw evaluator names. The incident showed that helper
names are part of the guardrail: generic names make seam scans and handoffs
look like normal-path AST evaluation, while explicit legacy/dynamic/suspending
names keep dynamic boundaries and bytecode execution separated.

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

Issue #1510 / PR #1512 added paired AST-free runtime proofs for dynamic seam
quarantine. Direct `eval` and `with` generator seams were intentionally allowed
under `EvaluationContext.AssertNoAstEvaluation`, but each test also ran the
ordinary non-dynamic path before and after the dynamic seam. The durable lesson
is that dynamic-boundary approval is not enough by itself: tests must prove the
ordinary path still fails loudly if it regresses to legacy AST evaluation and
that exercising the dynamic seam does not hide later ordinary-path re-entry.

Issue
`planitem-planmanual1780639098493226000-full-unified-bytecode-execution-burndown-b-37fc1c9650`
/ PR #3271 closed E4 as a fallback-boundary guardrail. The issue was not that
lowered `ExpressionProgram` execution is AST evaluation; it was that direct
standalone runner calls outside the bridge made production-route scans unable
to distinguish VM-owned routing from fallback-only lowered expression
execution. Future E4 work must keep direct runner calls centralized, classify
every lowered expression-program caller, and preserve `newTarget` through the
bridge. See ADR 0345.

Issue
`planitem-planmanual1780730299657353000-unified-bytecode-remaining-burndown-05-fal-5c9c48de33`
/ PR #3360 tightened the same E4 guard after a broad file-level allowlist still
let distinct roles coexist inside legitimate owner files. The durable lesson is
that the guard should classify the call's semantic purpose, using the exact
line or nearby marker when necessary, so a new caller in an already approved
file cannot silently inherit bridge, dynamic-boundary, class-field, or
fallback-only permission. See ADR 0345.

Issue
`planitem-planitem-planmanual1780730299657353000-unified-bytecode-remaining-burndo-dc78f9b61a`
/ PR #3375 replayed the helper-ownership guardrail on current `main` and moved
the proof into `docs/plans/bytecode-proof-manifest.json`. The durable lesson is
that retired profiling bridges must be source-gated across both engine code and
`tools/ProfileRunner`, and standalone executor ownership should use a manifest
allowlist so new call sites outside the approved helper-owned surface fail the
ordinary proof-manifest test suite. See ADR 0345.

Issue
`planitem-planitem-planitem-gh3377-rebaseline-the-finite-bytecode-retirement-inven-b668e36a1a`
/ PRs #3388 and #3391 rebaselined the finite bytecode retirement inventory after
A2 direct-eval evidence drift. The incident matters because declaration-free
direct eval had admitted sync and resumable subsets, while arguments-dependent
resumable eval, retained live-`with`, runtime-source eval, and declaration
instantiation remained intentionally open or terminal residue. Without a rule,
future agents could treat D1/D2/D3 as convenient open buckets and hide ordinary
A51, B24h, B36, E4, or E5 retirement work there. Future rebaselines should keep
those ordinary rows visible, use manifest rows for executable claims, and name
sync vs resumable proof rows separately when their entry points differ.

Issue
`planitem-planitem-planitem-gh3377-rebaseline-the-finite-bytecode-retirement-inven-78b3cfcd1a`
/ PR #3434 rebaselined the E4 retirement row after most old
`ExpressionProgram` bridge names were already deleted. The durable lesson is
that a finite retirement inventory still needs two kinds of source gate:
absence ratchets for deleted bridge names and a presence ratchet for the one
open bridge (`UnifiedBytecodeExpressionProgramExecutor.ExecuteDynamic(...)`).
Without the presence ratchet, a future rebaseline could make E4 look closed or
ambiguous while the dynamic expression bridge still exists; without the absence
ratchets, deleted runner/profiler/binding-target bridge names could return
silently. Keep runner-internal expression evaluation in E5 rather than using it
to reopen E4.

Issue
`planitem-planitem-planitem-gh3377-rebaseline-the-finite-bytecode-retirement-inven-1e1bc813d8`
converted the sync-function/binding-program standalone payload call sites. The
durable lesson is that external lowered binding-target callers can be moved off
runner construction only when they use the static lowered binding-target core
and route nested expression payloads through standalone unified bytecode; this
does not make runner-internal binding-target execution part of E4. Keep that
runner-owned path classified in E5 until the runner tier is retired, and do not
use a binding-target bridge cleanup to blur the E4 standalone executor boundary.

Issue
`planitem-planitem-planitem-gh3377-rebaseline-the-finite-bytecode-retirement-inven-7c09e17423`
/ PR #3437 rebaselined the E5 runner-retirement inventory after eligible
static-block bodies had gained a production bytecode route while
`ExecutionPlanRunner` entrypoints and fallback calls still remained. The durable
lesson is that admitted proof, open source-presence retirement anchors, and
terminal dynamic-residue exclusions must not share one manifest row. Keep
eligible static-block routing in an admitted child row, keep runner type,
script, sync, async-step entrypoints, and fallback construction in ordinary E5
children, and keep dynamic residue as an exclusion boundary instead of a
closure blocker for ordinary runner retirement.

Issue
`planitem-planitem-planitem-gh3377-rebaseline-the-finite-bytecode-retirement-inven-8e82fd27c3`
/ PR #3442 refined the same E5 split after the static-block fallback anchor was
still grouped with ordinary script fallback retirement. The durable lesson is
that a classified static-block `ExecutionPlanRunner.RunScript(...)` fallback
after production eligibility declines is explicit declined static-block residue,
not ordinary E5c script fallback retirement. Keep the manifest child owner and
classification text executable-tested so future checklist refreshes cannot hide
that static-block residue by merging it back into the script fallback owner.

PR #2729 completed full deletion of `EvaluateLegacyAstExpression` and its
sibling methods from `Ast/Legacy/ExpressionNodeExtensions.cs`. The build-stage
quality gate failed because the quarantine guard test
`RuntimeScan_FindsNoEvaluateExpressionCallersOutsideLegacyDefinition` used
`Assert.Single` to assert exactly one definition site remained; after deletion
the collection became empty and the assertion threw. The fix was to rename the
test to `RuntimeScan_EvaluateLegacyAstExpressionIsCompletelyRemoved` and change
`Assert.Single` to `Assert.Empty`, confirming total removal instead of quarantine.
The durable lesson: quarantine guard tests have a two-phase lifecycle — the
`Assert.Single` phase proves "isolated to its own definition", and the
`Assert.Empty` phase proves "tombstoned — no remaining reference". Treat the
phase transition as a mandatory companion step whenever the quarantined method
is deleted, not as a follow-up cleanup. Failing to update the guard causes the
build to break on what would otherwise be a clean deletion commit.
