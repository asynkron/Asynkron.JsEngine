# ADR 0342: Keep unified bytecode finally-completion resource disposal owned

## Status

Accepted

## Context

Faktorial issue
`planitem-planmanual1780639098493226000-full-unified-bytecode-execution-burndown-b-935f087566`
and delivery PR #3254 closed the A42 sync `using` declaration admission lane for
production unified bytecode.

The admitted route compiled sync `using` declarations into
`RegisterDisposable` and registered resources against the active function or
block environment. Direct VM completion paths already disposed active
function-environment resources on ordinary return and throw, and block
`PopEnvironment` disposed nested block resources. That was not enough for a
function-body `using` declaration whose return or throw first passed through a
`try/finally` region.

The repair-stage regression showed the gap: `CompleteFinally` completed a saved
pending return or throw after the `finally` body ran, but it skipped the same
function-environment disposal hook used by direct `Return` and `Throw`.
Function-body top-level lexical declarations have no enclosing block
`PopEnvironment`, so a `using` resource in that environment can only be
released by the function-completion cleanup path.

## Decision

Keep sync `using` resource cleanup owned by the unified-bytecode VM completion
lanes.

- A pending return completed by `CompleteFinally` must run active driver cleanup
  and then dispose active function-environment resources before returning the
  pending value.
- A pending throw completed by `CompleteFinally` must set the pending throw,
  clean active driver states, and dispose active function-environment resources
  before leaving the VM.
- Disposal failure handling stays shared with the direct completion paths:
  disposal may replace or wrap the pending completion according to existing
  explicit-resource-management semantics; the repair must not bypass that by
  returning the original value directly.
- Do not rely on `PopEnvironment` to clean function-body `using` declarations.
  Function-body lexical declarations are registered against the function
  environment, while `PopEnvironment` only covers block/catch/with environments
  that were pushed by the emitted program.
- Do not repair finally-through-using gaps by falling back to
  `ExecutionPlanRunner`, `ExpressionProgram`, or AST evaluation. The accepted
  sync production route remains VM-owned, and `await using` stays declined until
  async-dispose promise settlement is VM-owned.

## Consequences

- Future unified-bytecode exception-region changes must compare every terminal
  completion lane against the direct completion lanes: fall-through, direct
  return, direct throw, pending return through finally, pending throw through
  finally, and block cleanup should not drift apart.
- Tests that admit resource-management shapes need route assertions for return
  and throw through `finally`, not only direct return/throw and nested block
  disposal.
- A failed resource-disposal ordering case in an accepted production route is a
  VM completion-cleanup bug until proven otherwise, not a reason to widen
  fallback execution.

## Evidence

- ADR allocation note: the local `rtk faktorial-api adr-next` wrapper was not
  available in this runtime (`No such file or directory`), so this learn pass
  used the host HTTP allocator endpoint `POST /api/adrs/next`, which returned
  `{"adr_id":342}`.
- Delivery PR #3254 merged; repair commit
  `50a9a8513902b64e2e5de19c4ec653a725918611` added the `CompleteFinally`
  cleanup calls.
- The repair changed:
  - `src/Asynkron.JsEngine/Execution/UnifiedBytecode/UnifiedBytecodeVirtualMachine.cs`
  - `tests/Asynkron.JsEngine.Tests/UsingInFunctionDisposeReproTests.cs`
- Build-stage proof recorded:
  - `rtk dotnet test tests/Asynkron.JsEngine.Tests --filter "FullyQualifiedName~UsingInFunctionDisposeReproTests"` passed 15 tests with existing nullable warnings.
  - `rtk dotnet test tests/Asynkron.JsEngine.Tests --filter "FullyQualifiedName~Finally"` passed 172 tests with 0 warnings.
  - `rtk git diff --check` passed.

## Related

- `docs/rules/unified-bytecode-prototypes.md`
- `docs/bytecode-progress.md`
- `docs/plans/bytecode-burndown-checklist.md`
- ADR 0204: `docs/adrs/0204-keep-unified-bytecode-sync-production-routing-slot-bridged.md`
- ADR 0258: `docs/adrs/0258-keep-unified-bytecode-completed-lanes-integrated-at-production-boundary.md`
- ADR 0271: `docs/adrs/0271-keep-unified-bytecode-exception-regions-vm-owned-and-driver-cleanup-topology-guarded.md`
- `src/Asynkron.JsEngine/Execution/UnifiedBytecode/UnifiedBytecodeVirtualMachine.cs`
- `tests/Asynkron.JsEngine.Tests/UsingInFunctionDisposeReproTests.cs`
