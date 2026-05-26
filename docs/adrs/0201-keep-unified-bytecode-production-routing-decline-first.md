# ADR 0201: Keep unified bytecode production routing decline-first

## Status

Accepted

## Context

Faktorial issue
`planitem-planmanual1779822558747978000-batch-1-production-eligibility-boundary-de-8d0f3a4e16`
and PR #2205 introduced the first production-routing eligibility contract for
the unified bytecode work. Earlier unified-bytecode ADRs kept the compiler and
VM as an internal prototype, but the production route now needs a separate
selector boundary so runtime wiring can ask whether a lowered plan is safe
without executing bytecode.

The current unified compiler can emit prototype `Binary`, `Jump`, and
`JumpIfFalse` opcodes, and prototype tests still prove those instructions.
That does not make them production-routable. The first production slice must be
narrower than prototype coverage because operator coercion, general control
flow, dynamic lookup, activation capture, `this`, `new.target`, calls,
arguments-object behavior, async functions, and generators each have observable
runtime semantics that the production route has not yet proven.

## Decision

Keep production routing behind an explicit, decline-first eligibility selector.

- Use the already-lowered `ExecutionPlan` plus an activation descriptor as the
  selector input. Do not re-read AST source and do not infer activation safety
  from plan shape alone.
- Return a stable accepted/declined result with a
  `UnifiedBytecodeProductionDeclineCode` and reason text before any unified VM
  execution.
- Limit the first production-eligible opcode subset to neutral slot/literal
  flow: `LoadSlot`, `LoadLiteral`, `StoreSlot`, and `Return`.
- Keep `Binary`, `Jump`, and `JumpIfFalse` prototype-only for production
  routing even though the prototype compiler and VM still support them.
- Decline unsupported activation, expression, identifier, call, label, break,
  continue, and opcode shapes explicitly. Future routing logs and tests should
  be able to distinguish those decline families.
- Do not add a VM fallback to `ExpressionProgram`, AST evaluation, or
  `ExecutionPlanRunner`, and do not enable a production execution route from a
  selector-only task.

## Consequences

- Future production-routing work must call the selector and preserve its
  diagnostics instead of directly invoking `UnifiedBytecodeCompiler.TryCompile`.
- Expanding the production subset requires both positive route tests and
  negative decline tests for adjacent unsupported shapes, even when prototype
  bytecode coverage already exists.
- Prototype bytecode can continue growing as a broader proof surface, while the
  production route remains a separately proven subset.
- Unsupported coverage remains visible as declined selector diagnostics instead
  of being hidden by statement IR, expression bytecode, or AST fallback.

## Related

- Issue `planitem-planmanual1779822558747978000-batch-1-production-eligibility-boundary-de-8d0f3a4e16`
- PR #2205
- ADR 0181: `docs/adrs/0181-keep-unified-bytecode-prototype-ir-owned-and-all-or-nothing.md`
- ADR 0186: `docs/adrs/0186-keep-unified-bytecode-function-kind-eligibility-explicit.md`
- ADR 0189: `docs/adrs/0189-keep-unified-bytecode-linear-expression-packs-flattened.md`
- ADR 0192: `docs/adrs/0192-keep-unified-bytecode-acyclic-control-flow-compiler-owned.md`
- `.claude/rules/unified-bytecode-prototypes.md`
- `src/Asynkron.JsEngine/Execution/UnifiedBytecode/UnifiedBytecodeProductionEligibility.cs`
- `tests/Asynkron.JsEngine.Tests/UnifiedBytecodeProductionEligibilityTests.cs`
