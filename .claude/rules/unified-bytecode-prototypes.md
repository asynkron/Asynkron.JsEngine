# Unified Bytecode Prototypes

When extending the unified bytecode prototype, keep it IR-owned, internal, and
all-or-nothing until a separate routing issue proves production readiness.

## Rules

1. Use `ExecutionPlan` as the prototype compiler input. Do not create a
   parallel AST-to-unified-bytecode compiler for shapes that the current IR
   already lowers and annotates.
2. Keep eligibility at compile time. Unsupported statements, expression ops,
   identifiers, async/generator forms, locals, control flow, declarations, or
   dynamic shapes must return an unsupported reason before VM execution.
3. Do not add fallback inside `UnifiedBytecodeVirtualMachine` to
   `ExpressionProgram` evaluation, AST evaluation, or `ExecutionPlanRunner`.
   VM execution should only execute bytecode the unified compiler emitted.
4. Do not route normal production execution through the unified VM in a
   prototype-expansion issue. Runtime routing needs its own issue and proof
   pack.
5. For each accepted shape, add focused tests for the emitted unified opcode
   stream, a minimal execution result, and at least one nearby unsupported
   shape that declines cleanly.
6. Keep JavaScript semantic claims narrow. A prototype op such as numeric
   `Add` proves only the tested VM behavior; full JavaScript operator coercion
   requires an explicit migration and parity proof.

## Why

Issue #2118 / PR #2137 introduced the first unified bytecode slice for
`function add(x, y) { return x + y; }`. The useful decision was not just the
new files; it was the boundary: compile from existing `ExecutionPlan`, flatten
only the proven return-expression payload, keep the VM fallback-free, and leave
production routing untouched. Future agents should preserve that boundary so
unified-bytecode coverage gaps stay explicit instead of being masked by the
existing statement IR, expression bytecode, or AST evaluators.

Related ADR: `docs/adrs/0181-keep-unified-bytecode-prototype-ir-owned-and-all-or-nothing.md`.
