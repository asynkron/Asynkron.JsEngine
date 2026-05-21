# Expression Bytecode IR Payload Guardrails

When migrating expression-bearing IR instructions away from executable AST
payloads, prove the bytecode payload contract at the instruction level.

## Rules

1. Add or extend plan-walking tests over representative
   `ExecutionPlan.Instructions` when an instruction family starts carrying an
   `ExpressionProgram`, awaited `ExpressionProgram`, or equivalent bytecode
   payload.
2. Assert that each migrated expression-bearing instruction exposes the expected
   bytecode payload and that the payload is non-empty. Do not rely only on final
   runtime behavior; a runtime pass can hide a reintroduced AST payload.
3. If a migrated instruction keeps an AST compatibility property such as
   `ReturnExpression`, `Expression`, `Initializer`, `ValueExpression`, or
   `SourceExpression`, assert that the property remains null in the guardrail
   test.
4. Cover representative statement and control-flow families, not only the
   easiest expression statement shape. Include return, throw, branch,
   declaration, assignment, logical compound assignment, compound assignment,
   awaited assignment variants, await-and-discard, yield, `yield*`, `with`,
   loop initialization, and destructuring surfaces when the migration touches
   those areas.
5. Keep fixture assertions intent-based. Prefer finding the relevant
   instruction type and checking its payload over depending on fragile full
   instruction ordering.
6. For binding-target program guardrails, inspect nested subprograms as well as
   the top-level initializer/source program. Computed object binding names,
   default values, and similar nested `BindingTargetProgram` payloads must be
   non-empty bytecode programs when the migrated shape depends on them.

## Why

Issue #1393 / PR #1397 expanded
`Issue400And722ExpressionBytecodeTraceabilityTests` after expression bytecode
coverage had outgrown single-shape checks. The risk was not a current runtime
failure; it was future IR work silently reintroducing executable AST payloads
while still passing behavior-level tests. Future migrations need explicit
instruction-payload guardrails so the non-dynamic fast path remains AST parse /
analyze -> IR emit -> bytecode execution, with AST evaluation quarantined to
intentional fallback seams only.

Issue #1439 / PR #1448 expanded the same guardrail after investigation found
representative instruction coverage still missed expression-bearing payload
families: logical compound assignment, compound assignment, awaited assignment
variants, await-and-discard, and nested object binding target programs. The
lesson is that a guardrail can be structurally correct but still too shallow if
it checks only the top-level instruction payload and skips suspending variants
or nested binding subprograms.
