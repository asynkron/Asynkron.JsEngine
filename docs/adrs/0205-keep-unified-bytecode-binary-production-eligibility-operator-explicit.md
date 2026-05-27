# ADR 0205: Keep unified bytecode Binary production eligibility operator-explicit

## Status

Accepted

## Context

Faktorial issue
`planitem-planmanual1779822558747978000-batch-1-production-eligibility-boundary-ba-c0e3afa6d7`
and PR #2231 hardened the production eligibility boundary for the unified
bytecode `Binary` opcode after the first production sync route from ADR 0204.

The prototype compiler and VM already support a bounded Binary set:
`+`, `-`, `*`, `/`, `%`, `<`, `<=`, `>`, and `>=`. That prototype support is
not enough for production routing. Arithmetic and comparison operators have
observable coercion, abrupt-completion, BigInt, object, and string behavior, and
branch or loop plans can contain Binary conditions before they reach
`Jump`/`JumpIfFalse` opcodes.

ADR 0201 already kept `Binary`, `Jump`, and `JumpIfFalse` prototype-only for
production routing. The gap exposed by this issue was diagnostic and ordering
precision: a branch or loop containing a Binary condition should decline at the
Binary semantic boundary first, not at the later structural jump boundary, and
the decline reason should name the exact operator under review.

## Decision

Keep production `Binary` eligibility closed and operator-explicit until each
operator has production-grade runtime parity proof.

- Treat `UnifiedBytecodeOpCode.Binary` as a production decline before checking
  accumulated `Jump` or `JumpIfFalse` structural declines.
- Preserve the stable decline code
  `UnifiedBytecodeProductionDeclineCode.PrototypeOnlyBinaryOpcode`, but make
  the reason include the decoded operator token for known Binary operands.
- Keep the production route narrower than prototype compiler coverage. Numeric
  VM parity tests for the current compiled Binary set prove only the prototype
  VM surface, not production eligibility.
- When Binary appears inside branch or loop conditions, decline on the Binary
  semantic gate before branch or loop admission can widen through jump support.
- Keep production invocation tests proving Binary comparison functions stay off
  `unified-bytecode-production-fast-path` until a later routing slice proves the
  operator semantics.

## Consequences

- Future production widening must be operator-by-operator, not a blanket
  promotion of the prototype Binary opcode.
- Branch or loop production routing cannot become a back door for unproven
  operator semantics. Structural control-flow support must wait behind the
  semantic eligibility of the condition payloads it executes.
- Decline diagnostics remain useful for planning: a rejected `a < b` branch now
  points at the `<` operator boundary rather than only saying the plan contains
  a prototype jump opcode.
- Prototype VM tests should continue to compare numeric results against the
  current runtime for the compiled Binary set, while production route tests
  should prove the same functions do not use the fast path until the selector
  is deliberately widened.

## Related

- Issue
  `planitem-planmanual1779822558747978000-batch-1-production-eligibility-boundary-ba-c0e3afa6d7`
- PR #2231
- Commit `2f017b8c853caa3a6fe668bdcafe89a87379cc79`
- ADR 0201: `docs/adrs/0201-keep-unified-bytecode-production-routing-decline-first.md`
- ADR 0204: `docs/adrs/0204-keep-unified-bytecode-sync-production-routing-slot-bridged.md`
- ADR 0192: `docs/adrs/0192-keep-unified-bytecode-acyclic-control-flow-compiler-owned.md`
- `.claude/rules/unified-bytecode-prototypes.md`
- `src/Asynkron.JsEngine/Execution/UnifiedBytecode/UnifiedBytecodeProductionEligibility.cs`
- `tests/Asynkron.JsEngine.Tests/UnifiedBytecodeProductionEligibilityTests.cs`
- `tests/Asynkron.JsEngine.Tests/UnifiedBytecodeProductionInvocationTests.cs`
- `tests/Asynkron.JsEngine.Tests/UnifiedBytecodePrototypeTests.cs`
