# ADR 0352: Keep resumable direct eval admission route-family scoped

## Status

Accepted

## Context

Faktorial issue
`planitem-planitem-planmanual1780730299657353000-unified-bytecode-remaining-burndo-29a0fd043c`
/ PR #3316 widened resumable unified-bytecode activation checks for
declaration-free literal direct eval. The route can safely admit sync
generators and async functions when the eval source is literal,
declaration-free, and does not reference `arguments`.

The same change initially risked treating async generators as equivalent to the
other resumable families. They are not equivalent: async-generator execution has
its own settlement path, classified declined-body fallback, and route gates.
Widening direct eval there without a dedicated async-generator proof would turn
a direct-eval guard into a broad route-family admission.

## Decision

Keep declaration-free direct eval admission explicit per resumable invocation
family:

- sync generators and async functions may pass
  `allowDeclarationFreeDirectEval: true` when activation checks prove the eval
  source is a single literal with no declaration keyword and no `arguments`
  dependency;
- async generators must keep `allowDeclarationFreeDirectEval: false` until a
  future slice proves direct-eval semantics through the async-generator
  settlement path;
- declaration-bearing direct eval and runtime-source direct eval remain
  pre-VM declines for all resumable families.

## Consequences

- Future resumable direct-eval work must change each invoker deliberately
  instead of assuming `EvaluateResumable` acceptance is uniform across sync
  generator, async function, and async-generator callers.
- The detector remains a route-selection optimization, not eval semantics:
  declaration instantiation, runtime-source eval, and `arguments`-observable
  eval must stay on existing non-VM paths until the VM owns them.
- Async-generator route widening must include a public declined-neighbor
  settlement test before switching its direct-eval flag.

## Evidence

- PR #3316 merged as squash commit
  `b31b4e088ee5bbce0dcc365d17b449f25f56ec1bf`.
- Delivery commit before squash:
  `12ceec51e Keep async generator direct eval route scoped`.
- Implementation changed:
  - `src/Asynkron.JsEngine/Ast/TypedAstEvaluator.UnifiedBytecodeResumableActivation.cs`
  - `src/Asynkron.JsEngine/Ast/TypedAstEvaluator.SyncGeneratorInvoker.cs`
  - `src/Asynkron.JsEngine/Ast/TypedAstEvaluator.AsyncFunctionInvoker.cs`
  - `src/Asynkron.JsEngine/Ast/TypedAstEvaluator.AsyncGeneratorInvoker.cs`
- Focused proof lives in
  `tests/Asynkron.JsEngine.Tests/UnifiedBytecodeResumableDynamicIdentifierTests.cs`
  for routed sync-generator and async-function declaration-free direct eval,
  and in
  `tests/Asynkron.JsEngine.Tests/UnifiedBytecodeAsyncGeneratorRouteTests.cs`
  for the async-generator declined-neighbor settlement pin.
- Build-stage verification recorded the dynamic-identifier plus
  async-generator route packs passing 25 tests, the activation semantics proof
  pack passing 48 tests, and `rtk git diff --check` passing.
- ADR allocation note: the local `rtk faktorial-api adr-next` wrapper was not
  available in this worker, so this learn pass used the runtime allocator
  endpoint `POST /api/adrs/next`, which returned `{"adr_id":352}`.

## Related

- `docs/adrs/0349-keep-declined-async-generator-bodies-on-classified-runner-fallback.md`
- `docs/adrs/0351-keep-retained-with-closure-environments-dynamic-residue.md`
- `docs/rules/ecmascript-direct-eval-declaration-instantiation.md`
- `docs/rules/unified-bytecode-prototypes.md`
