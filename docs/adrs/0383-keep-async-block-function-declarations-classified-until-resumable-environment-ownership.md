# ADR 0383: Keep async block function declarations classified until resumable environment ownership

## Status

Accepted

## Context

Faktorial issue
`planitem-planitem-gh3495-shared-context-e5b-runner-entry-point-tombstones-after-e-bd02e19f7d`
and delivery PR #3550 revisited the E5d async-function declined-body runner
bridge after ADR 0373 superseded the earlier explicit-rejection retirement
attempt.

The build stage first tried to admit descriptor-backed async block-scoped
function declarations to the resumable unified-bytecode route by adding
`DeclareFunction` support. Review caught that this was unsafe: a handler for
the declaration instruction did not prove that the resumable route owned the
materialized declaration environment, closure-visible block binding lifetime,
or Annex B blocked-name setup used by the existing IR runner.

The repair removed the unsafe admission and kept these bodies on
`CreateClassifiedAsyncDeclinedBodyRunner(...)`, preserving valid async
completion while the exact semantics remain outside the VM-owned resumable
surface.

## Decision

Descriptor-backed block-scoped function declarations in async-function bodies
must stay classified as declined-body runner residue until the resumable
unified-bytecode route owns the whole declaration environment contract.

Future admission must prove all of the following together:

- the block declaration environment is materialized and survives suspension at
  the same points as the IR runner;
- `DeclareFunction` writes the block slot and any closure-visible binding
  without relying on an AST or runner fallback;
- sloppy Annex B blocked names are installed on the relevant var environment
  before descriptor-backed block declarations execute;
- strict block function declarations do not leak outside the block; and
- parameter-blocked sloppy Annex B declarations do not update the enclosing or
  global binding.

A resumable `DeclareFunction` opcode or handler alone is not admission proof.
Until the environment and Annex B ownership is complete, eligibility must
decline before VM execution with a stable reason and accepted async bodies must
continue to prove the resumable fast-path separately from classified fallback
bodies.

## Consequences

- `CreateClassifiedAsyncDeclinedBodyRunner(...)` and the async-function
  `.ExecuteAsyncStep(...)` call site remain classified open residue for this
  family, not tombstones.
- The proof manifest should keep E5b/E5d async-function runner rows open until
  every reachable declined async-function body has exact resumable parity.
- Future widening should pair positive route-hit tests with adjacent
  no-route/fallback-completion tests for strict no-leak and Annex B
  blocked-name behavior.
- ADR 0373 remains the guard against replacing valid declined async-function
  completion with promise rejection as a shortcut.

## Evidence

- ADR allocation note: `rtk faktorial-api adr-next` was unavailable in this
  runtime (`No such file or directory`), so this learn pass used the runtime
  allocator endpoint `POST /api/adrs/next`, which returned `{"adr_id":383}`.
  The prefix `0383` was checked for duplicate use before writing.
- Delivery PR #3550 merged as squash commit
  `f042adfa175690f51bd6eee6a89b031c009c2d37`.
- Review-back repair commit before squash:
  `8892abda6 Keep async block declarations classified`.
- Implementation changed
  `src/Asynkron.JsEngine/Execution/UnifiedBytecode/UnifiedBytecodeProductionEligibility.cs`,
  `tests/Asynkron.JsEngine.Tests/AsyncAwaitTests.cs`, and
  `tests/Asynkron.JsEngine.Tests/UnifiedBytecodeProductionEligibilityTests.cs`.
- Build-stage evidence recorded:
  - focused async/eligibility pack passed with 5 tests;
  - `BytecodeProofManifestTests` passed with 234 tests;
  - `ExpressionProgramCoverageMapTests` passed with 15 tests;
  - `rtk git diff --check` passed; and
  - the runner AST-eval seam scan reported no matches.

## Related

- ADR 0337:
  `docs/adrs/0337-keep-annex-b-blocked-names-shared-for-unified-fast-activation.md`
- ADR 0373:
  `docs/adrs/0373-retire-async-function-declined-runner-bridge-with-explicit-rejection.md`
- `docs/rules/ecmascript-annex-b-block-functions.md`
- `docs/rules/unified-bytecode-prototypes.md`
- `src/Asynkron.JsEngine/Execution/UnifiedBytecode/UnifiedBytecodeProductionEligibility.cs`
- `tests/Asynkron.JsEngine.Tests/AsyncAwaitTests.cs`
- `tests/Asynkron.JsEngine.Tests/UnifiedBytecodeProductionEligibilityTests.cs`
