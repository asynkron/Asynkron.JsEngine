# ADR 0212: Keep typed module execution helpers JsValue-native

## Status

Accepted

## Context

Issue `autrun-dit4lwg5pkm0-72a23c5a9c` / PR #2260 continued the Unboxer
cleanup of private `object?` carriers in the core runtime.

ADR 0168 had already moved the private `ExecuteProgram` script/eval path to
`JsValue`, but explicitly left the module-body execution surface as a separate
migration target. Before this delivery, `JsEngine` still had private typed
statement/expression helpers that called `ProgramNode.EvaluateProgram(...)` and
returned `object?`. Several typed module and async-module callsites then
converted the result right back to `JsValue` with
`JsValue.FromObjectUnsafe(...)`.

That carrier was not a public facade, host interop, debugger, or diagnostic
boundary. It was private typed execution plumbing for module evaluation, where
the selected callsites already needed JavaScript values. The delivery kept the
remaining public or compatibility `object?` shape as an adapter at the edge,
while moving the core helper path to `EvaluateProgramJsValue(...)`.

The accepted delivery:

- added `ExecuteTypedStatementJsValue(...)` and
  `ExecuteTypedExpressionJsValue(...)`;
- made the existing `object?` helpers adapt from the typed result through a
  single legacy conversion helper;
- moved selected async module callsites off immediate
  `ExecuteTypedExpression(...)` plus `JsValue.FromObjectUnsafe(...)` rewraps;
  and
- marked `TypedAstEvaluator.EvaluateProgram(object?)` obsolete with
  `error: true` after the selected direct core usage was removed.

Focused evidence from the build and review stages included:

```text
baseline rg FromObjectUnsafe(_engine.ExecuteTypedExpression|program.EvaluateProgram( in JsEngine.cs = 9 matches
final    rg FromObjectUnsafe(_engine.ExecuteTypedExpression|program.EvaluateProgram( in JsEngine.cs = 8 matches
```

Both build and review reran:

```bash
rtk dotnet build src/Asynkron.JsEngine/Asynkron.JsEngine.csproj -v minimal
```

and review also confirmed that `program.EvaluateProgram(` no longer appears in
`JsEngine.cs`.

Follow-up issue `autrun-ditfw6gh2qag-7ade4a3977` / PR #2364 completed the
same typed-expression bridge cleanup in `JsEngine.cs`. The remaining private
`ExecuteTypedExpression(...)` `object?` adapter had no intentional public,
host-interop, debugger, or diagnostic role; it only fed module and async-module
callsites that already consumed `JsValue`. The follow-up migrated those
callsites to `ExecuteTypedExpressionJsValue(...)`, assigned default-export
expression bindings directly as `JsValue`, and deleted the private adapter.

Focused evidence from that follow-up:

```text
baseline rg "ExecuteTypedExpression\(" src/Asynkron.JsEngine/JsEngine.cs = 14 matches
final    rg "ExecuteTypedExpression\(" src/Asynkron.JsEngine/JsEngine.cs = 0 matches
```

The build stage also ran:

```bash
rtk dotnet test tests/Asynkron.JsEngine.Tests --filter "FullyQualifiedName~ModuleTests|FullyQualifiedName~AsyncModuleTryAwaitTests"
```

with 56 passing tests, and review reported the repo quality gate clean.

Follow-up issue `gh2372` / PR #2380 selected the previously deferred
module-result owner surface. `ModuleEntry.LastValue`, `ExecuteModuleBody(...)`,
and `AsyncModuleBodyRunner._lastValue` now store statement completion values as
`JsValue`, while public `object?` facades and `Task<object?>` completion edges
convert through `ConvertJsValueToLegacyObject(...)`.

Focused evidence from that follow-up:

```text
src/Asynkron.JsEngine/JsEngine.cs | 43 +++++++++++++++++++++++----------------
1 file changed, 26 insertions(+), 17 deletions(-)
```

The build stage ran:

