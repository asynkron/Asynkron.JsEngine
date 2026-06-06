# ADR 0357: Keep A51l catch/try/driver cleanup diagnostics decomposed

## Status

Accepted

## Context

Faktorial issue
`planitem-planmanual1780730299657353000-unified-bytecode-remaining-burndown-03-com-c499586fbd`
targeted A51l, the unified-bytecode compiler diagnostics bucket for
catch/try/driver cleanup topology diagnostics that were not otherwise captured
by concrete driver rows.

The delivery found that A51l was no longer a useful standalone owner. Current
compiler and eligibility diagnostics already have more precise homes:

- catch-binding storage and related environment-sensitive targets belong to
  A51c;
- invalid targets, active try-completion targets, and remaining loop-control
  topology belong to A51a;
- iterator, for-in, `yield*`, resume-target, driver state-slot, and
  iterator-close state-slot diagnostics belong to A51d;
- logical property-write cleanup-start target diagnostics belong to A51j;
- suspending or nested finally cleanup, captured/free mutation cleanup, and
  similar resumable cleanup guards remain explicit eligibility declines rather
  than compiler `TryCompile` reason templates.

Without decomposing the bucket, future compiler-diagnostic work could keep
moving unrelated catch, try, and driver strings back into one residual
"cleanup topology" umbrella and make the burndown non-finite again.

## Decision

Close A51l as a standalone owner leaf. Future catch/try/finally and
driver-cleanup diagnostics must be classified by the semantic owner that already
decides the fallback boundary.

- Do not reintroduce an A51l row or generic "not otherwise captured" bucket in
  the expansion contract or burndown checklist.
- Map catch-binding and dynamic environment storage diagnostics to A51c.
- Map invalid targets, active try-completion targets, and unsupported
  loop-control topology to A51a.
- Map iterator/driver state-slot and iterator-close state-slot diagnostics to
  A51d.
- Map cleanup-start diagnostics for logical property writes to A51j.
- Keep resumable finally-cleanup eligibility guards in their existing
  resumable cleanup owner rows until the VM owns those semantics.
- Protect the decomposition with a source-backed drift guard over non-empty
  compiler reason templates so new catch/try/driver cleanup strings must be
  classified deliberately.

This is a diagnostics and ownership decision, not a runtime route admission.
It does not make unowned try/finally, catch binding, iterator cleanup, or
resumable cleanup shapes execute through the production VM.

## Consequences

- The remaining A51 compiler diagnostics are finite owner leaves instead of a
  residual umbrella.
- Reviewers should ask "which existing owner owns this reason?" before adding a
  catch/try/driver cleanup diagnostic to the expansion contract.
- A new catch/try/driver cleanup compiler reason must update the source guard,
  expansion contract, and checklist owner mapping in the same slice.
- Runtime widening in this area still needs ordinary route-hit/no-route proof;
  the A51l closure only records that the old diagnostics bucket is no longer an
  owner.

## Evidence

- Delivery PR #3330 lifecycle was already handled by Faktorial before this
  learn pass.
- Local delivery branch commit `07bfcefc7`:
  `Replay A51l diagnostics closure on current base`.
- The delivery branch merge head `aaf4a667e` combined the A51l closure with the
  latest local `origin/main` at commit `378a68299`.
- The delivery added
  `UnifiedBytecodeCompiler_A51lTryCleanupUmbrellaStaysClosed` to
  `ExpressionProgramCoverageMapTests`, removed the A51l contract row, updated
  the burndown checklist/progress docs, and mapped catch/try/driver cleanup
  reason templates to concrete owners.
- Build-stage evidence recorded `rtk git diff --check` passing and
  `rtk dotnet test tests/Asynkron.JsEngine.Tests --filter "FullyQualifiedName~ExpressionProgramCoverageMapTests"`
  passing with 16 tests.
- Learn-stage ADR allocation note: local `rtk faktorial-api adr-next` was not
  present in this runtime, so this pass used the same runtime allocator API
  through `POST /api/adrs/next`, which returned `{"adr_id":357}`.

## Related

- `docs/rules/unified-bytecode-prototypes.md`
- `docs/unified-bytecode-expansion-contract.md`
- `docs/plans/bytecode-burndown-checklist.md`
- ADR 0271: `docs/adrs/0271-keep-unified-bytecode-exception-regions-vm-owned-and-driver-cleanup-topology-guarded.md`
- ADR 0341: `docs/adrs/0341-keep-with-depth-and-zero-depth-dynamic-name-scans-separate.md`
