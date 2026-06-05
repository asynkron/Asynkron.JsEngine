# ADR 0324: Keep resumable generator context and cleanup declines explicit

## Status

Accepted

## Context

Issue `autrun-dj0vu3jrima8-13c399233d` / PR #3178 landed after the
resumable unified-bytecode route had widened across several generator and async
surfaces. That widening made two nearby unsupported sync-generator shapes easier
to misroute:

- Early `.return(...)` / `.throw(...)` while suspended inside a protected
  `try` must run pending `finally` cleanup. Captured or free plain writes in
  that cleanup still need the existing IR runner because the resumable VM does
  not yet re-drive those user finally bodies for early close.
- Nested function literals can look slot-local while still depending on
  context the resumable VM has not materialized, especially arrow lexical
  `this` / `new.target` / `super` dependencies and private-name access.

The repair reused the focused generator edge-case guard from the sibling
generator repair and added no-route tests for those unsupported shapes. The
important decision is the boundary, not the implementation detail: eligibility
must decline before VM execution when correctness depends on generator cleanup
or nested closure context that the resumable VM does not own.

## Decision

Keep resumable generator production eligibility conservative around cleanup and
nested function context.

- Decline captured/free plain writes in pending `finally` cleanup for suspended
  generators until the resumable VM owns early-close cleanup execution.
- Inspect nested function literal plans for lexical-this, `new.target`, `super`,
  and private-name dependencies before treating the literal as VM-safe.
- Keep these unsupported shapes on the existing IR runner instead of adding VM
  fallback into `ExecutionPlanRunner`, `ExpressionProgram`, or AST evaluation.
- Pair any future widening with both positive route proof and adjacent
  no-route proof for early-close cleanup and nested closure-context hazards.

## Consequences

- Resumable generator routing stays a finite boundary-mapping problem rather
  than a best-effort route with runtime fallback.
- Generator correctness for early close and private/lexical context remains on
  the existing proven path until the VM owns those semantics directly.
- Future slices that admit one of these shapes must update the route boundary,
  tests, roadmap/contract evidence, and this ADR/rule guidance together.

## Evidence

- Delivery PR #3178 merged as commit `ed41f9fb0`.
- The focused repair commit `e53f1d42b` / PR #3179 added:
  - `UnifiedBytecodeProductionEligibility.FunctionLiteralNeedsLexicalThisOrPrivateNameContext(...)`
  - a finally-cleanup guard for `AssignmentSlotInstruction`
  - no-route tests
    `GeneratorTryFinallyMutatesOuterBindingAfterYield_CorrectButDeclinesToRunner`
    and `GeneratorNestedArrowUsesPrivateThis_CorrectButDeclinesToRunner`
- Build-stage evidence recorded the repaired focused pack passing 5/5 tests
  after the baseline `make quality` failures:
  `Generator_PrivateFieldAccess_FromArrowFunction`,
  `Generator_ReturnExecutesPendingFinally_NoYieldInFinally`, and
  `Generator_ThrowExecutesPendingFinally_NoYieldInFinally`.
- ADR allocation note: this learn runtime could not execute
  `rtk faktorial-api adr-next` (`No such file or directory`). During conflict
  repair, `origin/main` already owned ADR `0323`, so the documented
  collision-repair fallback kept that accepted ADR stable and renumbered this
  branch artifact to the next free prefix, `0324`.

## Related

- `docs/rules/unified-bytecode-prototypes.md`
- `src/Asynkron.JsEngine/Execution/UnifiedBytecode/UnifiedBytecodeProductionEligibility.cs`
- `tests/Asynkron.JsEngine.Tests/UnifiedBytecodeResumableNestedFunctionTests.cs`
