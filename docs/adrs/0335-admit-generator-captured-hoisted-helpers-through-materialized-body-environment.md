# ADR 0335: Admit generator captured hoisted helpers through a materialized body environment

## Status

Accepted

## Context

Faktorial issue #gh3238 and PR #3240 widened the B36 resumable hoisted
declaration route after PR #3234 had already proven a generator-owned
materialized body environment for captured nested function literals.

The previous B36 slice admitted direct root function-scoped declarations only
when the helper was non-capturing. Capturing helper declarations were still
declined because declaration instantiation and closure creation happen before
`ExecuteResumable` starts, while generator body locals live in flat slots on
`UnifiedBytecodeResumeState`. Creating the helper against the wrong closure
would route but read stale or missing locals after `yield`/resume.

The B23 materialized-body-environment work proved the missing environment
lifetime for sync generators, but it did not by itself prove broad B36
declaration graphs. A helper declaration can also reference sibling helpers,
itself recursively, dynamic/eval names, block/Annex B descriptor state, class
declarations, or async/async-generator settlement lifetimes.

## Decision

Admit only sync-generator direct root hoisted helper declarations that capture
root body locals, and only when invocation setup materializes the resumable body
environment before helper creation.

- Keep the helper declaration collection decline-first.
- Allow captured activation-slot helpers only for the sync-generator invocation
  path that can request and own a materialized body `JsEnvironment`.
- Create helper function values against that materialized body environment, then
  pre-populate the compiled unified-bytecode flat slot before
  `ExecuteResumable` starts.
- Mirror the helper binding into the materialized environment so closure
  lookups and VM flat-slot reads agree.
- Reject helpers whose bodies need lexical `this`, `new.target`, `super`, or
  private-name context until those closure contexts are materialized directly.
- Reject recursive or sibling-helper references in this slice; declaration
  graph ordering remains a separate B36 problem.
- Keep async and async-generator captured helpers declined until their
  invocation and settlement paths prove the same materialized body-environment
  lifetime across await/resume.
- Keep dynamic/eval helpers, descriptor-backed block or Annex B declarations,
  and class declarations on the existing IR routes.

## Consequences

- The generator route may now share the materialized body-environment foundation
  between captured function literals and captured hoisted helper declarations,
  but the eligibility proof must still distinguish literal creation from
  declaration instantiation.
- Future B36 work should not infer async or async-generator safety from this
  sync-generator admission.
- Recursive and sibling helper graphs need separate declaration-order proof,
  even when every helper would otherwise capture only root body locals.
- The B36 checklist remains partial; the resumable instruction-gap inventory is
  unchanged because `FunctionDeclarationInstruction` was already conditionally
  admitted by the previous non-capturing B36 slice.

## Evidence

- PR #3240 merged as squash commit
  `413f5ecf1ac1611cc3b3a0a8fde7f4c87d6c1c51`.
- Delivery commit before squash:
  `f2745d498b93160483a2e00c7ded8aeffa0e3658`.
- Implementation changed
  `src/Asynkron.JsEngine/Ast/TypedAstEvaluator.SyncGeneratorInvoker.cs`,
  `src/Asynkron.JsEngine/Ast/TypedAstEvaluator.UnifiedBytecodeResumableActivation.cs`,
  `src/Asynkron.JsEngine/Execution/UnifiedBytecode/UnifiedBytecodeProductionEligibility.cs`,
  `src/Asynkron.JsEngine/Ast/TypedAstEvaluator.AsyncFunctionInvoker.cs`, and
  `src/Asynkron.JsEngine/Ast/TypedAstEvaluator.AsyncGeneratorInvoker.cs`.
- Focused proof lives in
  `tests/Asynkron.JsEngine.Tests/UnifiedBytecodeResumableNestedFunctionTests.cs`,
  including a generator route hit where a captured helper reads updated locals
  across yields, async and async-generator captured-helper no-route pins, and
  recursive/sibling-helper graph declines.
- Delivery docs updated `docs/bytecode-progress.md`,
  `docs/unified-bytecode-expansion-contract.md`, and
  `docs/plans/bytecode-burndown-checklist.md` to keep B36 partial.

## Related

- ADR 0333:
  `docs/adrs/0333-admit-generator-captured-function-literals-through-materialized-resumable-body-environment.md`
- `docs/rules/unified-bytecode-prototypes.md`
