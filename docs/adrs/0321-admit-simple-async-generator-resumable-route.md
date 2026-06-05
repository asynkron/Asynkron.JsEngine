# ADR 0321: Admit simple async-generator resumable route

## Status

Accepted

Supersession note: Faktorial issue
`planitem-planmanual1780639098493226000-full-unified-bytecode-execution-burndown-b-1747a4b32a`
and PR #3221 later widened this route to admit non-awaited async-generator
`yield*` over delegated async iterables after the VM owned delegated
`next`/`return`/`throw` settlement through the existing async-generator
`PendingAwait` bridge. The `yield* await ...` awaited delegated-source shape
still stays on the IR runner until that source-await settlement is VM-owned.

## Context

Faktorial issue #3135 and PR #3142 burned down the largest remaining
resumable-route gap: `async function*` bodies previously constructed the
`ExecutionPlanRunner` unconditionally and never tried
`UnifiedBytecodeVirtualMachine.ExecuteResumable`.

That left even simple direct-yield async generators on the IR runner after the
same resumable VM already owned many generator and async body shapes between
suspension points. The risk in widening this path was not the ordinary
`yield`/`await` step model; it was async-generator activation and settlement:
the async iterator object is produced before eager effects from non-simple
parameter lists, and delegated async-generator `yield*` settlement has different
driver and promise behavior than a direct `yield`.

The delivery kept those boundaries explicit. `AsyncGeneratorInvoker` now tries
the resumable VM only for simple-parameter async generators whose lowered plan
passes resumable production eligibility. Unsupported bodies still construct and
use the existing `ExecutionPlanRunner.ExecuteAsyncStep` bridge.

## Decision

Admit the first production async-generator route through the resumable unified
bytecode VM.

- `AsyncGeneratorInvoker` may initialize a `UnifiedBytecodeResumeState` for
  simple-parameter `async function*` bodies that pass
  `UnifiedBytecodeProductionEligibility.EvaluateResumable`.
- Accepted async-generator steps execute through
  `UnifiedBytecodeVirtualMachine.ExecuteResumable` and map `Yield`,
  `Completed`, `Throw`, and `PendingAwait` back into the existing
  async-generator promise settlement path.
- Non-simple parameter lists stay on the IR runner until the VM owns the eager
  parameter-initialization effects required before iterator creation.
- Non-awaited async-generator `yield*` may route after delegated async iterator
  settlement is VM-owned by the resumable VM. Async-generator
  `yield* await ...` stays an explicit pre-VM decline until awaited
  delegated-source settlement is VM-owned.
- The VM route must remain fallback-free for accepted programs: no callback
  into `ExecutionPlanRunner`, `ExpressionProgram`, or AST evaluation after the
  route is selected.

## Consequences

- Simple direct-yield async generators can now avoid constructing the IR runner
  when they are otherwise resumable-eligible.
- The remaining async-generator fallback is narrower and named: awaited
  delegated sources, non-simple parameters, and any body shape that the
  resumable VM still does not own.
- Future widening has a clear proof shape: selector/eligibility acceptance,
  public async iterator route logging, promise settlement parity, and adjacent
  no-route coverage for delegation or activation shapes still outside the VM.
- Documentation and roadmap status should describe this as the first narrow
  async-generator route, not as broad async-generator bytecode support.

## Evidence

- Delivery PR #3142 merged as commit
  `4143cedb928ff95637079ab4e5d8e250263d1fc9`.
- Build-stage verification recorded:
  - focused async-generator route pack passed with 5 tests
  - combined new proof pack plus
    `UnifiedBytecodeProductionEligibilityTests` passed 538 tests
  - delegated async-generator yield-star runtime pins passed 2 tests
  - `rtk git diff --check` passed
- The delivery updated:
  - `src/Asynkron.JsEngine/Ast/TypedAstEvaluator.AsyncGeneratorInvoker.cs`
  - `src/Asynkron.JsEngine/Execution/UnifiedBytecode/UnifiedBytecodeProductionEligibility.cs`
  - `tests/Asynkron.JsEngine.Tests/UnifiedBytecodeAsyncGeneratorRouteTests.cs`
  - `tests/Asynkron.JsEngine.Tests/UnifiedBytecodeProductionEligibilityTests.cs`
  - `docs/unified-bytecode-expansion-contract.md`
  - `docs/bytecode-progress.md`
  - `docs/plans/bytecode-burndown-checklist.md`
- PR #3221 / commit `4961c4e71131d0d3290db97c577ba8e85314fa04` later
  admitted the non-awaited async-generator `yield*` lane, added route/runtime
  proof for delegated async iterator `next`/`return`/`throw` settlement, and
  kept `yield* await ...` as a focused pre-VM decline.

## Related

- `docs/unified-bytecode-expansion-contract.md`
- `docs/rules/unified-bytecode-prototypes.md`
- ADR 0320:
  `docs/adrs/0320-keep-unified-bytecode-route-hit-evidence-explicit.md`
