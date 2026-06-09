# ADR 0381: Pin no-private ordinary declined-body replacement surface

## Status

Accepted.

## Context

Parent plan gh3560 and child issue
`planitem-gh3560-batch-1-pin-the-replacement-surface-add-a-focused-noprivateordina-294c3d48cf`
were created after an E5d tombstone attempt proved
`BytecodeProofManifestTests` could pass after removing the ordinary sync runner,
but `make quality` still failed. The broad source gate was not enough: no-private
ordinary sync functions can still need a replacement route after production
unified bytecode, the simple IR fast paths, and `SyncIrCallTrampoline` all
decline.

The child deliberately excluded private-name sync residue, class constructors,
async functions, generators, and async generators. The target surface was the
remaining no-private ordinary sync declined-body behavior that must keep working
until later slices replace it with an owned route.

## Decision

Keep `CreateClassifiedOrdinarySyncFunctionFallbackRunner(...)` as the live
ordinary sync fallback while no-private ordinary declined-body replacement
routes are still open, and pin the surface with a focused runtime proof pack.

The proof pack must cover representative no-private ordinary sync families that
previously reached the classified fallback:

- global or free identifier property-read payloads, including `Set`, `DataView`,
  and optional-chain reads;
- computed logical property writes;
- direct-eval FunctionCode declaration and activation preservation;
- simple `CreateArray` operand payloads;
- nested ordinary-call stack and return-flow stability.

For each family, tests should assert the observable result, require the
classified ordinary sync fallback log for the subject function, and forbid that
function's `unified-bytecode-production-fast-path` log. Later replacement-route
slices can update a case from fallback-required to route-required only when the
same family has executable route proof and nearby unsupported-route proof.

Do not infer safety from a manifest-only source tombstone. Removing or
tombstoning the ordinary sync fallback requires current-worktree runtime proof
that every family still covered by this replacement surface has either moved to
an owned route or remains intentionally classified as open residue.

## Consequences

- E5d ordinary sync fallback retirement stays incremental: one admitted family
  can move to route/no-fallback proof without pretending the whole no-private
  ordinary declined-body surface is retired.
- `NoPrivateOrdinaryDeclinedBodyProofPackTests` is the fast named guard for
  this surface. It complements `BytecodeProofManifestTests`; it does not replace
  manifest/source ownership checks.
- Future replacement-route work must localize expected log changes in the
  focused proof pack when it moves a family off the classified fallback.
- Private names, class constructors, async functions, sync generators, async
  generators, and terminal dynamic residue remain separate E5 owner surfaces.

## Evidence

- ADR allocation note: local `rtk faktorial-api adr-next` was unavailable in
  this runtime (`No such file or directory`), so this learn pass used the
  Faktorial HTTP allocator endpoint `POST /api/adrs/next`, which returned
  `{"adr_id":381}`. The prefix `0381` was checked for duplicate use after
  writing.
- Delivery PR #3562 merged on current local `origin/main` as commit
  `e43639830`.
- The merged delivery changed:
  - `tests/Asynkron.JsEngine.Tests/NoPrivateOrdinaryDeclinedBodyProofPackTests.cs`
- Build-stage commit recorded by the issue:
  - `4db9342b4 test: pin no-private ordinary fallback surface`
- Build-stage verification recorded:
  - `rtk dotnet test tests/Asynkron.JsEngine.Tests --filter FullyQualifiedName~NoPrivateOrdinaryDeclinedBodyProofPackTests`
    passed 5 tests.
  - `CreateClassifiedOrdinarySyncFunctionFallbackRunner(...)` remained present.

## Related

- `docs/rules/expression-bytecode-ast-seams.md`
- `src/Asynkron.JsEngine/Ast/TypedAstEvaluator.SyncFunctionInvoker.cs`
- `tests/Asynkron.JsEngine.Tests/NoPrivateOrdinaryDeclinedBodyProofPackTests.cs`
- `tests/Asynkron.JsEngine.Tests/BytecodeProofManifestTests.cs`
- ADR 0376:
  `docs/adrs/0376-keep-e5d-ordinary-sync-runner-allowlists-member-classified.md`
