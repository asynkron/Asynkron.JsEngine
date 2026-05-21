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
   declaration, assignment, yield, `yield*`, `with`, loop initialization, and
   destructuring surfaces when the migration touches those areas.
5. Keep fixture assertions intent-based. Prefer finding the relevant
   instruction type and checking its payload over depending on fragile full
   instruction ordering.

## Why

Issue #1393 / PR #1397 expanded
`Issue400And722ExpressionBytecodeTraceabilityTests` after expression bytecode
coverage had outgrown single-shape checks. The risk was not a current runtime
failure; it was future IR work silently reintroducing executable AST payloads
while still passing behavior-level tests. Future migrations need explicit
instruction-payload guardrails so the non-dynamic fast path remains AST parse /
analyze -> IR emit -> bytecode execution, with AST evaluation quarantined to
intentional fallback seams only.
