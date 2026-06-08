# ADR 0352: Keep resumable direct eval admission route-family scoped

## Status

Accepted

## Context

Faktorial issue
`planitem-planitem-planmanual1780730299657353000-unified-bytecode-remaining-burndo-29a0fd043c`
/ PR #3316 widened resumable unified-bytecode activation checks for
declaration-free literal direct eval. The route can safely admit sync
generators and async functions when the eval source is literal,
declaration-free, and does not require unproved activation ownership.

The same change initially risked treating async generators as equivalent to the
other resumable families. They are not equivalent: async-generator execution has
its own settlement path, classified declined-body fallback, and route gates.
Widening direct eval there without a dedicated async-generator proof would turn
a direct-eval guard into a broad route-family admission.

PR #3474 later proved the bounded implicit-`arguments` lane for sync
generators, async functions, and async generators by materializing the arguments
object in the resumable body environment. Review-back found that the same
source-level `eval("arguments[0]")` shape is not uniformly safe: if
`arguments` is an explicit parameter or body lexical binding, the VM route must
not substitute the implicit arguments object for that user binding.

## Decision

Keep declaration-free direct eval admission explicit per resumable invocation
family:

- sync generators, async functions, and async generators may pass
  `allowDeclarationFreeDirectEval: true` only when activation checks prove the
  eval source is a single literal with no declaration keyword and no unproved
  dynamic activation dependency;
- bounded implicit-`arguments` reads may route only when the invoker creates the
  resumable arguments object in the materialized body environment;
- explicit `arguments` parameter or body lexical bindings must decline to IR so
  direct eval resolves the user binding instead of the implicit arguments
  object;
- declaration-bearing direct eval and runtime-source direct eval remain
  pre-VM declines for all resumable families.

## Consequences

- Future resumable direct-eval work must change each invoker deliberately
  instead of assuming `EvaluateResumable` acceptance is uniform across sync
  generator, async function, and async-generator callers.
- The detector remains a route-selection optimization, not eval semantics:
  declaration instantiation, runtime-source eval, and explicit `arguments`
  binding shadowing must stay on existing non-VM paths until the VM owns them.
- Async-generator route widening and later `arguments` widening must include a
  public declined-neighbor settlement/no-route test before switching the
  route-family flag or admitting a new binding-owner lane.

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

Follow-up evidence from PR #3474:

- PR #3474 merged as squash commit
  `cce1f142a3101c492f9e760c37df6714d79593d1`.
- The delivery added bounded implicit-`arguments` direct-eval route proof for
  sync generators, async functions, and async generators, plus the review-back
  no-route neighbor
  `AsyncDirectEvalArgumentsParameter_StaysOnIrPathAndResolvesParameter`.
- Implementation changed the three resumable invokers to define the resumable
  arguments object only for implicit `arguments` access, and changed
  `TypedAstEvaluator.UnifiedBytecodeResumableActivation` to decline direct eval
  when `arguments` is an explicit parameter or body lexical binding.

## Related

- `docs/adrs/0349-keep-declined-async-generator-bodies-on-classified-runner-fallback.md`
- `docs/adrs/0351-keep-retained-with-closure-environments-dynamic-residue.md`
- `docs/rules/ecmascript-direct-eval-declaration-instantiation.md`
- `docs/rules/unified-bytecode-prototypes.md`
