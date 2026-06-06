# ADR 0363: Retire async-generator runner fallback with explicit route rejections

## Status

Accepted.

## Context

ADR 0349 kept route-ineligible async-generator bodies on the classified
`ExecutionPlanRunner` fallback after an earlier fallback-retirement attempt
made valid async generators fail during verification. That was the right
boundary while the resumable VM had not yet admitted the delegated
async-generator settlement lanes, captured helper route, declaration-free direct
eval, and the cleanup/control-flow cases needed by the broad async-generator
proof pack.

Issue
`planitem-planmanual1780730299657353000-unified-bytecode-remaining-burndown-05-fal-ca5d6207cf`
and delivery PR #3362 changed that boundary. The accepted async-generator route
now covers direct yield, non-awaited `yield*`, awaited-source `yield* await`,
delegated `return`/`throw`, pending-await settlement, try/finally cleanup,
captured hoisted helpers, and declaration-free direct eval. After that widening,
the remaining async-generator runner fallback became an E6 retirement target
instead of a required semantic bridge.

The final quality-gate repair was not the fallback itself; it was the test
contract. Broad async-generator tests that previously expected unsupported
route neighbors to settle through `ExecutionPlanRunner` had to assert explicit
unsupported-route rejection for shapes the VM still does not own, especially
non-simple parameter lists and destructuring forms whose
IteratorBindingInitialization effects are not resumable-invocation owned.

## Decision

`AsyncGeneratorInvoker` no longer constructs a declined-body
`ExecutionPlanRunner` fallback. When `EvaluateResumable` declines an
async-generator body, initialization throws an explicit unsupported-route error
that includes the decline reason.

Accepted async-generator bodies remain VM-owned through
`UnifiedBytecodeVirtualMachine.ExecuteResumable` and the existing
async-generator promise settlement contract. Unsupported async-generator shapes
must stay explicit rejections until the VM owns their semantics; they must not
be silently routed to a runner fallback or to a VM-side evaluator fallback.

Future async-generator widening must update both sides of the proof boundary in
the same slice:

- route-hit and settlement tests for newly admitted async-generator bodies;
- explicit unsupported-route rejection tests for adjacent shapes still outside
  resumable invocation;
- source gates proving `AsyncGeneratorInvoker` no longer has runner fallback
  construction while accepted steps stay off `ExecutionPlanRunner`,
  `ExpressionProgram`, and AST/expression-evaluation bridges.

## Consequences

- ADR 0349 is superseded. Declined async-generator bodies are no longer expected
  to settle through `CreateClassifiedAsyncGeneratorDeclinedBodyRunner`.
- Non-simple async-generator parameter lists are observable unsupported-route
  rejections until resumable invocation owns IteratorBindingInitialization.
- Test suites that cover broad async-generator runtime behavior must distinguish
  admitted route behavior from intentionally unsupported route-neighbor
  behavior instead of assuming legacy runner fallback settlement.
- E6 is closed in `docs/plans/bytecode-burndown-checklist.md`, while remaining
  async-generator widening work belongs to the owning semantic rows rather than
  the retired fallback tier.

## Evidence

- Delivery PR #3362 merged as commit
  `45f45af33e10aa7414dd23a057475faf2a1b63a4`.
- Build-stage repair commit `e3c525ea7` aligned broad async-generator tests
  with the retired fallback contract.
- Focused repair verification recorded 32 broad async-generator related tests
  passed, 14 route/source-gate tests passed, and `rtk git diff --check` passed.
- The quality-gate fallout was 12 async-generator related failures before the
  repair and zero focused failures afterward.

## Related

- `docs/rules/unified-bytecode-prototypes.md`
- ADR 0321:
  `docs/adrs/0321-admit-simple-async-generator-resumable-route.md`
- ADR 0349:
  `docs/adrs/0349-keep-declined-async-generator-bodies-on-classified-runner-fallback.md`
