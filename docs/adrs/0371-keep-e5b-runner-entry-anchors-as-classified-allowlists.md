# ADR 0371: Keep E5b runner entry anchors as classified allowlists

## Status

Accepted.

## Context

Issue
`planitem-planitem-planitem-gh3377-rebaseline-the-finite-bytecode-retirement-inven-aefaaf9189`
and delivery PR #3462 reconciled the remaining E5b
`ExecutionPlanRunner` proof anchors after earlier runner-retirement slices had
split concrete fallback ownership into E5c and E5d.

Before the delivery, the proof manifest treated broad type and method presence
as ordinary E5b source-presence anchors:

- `ExecutionPlanRunner` type presence;
- `RunScript` entrypoint presence;
- `RunSync` entrypoint presence;
- `ExecuteAsyncStep` entrypoint presence.

That framing had become misleading. The broad Core type and entry methods still
exist, but current retirement work needs to know where runner construction and
entry calls are still used. A type-level or method-declaration scan can stay
green while the remaining work has moved to classified owner rows such as
script/static-block fallback, ordinary sync fallback, async-function declined
body fallback, and sync-generator declined-body residue.

The build-stage quality repair exposed the same boundary from the test side.
Four stale sync-generator tests still expected route-ineligible bodies to
complete through legacy runner fallback behavior. The repair aligned them with
the current explicit pre-gate decline contract from
`docs/rules/generator-execution-path-parity.md`: non-simple parameters and root
hoist collection gaps that do not reach the production-resumable classifier
must fail explicitly rather than reuse a generic declined-body runner fallback.

## Decision

Keep the four E5b runner entry proof rows, but make them classified source
allowlists rather than broad source-presence anchors.

- `E5-ir-runner-type-still-present` now allowlists `new ExecutionPlanRunner(`
  construction to the files that own script runners and E5d
  function/resumable declined-body residue.
- `E5-ir-runner-script-entry-still-present` now allowlists
  `ExecutionPlanRunner.RunScript(` call sites to the E5c script/static-block
  fallback owners.
- `E5-ir-runner-sync-entry-still-present` now allowlists `runner.RunSync();`
  to the E5d ordinary sync fallback owner.
- `E5-ir-runner-async-step-entry-still-present` now allowlists
  `ExecuteAsyncStep(` to the async-function declined-body bridge and the runner
  implementation method.

Broad Core type or method declaration presence is no longer counted as a
generic hot-path blocker. Future retirement work should delete or narrow the
specific owner call sites and then convert the matching allowlist row to a
tombstone. It should not reintroduce a generic "runner entry still present"
proof row that passes only because `ExecutionPlanRunner.Core.cs` still declares
the type or shared method.

When the allowlist change touches generator fallback boundaries, update nearby
tests in the same slice so route-ineligible sync-generator shapes assert the
explicit decline reason instead of fallback success.

## Consequences

- The E5b inventory remains finite while showing the semantic owners of live
  runner use.
- Future agents can distinguish a still-live runner call site from a shared
  implementation method that exists only because other classified owner rows
  have not been tombstoned yet.
- Proof manifest tests now assert that E5 runner rows are `source-allowlist`
  rows with allowed paths and "not a broad E5b" classifications.
- Sync-generator test expectations stay aligned with the current explicit
  pre-gate decline boundary.

## Evidence

- ADR allocation note: `rtk faktorial-api adr-next` was unavailable in this
  runtime (`No such file or directory`), so this learn pass used the Faktorial
  HTTP allocator endpoint `POST /api/adrs/next`, which returned
  `{"adr_id":371}`.
- Delivery PR #3462 merged as commit
  `6136752b1e694af0230af7d206e008f0e3fcf119`.
- The delivery changed:
  - `docs/bytecode-progress.md`
  - `docs/plans/bytecode-burndown-checklist.md`
  - `docs/plans/bytecode-proof-manifest.json`
  - `tests/Asynkron.JsEngine.Tests/BytecodeProofManifestTests.cs`
  - `tests/Asynkron.JsEngine.Tests/ClassMethodDestructuringTests.cs`
  - `tests/Asynkron.JsEngine.Tests/Test262AsyncGeneratorDestructuringLayeredTests.cs`
  - `tests/Asynkron.JsEngine.Tests/UnifiedBytecodeResumableNestedFunctionTests.cs`
- Focused build-stage verification recorded:
  - baseline `make quality` failed with four stale sync-generator expectation
    tests;
  - the focused proof pack passed 225 tests after the repair;
  - `rtk git diff --check` passed.

## Related

- `docs/rules/expression-bytecode-ast-seams.md`
- `docs/rules/generator-execution-path-parity.md`
- `docs/plans/bytecode-proof-manifest.json`
- ADR 0347:
  `docs/adrs/0347-keep-resumable-runner-construction-classified-by-route-boundary.md`
- ADR 0363:
  `docs/adrs/0363-retire-async-generator-runner-fallback-with-explicit-route-rejections.md`
