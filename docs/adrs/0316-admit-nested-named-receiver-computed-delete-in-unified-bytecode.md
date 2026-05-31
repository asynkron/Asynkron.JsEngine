# ADR 0316: Admit nested named receiver computed delete in unified bytecode

## Status

Accepted

## Context

PR #2931 widened the production unified-bytecode delete boundary from direct
computed deletes such as:

```js
delete box[key]
```

to simple nested named receiver chains with a computed final key:

```js
delete box.child[key]
```

ADR 0309 had already admitted ordinary named, nested named, and direct computed
property delete. The remaining gap was not VM semantics: the existing
`GetNamedProperty` and `DeleteComputedProperty` opcodes already carry the
needed receiver and descriptor-aware delete behavior. The risk was selector
overreach. Optional receiver chains, dynamic receiver/key dependencies, and
richer computed-key payloads still need separate ownership and must not route
through the ordinary computed-delete lane.

## Decision

Admit nested named receiver computed property delete to production unified
bytecode when all of these are true:

1. the root receiver is activation-resolved;
2. every intermediate receiver hop is a non-optional, non-private named property
   read;
3. the final key operand is compiler-owned by the existing simple computed-key
   boundary; and
4. the final operation is the existing `DeleteComputedProperty` opcode.

Lower the accepted route by composing existing opcodes:

1. emit `GetNamedProperty` for each admitted receiver hop;
2. emit the owned computed-key payload; and
3. finish with `DeleteComputedProperty`.

Do not add VM fallback to `ExpressionProgram`, `ExecutionPlanRunner`, or AST
evaluation for this shape. Keep optional receiver chains, richer computed-key
payloads, dynamic lookup, private names, and `super` as pre-VM declines until a
later slice owns selector, compiler, VM, and route proof for those exact
semantics.

## Consequences

- `delete box.child[key]` can use the production unified-bytecode fast path
  without adding a new opcode or changing VM delete semantics.
- Ordinary computed-delete widening remains a composed owned-opcode route, not
  permission for arbitrary receiver expressions or arbitrary key payloads.
- Future delete widening should prove the accepted opcode route, public
  fast-path logging, computed-key coercion/order, strict/sloppy descriptor
  behavior, and neighboring declines in the same delivery slice.

## Evidence

- Delivery PR #2931 merged as commit `03e2e0cbd`.
- The original delivery commit was `aabc7c600` on branch
  `agent-go/task-gh2926`.
- Changed production surfaces:
  - `src/Asynkron.JsEngine/Execution/UnifiedBytecode/UnifiedBytecodeProductionEligibility.cs`
  - `tests/Asynkron.JsEngine.Tests/UnifiedBytecodeProductionEligibilityTests.cs`
  - `tests/Asynkron.JsEngine.Tests/UnifiedBytecodeProductionInvocationTests.cs`
  - `docs/unified-bytecode-expansion-contract.md`
- Focused delivery verification passed:
  - `rtk dotnet test tests/Asynkron.JsEngine.Tests --filter "FullyQualifiedName~UnifiedBytecodeProductionEligibilityTests&FullyQualifiedName~PropertyDelete"`: 11 passed.
  - `rtk dotnet test tests/Asynkron.JsEngine.Tests --filter "FullyQualifiedName~UnifiedBytecodeProductionInvocationTests&FullyQualifiedName~PropertyDelete"`: 5 passed.
  - `rtk dotnet test tests/Asynkron.JsEngine.Tests --filter "FullyQualifiedName~ExpressionProgramCoverageMapTests&FullyQualifiedName~UnifiedBytecodeExpansionContract"`: 1 passed.
  - `rtk git diff --check`: clean.
  - Runner AST seam scan for `EvaluateExpression(` / `ProfileEvaluateExpression(`
    under `TypedAstEvaluator.ExecutionPlanRunner*`: no matches.

## Issue / PR

Issue #gh2926 / PR #2931.

## Related

- `docs/adrs/0308-admit-nested-named-property-write-receiver-chains-in-unified-bytecode.md`
- `docs/adrs/0309-admit-ordinary-property-delete-in-unified-bytecode.md`
- `docs/adrs/0311-admit-optional-named-computed-read-continuations-in-unified-bytecode.md`
- `docs/rules/unified-bytecode-prototypes.md`
