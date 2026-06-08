# ADR 0377: Retire sync generator declined residue runner through creation-time IR route

## Status

Accepted.

## Context

Issue
`planitem-gh3495-shared-context-e5d-function-and-resumable-runner-retirement-retir-f6503ca61b`
and delivery PR #3521 targeted the remaining sync-generator E5d
declined-residue bridge.

Before the delivery, `SyncGeneratorInvoker` tried the production resumable VM
route first. When `UnifiedBytecodeProductionEligibility.EvaluateResumable(...)`
declined an otherwise simple generator body, invocation fell through to a
classified `ExecutionPlanRunner` bridge. That kept valid JavaScript semantics
for bodies whose existing IR route still worked, but it also left a post-
decline runner bridge inside the production-resumable invoker.

The narrow owner was sync generators only. Async-function declined bodies and
class-constructor fallback runners remained open E5d owners, and non-simple
sync-generator parameter lists still lacked resumable-owned
IteratorBindingInitialization.

The review repair found one bypass: module-level sync generator declarations
constructed `SyncGeneratorInvoker` directly, so they skipped the same
creation-time route selection that ordinary function/class/object generator
creation used.

## Decision

Retire the sync-generator declined-residue runner bridge. `SyncGeneratorInvoker`
now owns the production-resumable VM route only:

- admitted generator bodies return the unified-bytecode iterator;
- non-simple parameters and other unsupported invocation-shape pre-gates throw
  explicit unsupported-route errors;
- a production-resumable decline no longer creates a runner-backed residue
  bridge after `EvaluateResumable(...)`.

Preserve valid simple-parameter generator semantics by selecting the IR route
before invocation. Creation code calls `ShouldCreateIrSyncGeneratorInvoker(...)`
and constructs `IrSyncGeneratorInvoker` for simple-parameter generator bodies
whose existing lowered plan is not resumable-VM eligible. That callable shares
the generator intrinsic shape with `SyncGeneratorInvoker`, but it creates the
`ExecutionPlanRunner` as the plan-based generator implementation rather than as
a production-resumable fallback.

Apply the selector consistently to module declarations as well as ordinary
function, class, and object method creation. Module generator declarations must
not construct `SyncGeneratorInvoker` directly when the shared selector would
choose `IrSyncGeneratorInvoker`.

## Consequences

- E5d no longer has a sync-generator post-decline runner bridge. The proof
  manifest row is a source-absence tombstone for
  `CreateClassifiedSyncGeneratorDeclinedResidueRunner`.
- `E5-ir-runner-type-still-present` still allows the `ExecutionPlanRunner`
  construction in `IrSyncGeneratorInvoker`, but that construction is classified
  as creation-time IR route selection, not a retired declined-residue bridge.
- Source gates must reject stale bridge names and markers including
  `CreateClassifiedSyncGeneratorDeclinedResidueRunner`,
  `CreateSyncGeneratorDeclinedResidueRunner`,
  `classified-sync-generator-ir-fallback`,
  `classified-sync-generator-declined-residue`, and `isDeclinedResidue`.
- Future resumable-generator widening should admit more bodies into
  `SyncGeneratorInvoker` / `ExecuteResumable` or keep unsupported shapes
  explicit. It should not restore an invocation-time runner fallback to
  preserve semantics for declined simple bodies.

## Evidence

- ADR allocation note: local `rtk faktorial-api adr-next` was unavailable in
  this runtime (`No such file or directory`), so this learn pass used the
  Faktorial HTTP allocator endpoint `POST /api/adrs/next`, which returned
  `{"adr_id":377}`. The prefix `0377` was checked free before writing.
- Delivery PR #3521 merged on current local `origin/main` as commit
  `690eb9a3746a92255fc4eefb7f4c63c907c75b8f`.
- The merged delivery changed:
  - `src/Asynkron.JsEngine/Ast/TypedAstEvaluator.SyncGeneratorInvoker.cs`
  - `src/Asynkron.JsEngine/Ast/TypedAstEvaluator.IrSyncGeneratorInvoker.cs`
  - `src/Asynkron.JsEngine/Ast/TypedAstEvaluator.ModuleFunctionFactory.cs`
  - `src/Asynkron.JsEngine/Ast/TypedAstEvaluator.GeneratorFunctionBase.cs`
  - `docs/plans/bytecode-proof-manifest.json`
  - `docs/plans/bytecode-burndown-checklist.md`
  - `tests/Asynkron.JsEngine.Tests/UnifiedBytecodeProductionInvocationTests.cs`
  - `tests/Asynkron.JsEngine.Tests/ModuleTests.cs`
  - `tests/Asynkron.JsEngine.Tests/BytecodeProofManifestTests.cs`
- Build-stage commits recorded by the issue:
  - `4a29a078e Retire sync generator declined residue runner`
  - `01b626507 Fix IR generator method setup`
  - `c6147f9e0 Route module sync generator declines through IR`
- Focused build-stage verification recorded:
  - `rtk dotnet build src/Asynkron.JsEngine/Asynkron.JsEngine.csproj` passed.
  - `rtk git diff --check` passed.
  - `rtk dotnet test tests/Asynkron.JsEngine.Tests --filter "FullyQualifiedName~ModuleSyncGeneratorProductionResumableDecline_UsesCreationTimeIrRoute|FullyQualifiedName~SelfImportAnonymousDefaultGeneratorExport"` passed 2 tests.
  - `rtk dotnet test tests/Asynkron.JsEngine.Tests --filter "FullyQualifiedName~SyncGeneratorProductionResumableDecline_UsesCreationTimeIrRouteWithoutResidueRunner|FullyQualifiedName~SourceGate_SyncGeneratorInvoker_RetiresDeclinedResidueRunnerBridge"` passed 2 tests.
  - `rtk dotnet test tests/Asynkron.JsEngine.Tests --filter "FullyQualifiedName~ActivationSemanticsProofPackTests"` passed 51 tests.

## Related

- `docs/rules/generator-execution-path-parity.md`
- `docs/plans/bytecode-proof-manifest.json`
- `docs/plans/bytecode-burndown-checklist.md`
- `src/Asynkron.JsEngine/Ast/TypedAstEvaluator.IrSyncGeneratorInvoker.cs`
- `src/Asynkron.JsEngine/Ast/TypedAstEvaluator.SyncGeneratorInvoker.cs`
- `src/Asynkron.JsEngine/Ast/TypedAstEvaluator.ModuleFunctionFactory.cs`
- `tests/Asynkron.JsEngine.Tests/UnifiedBytecodeProductionInvocationTests.cs`
- `tests/Asynkron.JsEngine.Tests/ModuleTests.cs`
- ADR 0347:
  `docs/adrs/0347-keep-resumable-runner-construction-classified-by-route-boundary.md`
- ADR 0363:
  `docs/adrs/0363-retire-async-generator-runner-fallback-with-explicit-route-rejections.md`
