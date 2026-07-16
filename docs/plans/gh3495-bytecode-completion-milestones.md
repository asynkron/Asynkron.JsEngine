# gh3495 Bytecode Completion Milestones

Date: 2026-07-16

Issue gh3495 is the parent coordination issue for final bytecode-only execution.
It must not implement or tombstone an individual bytecode row directly. The
owned implementation work is split into independently shippable child milestones
that update the proof manifest, checklist, focused tests, and runtime/docs
surfaces together.

The source-of-truth status remains:

- `docs/plans/bytecode-burndown-checklist.md`
- `docs/plans/bytecode-proof-manifest.json`
- `tests/Asynkron.JsEngine.Tests/BytecodeProofManifestTests.cs`
- the focused test class named by each child milestone

This document is a routing contract. It does not close any manifest row and it
does not replace executable proof rows.

## Parent Rules

- Do not implement E5, B24h, or B36 runtime behavior directly under gh3495.
- Do not add proof commands that do not already execute in this repository.
- Reuse sibling task ownership instead of duplicating row work in this parent.
- Keep terminal dynamic residue separate from ordinary runner retirement.
- A child may close a row only by updating the manifest, checklist, focused
  tests, and source gates together.
- gh3495 can close only after the child milestones have merged and the final
  E4/E5 source-absence and route-coverage proof set passes.

## Milestone Routing

| Milestone | Owner | Scope | Required existing proof commands |
|---|---|---|---|
| E5 async-function route parity | gh3491 | Reduce async-function declined-body runner reliance one exact semantic family at a time. Keep `CreateClassifiedAsyncDeclinedBodyRunner(...)` classified until ADR 0373/0383 ownership is fully proven. | `rtk dotnet test tests/Asynkron.JsEngine.Tests --filter "FullyQualifiedName~AsyncAwaitTests"`; `rtk dotnet test tests/Asynkron.JsEngine.Tests --filter "FullyQualifiedName~UnifiedBytecodeProductionEligibilityTests"`; `rtk dotnet test tests/Asynkron.JsEngine.Tests --filter "FullyQualifiedName~BytecodeProofManifestTests"` |
| E5c ordinary script fallback retirement | new E5c child before implementation | Retire one ordinary non-terminal script fallback family only after route/no-route proof separates admitted scripts, ordinary wrapper-script fallback, terminal direct-eval residue, and static-block residue. | `rtk dotnet test tests/Asynkron.JsEngine.Tests --filter "FullyQualifiedName~UnifiedBytecodeProductionInvocationTests"`; `rtk dotnet test tests/Asynkron.JsEngine.Tests --filter "FullyQualifiedName~ExecutionPlanDiagnosticsTests"`; `rtk dotnet test tests/Asynkron.JsEngine.Tests --filter "FullyQualifiedName~BytecodeNonResidueDeclineRatchetTests"`; `rtk dotnet test tests/Asynkron.JsEngine.Tests --filter "FullyQualifiedName~BytecodeProofManifestTests"` |
| B24h class-expression environment bridge | gh3490, then follow-up B24h children for remaining anchors | Keep B24h class-expression work separate from B36 class declarations and E5 runner retirement. Replace one remaining B24h open anchor at a time with focused route or no-route proof. | `rtk dotnet test tests/Asynkron.JsEngine.Tests --filter "FullyQualifiedName~UnifiedBytecodeResumableClassExpressionTests"`; `rtk dotnet test tests/Asynkron.JsEngine.Tests --filter "FullyQualifiedName~BytecodeProofManifestTests"` |
| B36 class/static-block declarations | gh3624 and gh3625 | Keep B36 declaration/static-block residue source-owned. gh3624 owns static-block fallback classification; gh3625 owns the exact deferred class-definition environment proof boundary. | `rtk dotnet test tests/Asynkron.JsEngine.Tests --filter "FullyQualifiedName~UnifiedBytecodeResumableClassDeclarationTests"`; `rtk dotnet test tests/Asynkron.JsEngine.Tests --filter "FullyQualifiedName~UnifiedBytecodeProductionEligibilityTests"`; `rtk dotnet test tests/Asynkron.JsEngine.Tests --filter "FullyQualifiedName~BytecodeProofManifestTests"` |
| Final gh3495 closure | gh3495 after children merge | Convert the parent from planning to final bytecode-only closure only after all ordinary non-terminal child rows are admitted, retired, or hard-quarantined by manifest proof. | `rtk dotnet test tests/Asynkron.JsEngine.Tests --filter "FullyQualifiedName~BytecodeProofManifestTests"`; `rtk dotnet test tests/Asynkron.JsEngine.Tests --filter "FullyQualifiedName~ExecutionPlanDiagnosticsTests"`; `rtk dotnet test tests/Asynkron.JsEngine.Tests --filter "FullyQualifiedName~UnifiedBytecodeProductionInvocationTests"`; `rtk dotnet test tests/Asynkron.JsEngine.Tests --filter "FullyQualifiedName~BytecodeNonResidueDeclineRatchetTests"`; `rtk dotnet test tests/Asynkron.JsEngine.Tests --filter "FullyQualifiedName~AsyncAwaitTests"`; `rtk dotnet test tests/Asynkron.JsEngine.Tests --filter "FullyQualifiedName~ProductionRouteCoverageRatchetTests"` |

## Child Acceptance Contract

Every child milestone must report:

- the exact manifest proof id or ids it changes;
- the owning source file and focused test class;
- route-hit evidence for newly admitted shapes;
- no-route or source-absence evidence for neighboring residue;
- explicit terminal-dynamic-residue classification when applicable;
- `run-quality` verification requested after commit.

When a row is still open, the child must keep its proof row executable or keep a
source-presence/source-absence gate that names the exact owner. Broad prose-only
anchors are not enough for final gh3495 closure.
