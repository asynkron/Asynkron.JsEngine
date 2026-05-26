# ADR 0181: Keep unified bytecode prototype IR-owned and all-or-nothing

## Status

Accepted

## Context

Issue #2118 / PR #2137 added the first vertical slice for a future unified
bytecode pipeline. The long-term target is:

```text
AST -> current IR -> unified bytecode -> unified VM
```

The current production runtime remains statement IR plus `ExpressionProgram`
payloads. The first slice deliberately used the already-lowered
`ExecutionPlan` as compiler input so existing lowering, evaluation order,
scope metadata, and JavaScript semantics stay owned by the current IR.

The accepted prototype only handles a sync function entrypoint shaped as a
non-awaited `ReturnInstruction` with a `ReturnProgram`, and only flattens
parameter `LoadIdentifier` operations plus binary numeric `Add` into:

```text
LoadSlot
LoadSlot
Add
Return
```

## Decision

Keep the unified bytecode prototype as an internal, IR-owned, all-or-nothing
compiler and VM surface.

- Use `ExecutionPlan` as the source of truth for prototype compilation.
- Keep normal production execution on the existing runtime until a separate
  routing issue proves a broader contract.
- Decline unsupported plans during compilation with explicit reasons.
- Do not add VM fallback to `ExpressionProgram` evaluation, AST evaluation, or
  `ExecutionPlanRunner`.
- Expand the prototype only through narrow shape slices that include compile
  shape tests, execution proof, and negative unsupported-plan proof.
- Treat the current numeric `Add` VM operation as a prototype proof, not a
  claim that full JavaScript `+` coercion has been migrated.

## Consequences

- Future unified-bytecode work should extend the compiler from already-lowered
  IR instead of creating a parallel AST-to-bytecode compiler.
- Eligibility remains visible and testable because unsupported shapes fail
  before VM execution.
- The VM cannot hide coverage gaps by delegating to existing evaluators.
- Runtime behavior remains unchanged while the prototype grows behind internal
  APIs.
- Broader routing will need its own evidence: current coverage, parity tests,
  unsupported-shape accounting, runner seam checks, and performance evidence
  from the same worktree.

## Related

- Issue #2118
- PR #2137
- `src/Asynkron.JsEngine/Execution/UnifiedBytecode/`
- `tests/Asynkron.JsEngine.Tests/UnifiedBytecodePrototypeTests.cs`
- `.claude/rules/unified-bytecode-prototypes.md`
