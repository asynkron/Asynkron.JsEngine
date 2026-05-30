# ADR 0293: Admit logical and nullish expressions in unified bytecode with peek-semantics jump opcodes

## Status

Accepted

## Context

Faktorial issue
`planitem-planmanual1780157100924814000-baseline-batch-5-logical-and-nullish-opera-f2c1e6c23b`
and PR #2761 admitted `&&`, `||`, and `??` expressions to the production
unified-bytecode VM by adding three new expression-level short-circuit jump opcodes.

The expression-program IR already emits `JumpIfFalse`, `JumpIfTrue`, and
`JumpIfNotNullish` for these operators at the expression bytecode layer.

The existing unified-bytecode opcode `JumpIfFalse` is used for statement-level
`if`/`while` condition branches. That opcode has **pop semantics**: when the
branch is taken, the top-of-stack (TOS) condition value is consumed and not
left on the stack, because `if (cond)` never uses `cond` as an expression
result.

Short-circuit logical/nullish expressions are fundamentally different: when
`a && b` short-circuits (LHS is falsy), the value of `a` IS the result of
the expression. It must remain on the stack so the expression produces the
correct value. The same holds for `||` (RHS skipped when LHS is truthy) and
`??` (RHS skipped when LHS is not nullish). Reusing `JumpIfFalse` (pop
semantics) for the `&&` short-circuit path would consume the LHS and produce
an incorrect `undefined` or garbage result.

At the same time, `JumpIfShortCircuited` — used by optional-chain
`?.`-access IR — was already present in the expression bytecode family but
remained declined as `OptionalChainDependency` because optional-chain
execution requires separate ownership.

## Decision

Admit `&&`, `||`, and `??` expression-level short-circuit jumps through three
new **peek-semantics** unified bytecode opcodes:

1. `JumpIfShortCircuitFalse` — emitted for expression `JumpIfFalse`.  
   If TOS is truthy → fall through (continue evaluating RHS); TOS unchanged.  
   If TOS is falsy  → jump to target (TOS is the short-circuit result value, preserved).

2. `JumpIfShortCircuitTrue` — emitted for expression `JumpIfTrue`.  
   If TOS is truthy → jump to target (TOS is the short-circuit result value, preserved).  
   If TOS is falsy  → fall through; TOS unchanged.

3. `JumpIfShortCircuitNotNullish` — emitted for expression `JumpIfNotNullish`.  
   If TOS is not nullish → jump to target (TOS is the preserved result value).  
   If TOS is nullish    → fall through; TOS unchanged.

None of these opcodes decrement `stackPointer`; they only move `programCounter`.

The compiler changed from `foreach` to `for` over the expression program ops in
`TryAppendExpressionProgramOps`, added an `exprPcToUnifiedPc[]` backpatch map,
and emits placeholder operands (`0`) for the three new opcodes, then backpatches
the target after the full expression op sequence is emitted.

`JumpIfShortCircuited` (optional chain) remains declined as
`OptionalChainDependency`. It is not in scope: optional chain requires a separate
ownership slice for nullable-receiver, optional-member, and optional-call semantics.

All three new opcodes are added to `TryFindPrototypeOnlyOpcode`'s
production-eligible set and to the expansion contract opcode inventory.

Non-resumable VM path only: the resumable unified-bytecode path
(`ExecuteResumable`) does not include `&&`/`||`/`??` opcode cases, and
functions with these operators that have async/generator function kind are
declined before the resumable VM by the standard resumable
`TryFindUnsupportedResumableOpcode` allowlist.

## Consequences

- `&&`, `||`, and `??` expressions execute in production unified bytecode for
  ordinary sync functions without fallback to `ExpressionProgram`,
  `ExecutionPlanRunner`, or AST evaluation.
- The peek/pop distinction is now explicit in the opcode taxonomy:
  `JumpIfFalse` = pop semantics (statement branch conditions);
  `JumpIfShortCircuitFalse/True/NotNullish` = peek semantics (expression short-circuit).
- Optional-chain `?.` expressions continue to decline as `OptionalChainDependency`
  until a future slice owns that execution model end to end.
- The compiler backpatch pattern for expression-level forward jumps generalizes
  the same `exprPcToUnifiedPc[]` forward-target resolution already used for
  statement-level branch targets, applied here within the expression program op
  sequence.

## Evidence

- Delivery PR #2761 merged as commit
  `c91728b7 feat: admit &&, ||, ?? expressions to production unified bytecode VM (ADR 0238 batch-5)`.
- Build-stage baseline signal before delivery: `JumpIfShortCircuitFalse/True/NotNullish`
  opcodes in `UnifiedBytecodeProgram.cs` = 0.
- Build-stage final signal after delivery: three new opcodes present in enum;
  `UnifiedBytecodeCompiler.cs` emits them from expression `JumpIfFalse/True/NotNullish` ops.
- `dotnet build -c Release` — 0 errors, 0 warnings.
- Focused test pack passed:
  `dotnet test tests/Asynkron.JsEngine.Tests --filter "FullyQualifiedName~UnifiedBytecodeProductionEligibilityTests|FullyQualifiedName~UnifiedBytecodeProductionInvocationTests"`
  — 4 new eligibility tests (`&&`, `||`, `??` accepted; optional chain still declined),
  7 new invocation tests (`&&` short-circuit/fall-through, `||` short-circuit/fall-through,
  `??` non-nullish/null/undefined), all asserting `unified-bytecode-production-fast-path` log
  and correct JS results.

## Related

- `docs/adrs/0210-keep-unified-bytecode-control-flow-production-routing-operator-and-shape-owned.md`
- `docs/adrs/0238-keep-unified-bytecode-compound-property-writes-get-for-set-owned.md`
- `docs/adrs/0246-keep-unified-bytecode-expansion-contract-source-of-truth-and-drift-guarded.md`
- `docs/adrs/0289-admit-optional-calls-in-unified-bytecode-nullish-short-circuit-receiver-owned.md`
- `docs/rules/unified-bytecode-prototypes.md`
- `src/Asynkron.JsEngine/Execution/UnifiedBytecode/UnifiedBytecodeCompiler.cs`
- `src/Asynkron.JsEngine/Execution/UnifiedBytecode/UnifiedBytecodeProductionEligibility.cs`
- `src/Asynkron.JsEngine/Execution/UnifiedBytecode/UnifiedBytecodeProgram.cs`
- `src/Asynkron.JsEngine/Execution/UnifiedBytecode/UnifiedBytecodeVirtualMachine.cs`
- `tests/Asynkron.JsEngine.Tests/UnifiedBytecodeProductionEligibilityTests.cs`
- `tests/Asynkron.JsEngine.Tests/UnifiedBytecodeProductionInvocationTests.cs`
