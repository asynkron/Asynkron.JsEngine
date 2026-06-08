# ADR 0376: Keep E5d ordinary sync runner allowlists member-classified

## Status

Accepted.

## Context

Issue
`planitem-gh3495-shared-context-e5d-function-and-resumable-runner-retirement-retir-4a3da72159`
and delivery PR #3520 targeted one safe E5d ordinary sync declined-body family:
stop that family from reaching the ordinary sync fallback runner after
production unified bytecode can route it.

Current `origin/main` had already admitted the selected zero-depth dynamic
identifier delete shape. The delivery therefore narrowed to proof and
classification:

- add a classified ordinary sync fallback log at the remaining slow-path runner
  construction;
- add runtime proof that `deleteImplicitGlobal` routes through production
  unified bytecode and does not log the classified fallback;
- add the proof-manifest row for the retired family while keeping E5d open for
  remaining ordinary sync, class-constructor, async-function, sync-generator,
  and terminal dynamic residue owners.

The build-stage repair found that the source allowlist could still claim the
wrong owner for the live `runner.RunSync();` call. The remaining ordinary sync
runner execution is in `InvokeWithContextSlow(...)`, after
`TryInvokeIrFast(...)` has declined. It is not a `TryInvokeIrFast(...)` runner
call, and `TryInvokeIrFast(...)` must stay runner-free.

## Decision

Keep E5d ordinary sync runner allowlists classified by the enclosing member
declaration and by the production-decline boundary that reaches the call.

- A retired ordinary sync family needs executable route/no-fallback proof:
  require the relevant `unified-bytecode-production-fast-path` log and forbid
  the classified ordinary sync fallback log for that exact function.
- The remaining ordinary sync `runner.RunSync();` proof stays open while it is
  the outer slow-path fallback in `InvokeWithContextSlow(...)` after
  `TryInvokeIrFast(...)` declines.
- Source-gate tests must map matched call sites to the nearest expected member
  declaration before falling back to local token scans. File-level allowlists or
  nearest-token matches are not enough for E5 runner ownership.
- Retiring one ordinary sync family must not hard-tombstone the class
  constructor, async-function, sync-generator, or E5e terminal dynamic residue
  rows. Those owners remain separately classified until their own route proof
  changes.

## Consequences

- Future E5d work can retire one ordinary sync family without making the
  manifest look as if all ordinary sync runner fallback disappeared.
- `TryInvokeIrFast(...)` remains a source-gated runner-free accepted-route
  section, while the outer slow-path fallback remains auditable.
- `BytecodeProofManifestTests` must keep classified call-site checks strict
  enough to fail when a matched source call is attributed to the wrong member.
- Partial route proof should update the retired-family runtime row and the
  remaining fallback classification together.

## Evidence

- ADR allocation note: `rtk faktorial-api adr-next` was unavailable in this
  runtime (`No such file or directory`), so this learn pass used the Faktorial
  HTTP allocator endpoint `POST /api/adrs/next`, which returned
  `{"adr_id":376}`. The prefix `0376` was checked free before writing.
- Delivery PR #3520 merged on current local `origin/main` as commit
  `cace94014`.
- The merged delivery changed:
  - `docs/plans/bytecode-proof-manifest.json`
  - `src/Asynkron.JsEngine/Ast/TypedAstEvaluator.SyncFunctionInvoker.cs`
  - `tests/Asynkron.JsEngine.Tests/BytecodeProofManifestTests.cs`
  - `tests/Asynkron.JsEngine.Tests/UnifiedBytecodeProductionInvocationTests.cs`
- Build-stage commits recorded by the issue:
  - `20ecbfc1a Retire dynamic delete from ordinary sync fallback proof`
  - `bd13b1cb3 Fix E5d sync runner manifest metadata`
- Build-stage verification recorded a focused proof pack passing 227 tests,
  `rtk git diff --check` passing, and the runner AST seam scan for
  `EvaluateExpression(` / `ProfileEvaluateExpression(` returning no matches.

## Related

- `docs/rules/expression-bytecode-ast-seams.md`
- `docs/plans/bytecode-proof-manifest.json`
- `src/Asynkron.JsEngine/Ast/TypedAstEvaluator.SyncFunctionInvoker.cs`
- `tests/Asynkron.JsEngine.Tests/BytecodeProofManifestTests.cs`
- `tests/Asynkron.JsEngine.Tests/UnifiedBytecodeProductionInvocationTests.cs`
- ADR 0371:
  `docs/adrs/0371-keep-e5b-runner-entry-anchors-as-classified-allowlists.md`
