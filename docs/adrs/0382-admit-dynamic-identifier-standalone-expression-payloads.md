# ADR 0382: Admit dynamic identifier standalone expression payloads

## Status

Accepted.

## Context

Parent plan gh3560 and child issue
`planitem-gh3560-batch-1-pin-the-replacement-surface-batch-2-dynamic-identifier-ex-44cec02efe`
targeted the Batch 2 dynamic identifier expression payload slice after ADR
0381 had pinned the no-private ordinary declined-body replacement surface.

`LoweredExpressionProgramCache.ExecuteCached(...)` already routes lowered
value-only expression operands through
`UnifiedBytecodeExpressionProgramExecutor.ExecuteStandalone(...)`, and the
standalone compiler can be invoked with dynamic identifiers enabled. The VM
already owns the required dynamic operations, including live environment lookup
through `LoadDynamicIdentifier` and receiver-sensitive dynamic call-target
preparation through the existing dynamic identifier call-target opcodes.

The open gap was in compiler specialization order. Some early standalone
property-read, optional-chain, and call-target appenders were still shaped
around activation-slot-only operand loading. When a free or global identifier
had no activation slot, those appenders could hard-fail before the later
dynamic-aware path or existing VM dynamic opcodes had a chance to handle the
payload. That made an ordinary no-private declined body look like it needed
`ExecutionPlanRunner` or AST fallback even though the correct replacement
surface was already the standalone unified-bytecode expression route.

## Decision

Admit free/global dynamic identifier property-read and call-target payloads by
making the standalone unified-bytecode compiler's owned appenders use
dynamic-aware operand and key emission when dynamic identifiers are allowed.

The accepted shape is:

- keep `LoweredExpressionProgramCache.ExecuteCached(...)` and already-lowered
  payload callers on `UnifiedBytecodeExpressionProgramExecutor.ExecuteStandalone(...)`;
- keep runtime semantics on the existing VM dynamic identifier and optional-chain
  opcodes instead of adding `ExecutionPlanRunner`, legacy AST evaluator, or new
  dynamic bridge fallback;
- update standalone compiler appenders that own optional named property reads,
  optional named read chains, optional computed continuations, and computed call
  keys to use dynamic-aware base/key emission where the expression shape is
  otherwise owned;
- preserve optional-chain nullish ordering by keeping the nullish jump before
  later property/key/call work; and
- preserve call reference receiver semantics, including live global rebinding
  and `with` binding-object receiver lookup.

`NoPrivateOrdinaryDeclinedBodyProofPackTests` is the focused proof surface for
this replacement-route slice. It should pin live global property reads,
optional-chain short-circuiting, live dynamic call targets, optional dynamic
calls, and `with`-scoped dynamic call receiver preservation before any future
slice removes more ordinary sync fallback coverage.

## Consequences

- E4 standalone expression payload retirement stays centralized behind the
  standalone executor, even when the payload originates inside a declined
  ordinary sync body.
- E5d ordinary sync fallback retirement can progress one payload family at a
  time: admitted dynamic identifier expression payloads can move to standalone
  unified bytecode without claiming that all no-private ordinary declined-body
  behavior is production-routed.
- Future compiler work must distinguish "not my boundary" from "hard unsupported"
  when a specialized appender does not own a dynamic identifier shape. A hard
  non-empty failure reason can block later dynamic-aware lowering and recreate
  the same fallback pressure.
- Optional-chain and call-target proof must remain behavioral, not only
  structural. A route that compiles but evaluates keys too early, loses live
  global rebinding, or collapses `with` receivers is not an acceptable
  replacement.

## Evidence

- ADR allocation note: local `rtk faktorial-api adr-next` was unavailable in
  this runtime (`No such file or directory`), so this learn pass used the
  Faktorial HTTP allocator endpoint `POST /api/adrs/next`, which returned
  `{"adr_id":382}`. The prefix `0382` was checked for duplicate use before
  writing.
- Delivery PR #3565 merged on current local `origin/main` as commit
  `83c4e659c`.
- Build-stage commit recorded by the issue:
  - `9cc3becf4 Admit dynamic standalone expression payloads`
- The local diff from the build-stage commit to the squash-merged main commit
  was empty for the delivery files.
- The merged delivery changed:
  - `src/Asynkron.JsEngine/Execution/UnifiedBytecode/UnifiedBytecodeCompiler.cs`
  - `tests/Asynkron.JsEngine.Tests/NoPrivateOrdinaryDeclinedBodyProofPackTests.cs`
- Build-stage verification recorded:
  - `rtk dotnet test tests/Asynkron.JsEngine.Tests --filter FullyQualifiedName~NoPrivateOrdinaryDeclinedBodyProofPackTests`
    passed 8 tests.
  - `rtk dotnet build src/Asynkron.JsEngine` passed with 0 errors and 0
    warnings.
  - `rtk git diff --check` passed.
- Review-stage verification recorded no unresolved review threads and a clean
  fallback scan over the changed standalone/unified-bytecode surfaces: no new
  `ExecutionPlanRunner`, `EvaluateLegacyAstExpression`, `EvaluateExpression`,
  `ProfileEvaluateExpression`, or `ExecuteDynamic` fallback calls.

## Related

- `docs/adrs/0381-pin-no-private-ordinary-declined-body-replacement-surface.md`
- `docs/adrs/0372-cache-ordinary-expression-payloads-on-owning-ast-nodes.md`
- `docs/adrs/0345-keep-standalone-expression-program-evaluation-behind-bridge.md`
- `docs/rules/expression-bytecode-ast-seams.md`
- `docs/rules/expression-bytecode-call-targets.md`
- `src/Asynkron.JsEngine/Ast/LoweredExpressionProgramCache.cs`
- `src/Asynkron.JsEngine/Execution/UnifiedBytecode/UnifiedBytecodeCompiler.cs`
- `src/Asynkron.JsEngine/Execution/UnifiedBytecode/UnifiedBytecodeExpressionProgramExecutor.cs`
- `src/Asynkron.JsEngine/Execution/UnifiedBytecode/UnifiedBytecodeVirtualMachine.cs`
- `tests/Asynkron.JsEngine.Tests/NoPrivateOrdinaryDeclinedBodyProofPackTests.cs`