```bash
rtk dotnet test tests/Asynkron.JsEngine.Tests/Asynkron.JsEngine.Tests.csproj -c Release --filter "FullyQualifiedName~ModuleTests|FullyQualifiedName~AsyncModuleTryAwaitTests"
```

with 56 passing tests. Review required the build handoff to state the ADR 0212
boundary explicitly: private typed module execution remains `JsValue`-native,
while public `object?` APIs stay adapter boundaries.

Follow-up issue `gh2374` / PR #2383 completed the adjacent module evaluation
task seam. `ModuleEntry.EvaluationTask`, async module body completion tasks,
dependency-drain task lists, and the selected module-body async helpers now
carry `JsValue` internally. Public or compatibility `object?` returns still
adapt at the edge through `ConvertJsValueToLegacyObject(...)`.

That follow-up also exposed a scheduling regression risk during review: after
the internal task storage became `Task<JsValue>`, a dependency walk briefly
discarded the task returned by `EnsureModuleEvaluatedAsync(...)` and then
conditionally awaited `dependency.EvaluationTask`. Synchronous dependencies do
not necessarily install a stored async evaluation task, so faults raised through
the returned task could be dropped. Commit `c365acc4` fixed the seam by
capturing the returned task and awaiting it for non-async dependencies, while
using stored `EvaluationTask` only for the pending async-dependency list.

Follow-up issue `autrun-ditjxyki91ew-7082b9173d` / PR #2403 removed the
remaining private `ExecuteTypedStatement(...)` `object?` adapter in
`JsEngine.cs`. The selected module and async-module statement callsites now call
`ExecuteTypedStatementJsValue(...)` directly. Public facade and edge-returning
module APIs still convert through `ConvertJsValueToLegacyObject(...)`.

Focused evidence from that follow-up:

```text
baseline rg "ExecuteTypedStatement\(" src/Asynkron.JsEngine/JsEngine.cs = 11 matches
final    rg "ExecuteTypedStatement\(" src/Asynkron.JsEngine/JsEngine.cs = 0 matches
```

The build stage ran:

```bash
rtk dotnet test tests/Asynkron.JsEngine.Tests --filter "FullyQualifiedName~ModuleTests|FullyQualifiedName~AsyncModuleTryAwaitTests"
```

with 56 passing tests.

Follow-up issue `gh2401` / PR #2406 hardened the dependency-walk completion
contract without changing the ADR 0212 value boundary. The normal async
dependency drain still uses stored `ModuleEntry.EvaluationTask`, but
`EvaluateModuleDependenciesAsync(...)` now falls back to awaiting the task
returned by `EnsureModuleEvaluatedAsync(...)` when an async dependency has not
published a stored task. That keeps faults observable instead of allowing a
dependency walk to continue with no observed completion surface.

Focused evidence from that follow-up:

```text
src/Asynkron.JsEngine/JsEngine.cs            |  5 +++
tests/Asynkron.JsEngine.Tests/ModuleTests.cs | 53 ++++++++++++++++++++++++++++
2 files changed, 58 insertions(+)
```

The build stage ran:

```bash
rtk dotnet test tests/Asynkron.JsEngine.Tests --filter "Name~TopLevelAwait_AsyncDependencyWalkSurfaces"
```

with both dependency-walk fault propagation tests passing.

Follow-up issue `agentmanual1780943196527007000` selected the module export
object storage seam. `JsObject` already stores property values as `JsValue`, but
`JsEngine` module export helpers still wrote live export bindings through the
`IDictionary<string, object?>` indexer and `ModuleNamespace` lookup recovered
those bindings through the object-shaped `TryGetValue(...)`/`ToObject()` path.
The follow-up moved module export writes to `SetJsValue(...)`, made namespace
lookup read with `TryGetJsValue(...)`, and kept `LiveExportBinding` wrapped once
at the helper boundary.

Focused evidence from that follow-up:

```text
final rg "exports\\[" src/Asynkron.JsEngine/JsEngine.cs = 0 matches
```

The build stage ran:

