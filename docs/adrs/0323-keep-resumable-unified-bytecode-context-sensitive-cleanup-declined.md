# ADR 0323: Keep resumable unified bytecode context-sensitive cleanup declined

## Status

Accepted

## Context

Faktorial issue #3172 / PR #3179 repaired red `main` after the resumable
unified-bytecode route admitted generator shapes whose ordinary completion was
correct, but whose suspension-adjacent context was still owned by the IR runner.

Two unsafe boundaries were exposed:

- external generator early-close (`.return()` / `.throw()`) while suspended in a
  protected `try` must run the pending `finally`; the resumable VM does not yet
  drive captured or free plain assignments inside that cleanup body;
- nested function literals that look non-capturing by ordinary slot dependency
  can still require caller context when an arrow uses lexical `this` or private
  names.

Both failures were eligibility mistakes. The runtime VM did not gain the missing
cleanup or closure-context semantics in this repair, so routing those shapes
would have made the production unified-bytecode path observably wrong.

## Decision

Keep resumable production eligibility decline-first for context-sensitive
generator cleanup and nested function context.

- Treat captured or free plain assignments in a pending `finally` body the same
  as other captured/free dynamic mutations for early-close purposes: keep them
  on the IR runner until the resumable VM explicitly owns pending-finally
  cleanup execution for that mutation family.
- When a nested function literal appears inside a resumable body, inspect the
  nested lowered plan before admission. Decline the outer resumable route if the
  nested literal needs arrow lexical `this`, `new.target`, `super`, or private
  name context that the resumable route has not materialized.
- Do not infer resumable safety from "no activation-slot capture" alone.
  Lexical context and private-name dependencies are separate from ordinary
  slot-capture dependency.
- Preserve the IR runner as the correctness owner for these shapes until a
  future slice adds VM-owned semantics and proves both route and no-route
  boundaries.

## Consequences

- Resumable unified bytecode remains narrower than the opcode allowlist where
  suspension cleanup or nested closure context has extra semantic state.
- Non-capturing nested functions that do not need lexical/private context still
  route through the resumable VM.
- Generator early-close shapes that mutate escaped bindings in `finally` stay
  correct through the IR runner.
- Future resumable widening must prove `.return()` / `.throw()` pending-finally
  execution and nested lexical/private context materialization before removing
  these declines.

## Evidence

- Delivery PR #3179 merged as commit
  `e2e0da91828acf3c023606d14590f2d417e3a144`.
- Build-stage verification recorded:
  - reproduced red `main` with `rtk dotnet test tests/Asynkron.JsEngine.Tests`
    failing generator pending-finally cleanup and generator private-field arrow
    access tests;
  - focused reproduced/new guard filter passed 5 tests;
  - `UnifiedBytecodeResumableNestedFunctionTests` passed 16 tests;
  - `rtk git diff --check` passed.
- The delivery updated:
  - `src/Asynkron.JsEngine/Execution/UnifiedBytecode/UnifiedBytecodeProductionEligibility.cs`
  - `tests/Asynkron.JsEngine.Tests/UnifiedBytecodeResumableNestedFunctionTests.cs`

## Related

- ADR 0277:
  `docs/adrs/0277-keep-resumable-unified-bytecode-state-bounded-and-yield-star-declined.md`
- ADR 0283:
  `docs/adrs/0283-accept-this-dependent-async-generator-in-resumable-unified-bytecode.md`
- ADR 0321:
  `docs/adrs/0321-admit-simple-async-generator-resumable-route.md`
- `docs/rules/unified-bytecode-prototypes.md`
