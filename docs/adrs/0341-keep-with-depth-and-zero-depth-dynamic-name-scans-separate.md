# ADR 0341: Keep with-depth and zero-depth dynamic-name scans separate

## Status

Accepted

## Context

Faktorial issue
`planitem-planmanual1780639098493226000-full-unified-bytecode-execution-burndown-b-f2e74379de`
and delivery PR #3253 closed A45 in the unified-bytecode burndown checklist.
The original gap was that `UnifiedBytecodeWithDepthAnalysis` followed normal
control-flow successors but did not propagate active `with` depth into
`EnterTryInstruction` catch and finally entries. That left try/catch/finally
regions under an active `with` misclassified, even though the unified compiler
and VM already own ordinary exception-region opcodes.

The delivery also hit a quality-gate regression while repairing a nearby
finally-return free-callee shape. A first attempt treated zero-depth
catch/finally entries as part of the ordinary with-depth/plan-shape scan. That
over-widened the analysis surface and disturbed unrelated routing proofs:

- TDZ closure assignment before initialization;
- per-iteration const routing;
- sync for-loop simple let binding isolation.

The accepted repair split the two meanings of "visit exception regions":

- the main with-depth/plan-shape traversal follows catch/finally entries only
  when an active `with` depth must be propagated;
- the ordinary dynamic-name admission scan runs a separate optional
  zero-depth exception-region pass, and only uses that pass to find free call
  targets that enable the existing dynamic-name path.

That split admitted sloppy `finally { return helper(); }` free callees and
active-with try/catch/finally bodies without turning catch-only free reads into
a broad dynamic-name admission signal.

## Decision

Keep `UnifiedBytecodeWithDepthAnalysis` responsible for active `with` depth,
not for every possible exception-region reachability question.

- The default active-with traversal must preserve the active-with boundary:
  push `EnterTryInstruction` handler/finally entries when successor depth is
  greater than zero, and keep invalid targets, negative depth, and
  inconsistent-depth joins as hard failures.
- Zero-depth exception-region traversal is allowed only as an explicit
  opt-in for callers that need a separate reachability question. It must not
  silently become the default plan-shape scan.
- Ordinary dynamic-name admission may use the zero-depth exception-region pass
  only for free call targets. Do not use catch/finally free reads, stores,
  catch binding access, lexical dynamic declarations, or TDZ-head storage as
  evidence that the whole body can take the ordinary dynamic-name production
  route.
- Future widening must include positive route proof for the admitted
  exception-region shape and nearby no-route or regression proof for the
  dynamic-name families still owned by A51c and related scope/environment
  lowering rows.

## Consequences

- `with` inside try/catch/finally and try/catch/finally inside `with` can be
  reasoned about through one active-depth analysis without adding a second CFG
  recognizer.
- Finally-region free call targets can enable the same ordinary dynamic-name
  path as other free call targets, but zero-depth catch/finally free reads do
  not over-admit dynamic lookup.
- The A45 checklist row is closed, while A51c remains the owner for catch
  binding, lexical dynamic declaration, active-with dynamic-name, and TDZ-head
  binding storage gaps.
- Reviewers should treat failures in this area as classification bugs in
  reachability ownership before changing VM execution or adding runner
  fallback.

## Evidence

- Delivery PR #3253 merged as commit `a42a44ccc`.
- The delivery changed:
  - `src/Asynkron.JsEngine/Execution/UnifiedBytecode/UnifiedBytecodeWithDepthAnalysis.cs`
  - `src/Asynkron.JsEngine/Execution/UnifiedBytecode/UnifiedBytecodeProductionEligibility.cs`
  - `tests/Asynkron.JsEngine.Tests/FinallyReturnCallAdmissionTests.cs`
  - `tests/Asynkron.JsEngine.Tests/UnifiedBytecodeProductionEligibilityTests.cs`
  - `docs/plans/bytecode-burndown-checklist.md`
- Build-stage repair commit `a541a92f7` narrowed the zero-depth pass after the
  canonical quality gate reported three internal failures:
  `TdzClosureTest.ClosureTdz_AssignBeforeInit_ShouldThrowReferenceError`,
  `A44PerIterationLetDeclineTests.PerIterConst_OfMultiCaptured_RoutesThroughProduction`,
  and
  `IrLoopEnvironmentTests.SyncForLoop_NonCapturingSimpleLet_DoesNotLeakLoopBinding`.
- Final build-stage proof recorded a passing targeted 5-test filter covering
  the three failed tests plus the A45 route and eligibility proofs.
- Additional build-stage checks recorded:
  - `rtk git diff --check`
  - AST seam scan for `EvaluateExpression\(|ProfileEvaluateExpression\(`
    with no matches.

## Related

- `docs/rules/unified-bytecode-prototypes.md`
- `docs/plans/bytecode-burndown-checklist.md`
- ADR 0201: `docs/adrs/0201-keep-unified-bytecode-production-routing-decline-first.md`
- ADR 0204: `docs/adrs/0204-keep-unified-bytecode-sync-production-routing-slot-bridged.md`
- ADR 0269: `docs/adrs/0269-keep-with-backed-unified-bytecode-dynamic-names-activation-hoist-and-receiver-owned.md`
- ADR 0271: `docs/adrs/0271-keep-unified-bytecode-exception-regions-vm-owned-and-driver-cleanup-topology-guarded.md`