```bash
rtk dotnet test tests/Asynkron.JsEngine.Tests --filter "Name~ReExportedDefaultAliasReadsLiveBindingThroughNamespace|Name~SourceGate_ModuleExportStorage_StaysJsValueNative"
```

covering a re-exported default alias through module namespace lookup plus a
source gate for the selected storage path.

## Decision

Keep private typed module execution helpers `JsValue`-native.

For future typed module execution migrations:

1. route private typed statement/expression helpers through
   `ProgramNode.EvaluateProgramJsValue(...)`;
2. keep `object?` adapters only at public facade or explicitly deferred
   compatibility boundaries;
3. do not call a private `object?` typed execution helper and immediately rewrap
   the result with `JsValue.FromObjectUnsafe(...)`;
4. do not reintroduce private `ExecuteTypedStatement(...)` or
   `ExecuteTypedExpression(...)` `object?` adapters;
   `ExecuteTypedStatementJsValue(...)` and `ExecuteTypedExpressionJsValue(...)`
   are the core typed statement/expression entrypoints;
5. use `[Obsolete(..., true)]` on legacy private wrappers after the selected
   direct usage is removed, so hidden core callsites become compiler errors
   instead of new accidental object-carrier seams;
6. keep module `LastValue` storage, module-body completion storage, async
   module runner last-value storage, stored module evaluation tasks, and
   internal dependency-drain task lists `JsValue`-native; convert only at
   public `object?` facade or edge-returning `Task<object?>` adapter
   boundaries;
7. when a dependency walker calls `EnsureModuleEvaluatedAsync(...)`, preserve
   and await that returned task for synchronous dependencies instead of
   substituting `dependency.EvaluationTask`; stored evaluation tasks are for
   async module continuation tracking, not the only completion/fault surface,
   and an async dependency with no stored task must still await the returned
   task as the fault-propagation fallback; and
8. keep module export storage and namespace export lookup on the `JsValue`
   storage surface (`SetJsValue(...)`/`TryGetJsValue(...)`), wrapping
   `LiveExportBinding` only at the dedicated helper boundary; and
9. prove each slice with a before/after search for the selected legacy
   signatures plus focused module or async-module coverage when behavior, not
   just helper plumbing, changes.

## Consequences

- Typed module execution now follows the same value-primitive direction as the
  private script/eval `ExecuteProgram` path from ADR 0168.
- The selected `ExecuteTypedStatement(...)` and `ExecuteTypedExpression(...)`
  private object adapters are gone; a future reintroduction should be treated
  as a regression unless it is tied to a new explicit public, host interop,
  debugger, or diagnostic boundary.
- Module result storage is no longer a deferred `object?` owner surface:
  `ModuleEntry.LastValue`, synchronous module-body completion, and async module
  runner last-value storage now stay typed as `JsValue`.
- Stored module evaluation tasks and dependency-drain task lists are no longer
  deferred `Task<object?>` owner surfaces; they stay typed as `Task<JsValue>`
  until an edge adapter returns public or compatibility `object?`.
- Module export storage no longer uses the `JsObject` compatibility indexer for
  private export-binding values; namespace lookup consumes the stored `JsValue`
  directly and only unwraps `LiveExportBinding` through typed extraction.
- Module dependency evaluation must observe both completion surfaces: the
  immediate task returned by `EnsureModuleEvaluatedAsync(...)` for synchronous
  dependency faults or unexpected async-task publication gaps, and stored
  `EvaluationTask` for normal async dependency continuation/drain tracking.
- Future Unboxer slices should focus on other object-shaped module result
  surfaces without reopening the public `Evaluate*` facade shape.
- Obsolete error-level wrappers are useful as temporary compiler pressure, but
  should not become permanent compatibility APIs once internal callers are gone.

## Related

- `.claude/rules/ecmascript-modules.md`
- `.claude/rules/jsvalue-core-values.md`
- `docs/adrs/0168-keep-executeprogram-jsvalue-native.md`
- `docs/adrs/0182-keep-module-namespace-own-keys-jsvalue-native.md`
