# ADR 0373: Superseded attempt to retire async-function declined runner bridge with explicit rejection

## Status

Superseded.

Superseded on June 8, 2026 after the explicit-rejection contract broke
`make quality`: broad async-function and microtask-draining tests stayed pending
or observed empty results because route-ineligible async functions no longer
completed through the legacy runner. The current contract is to keep
`CreateClassifiedAsyncDeclinedBodyRunner(...)` and the async-function
`.ExecuteAsyncStep(...)` call site as classified open residue until the
resumable unified route owns those shapes semantically.

## Context

Issue
`planitem-gh3377-rebaseline-the-finite-bytecode-retirement-inventory-retire-fallba-db1bb485e8`
and delivery PR #3480 targeted the E5 async-function declined-body runner
bridge in `TypedAstEvaluator.AsyncFunctionInvoker`.

Before this slice, accepted async-function bodies tried resumable production
unified bytecode first, but route-ineligible bodies still constructed a
classified `ExecutionPlanRunner` bridge through
`CreateClassifiedAsyncDeclinedBodyRunner(...)` and continued through
`.ExecuteAsyncStep(...)`. Earlier E5 rebaseline work made that bridge auditable
by preserving the production decline code and reason, but it still left
async-function fallback behavior as live E5d runner residue.

The finite retirement inventory had to distinguish this from the still-open
ordinary sync function, class-constructor, and sync-generator runner owners.
Keeping a broad `ExecuteAsyncStep(` allowlist would hide whether the
async-function entry bridge had actually disappeared, because the runner
implementation method itself can remain for other owners.

## Superseded Decision

Retire the async-function declined-body runner bridge.

`AsyncFunctionInvoker` now:

- attempts `TryExecuteUnifiedBytecode(...)` first;
- resolves or rejects accepted resumable VM steps through the existing promise
  settlement path;
- rejects route-ineligible async-function bodies with an explicit message that
  includes the production decline code and reason;
- logged `async-function-unified-bytecode-declined-rejected` with the function
  name, decline code, and detail;
- does not construct `ExecutionPlanRunner`, call `.ExecuteAsyncStep(...)`, or
  keep `CreateClassifiedAsyncDeclinedBodyRunner(...)`.

The proof manifest must represent this as a tombstone:

- E5b keeps open allowlists for remaining runner owners, but the async-step
  entry row is now a `source-absence` proof scoped to
  `TypedAstEvaluator.AsyncFunctionInvoker.cs` and the async-function call-site
  token `.ExecuteAsyncStep(`.
- E5d keeps ordinary sync function, class-constructor, and sync-generator
  runner fallback owners open, but the async-function declined-body row is now
  `retired-fallback` source absence for
  `CreateClassifiedAsyncDeclinedBodyRunner`.

Future route widening should replace the classified async-function runner bridge
with public route-hit proof and nearby unsupported-route proof. It must not
replace fallback execution with rejection unless the affected async semantics
are already owned elsewhere.

## Consequences

- Route-ineligible async functions must complete through the classified legacy
  runner until their shapes route through resumable unified bytecode.
- The E5 inventory can retire the async-function bridge without falsely closing
  ordinary sync, class-constructor, or sync-generator runner owners.
- Source gates now keep the helper and async-function `ExecuteAsyncStep` call
  site visible as classified residue.
- Tests must assert fallback completion for current declined async bodies and
  separately assert absence of the resumable fast-path log.

## Evidence

- ADR allocation note: local `rtk faktorial-api adr-next` was unavailable in
  this runtime (`No such file or directory`), so this learn pass used the
  runtime allocator endpoint `POST /api/adrs/next`, which returned
  `{"adr_id":373}`. The prefix `0373` was checked free before writing.
- Delivery PR #3480 merged on current `origin/main`; local merge commit
  `5d47fb6c1` contains delivery commit
  `3f45e464d Retire async function declined runner bridge`. The carried
  build-stage summary records the original delivery commit as
  `4b1aee8ff Retire async function declined runner bridge`.
- Build-stage evidence recorded:
  - baseline async-function runner bridge source matches: 2
    (`CreateClassifiedAsyncDeclinedBodyRunner`, `.ExecuteAsyncStep(` in
    `AsyncFunctionInvoker`);
  - final async-function runner bridge source matches: 0 in
    `AsyncFunctionInvoker`;
  - `rtk jq empty docs/plans/bytecode-proof-manifest.json` passed;
  - `rtk git diff --check` passed;
  - focused async bridge/manifest proof pack passed;
  - `BytecodeProofManifestTests` passed with 226 tests;
  - `AsyncAwaitTests` passed with 27 tests.

## Related

- `docs/rules/expression-bytecode-ast-seams.md`
- `docs/rules/unified-bytecode-prototypes.md`
- `docs/plans/bytecode-proof-manifest.json`
- `src/Asynkron.JsEngine/Ast/TypedAstEvaluator.AsyncFunctionInvoker.cs`
- `tests/Asynkron.JsEngine.Tests/UnifiedBytecodeProductionInvocationTests.cs`
- `tests/Asynkron.JsEngine.Tests/BytecodeProofManifestTests.cs`
- ADR 0371:
  `docs/adrs/0371-keep-e5b-runner-entry-anchors-as-classified-allowlists.md`
