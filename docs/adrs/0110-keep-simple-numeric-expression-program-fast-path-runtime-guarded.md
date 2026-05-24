# ADR 0110: Keep simple numeric expression program fast path runtime-guarded

## Status

Accepted

## Context

Issue `autrun-dir3mbor11cg-7a927e0289` / PR #1731 continued the
`ir-arithmetic` optimization track after earlier slices had already added a
number-number binary helper and a slot-proven self-assignment rewrite.

The selected benchmark was still a tight numeric loop:

```javascript
var total = 0;
for (var i = 0; i < 200000; i++) {
    total = total + ((i * 3 + 7) % 11);
}
total;
```

The baseline table reported:

```text
ir-arithmetic  asynkron_ms=6993  jint_ms=2070  Jint 3.38x faster
```

The CPU profile showed `HandleAssignmentSlot` and
`EvaluateExpressionProgram` dominating. The hot expression programs in this
profile contained only numeric literals, identifier reads, and numeric binary
operators. They did not require the full expression opcode switch, optional
chain flag stack, call/member handling, object constants, string constants, or
generic coercive binary semantics.

The tempting broader optimization would be to treat small or arithmetic-looking
expression programs as numeric at compile time. That is unsafe in JavaScript:
the same bytecode shape can still observe dynamic lookup, `with`, `arguments`,
string addition, BigInt, object coercion, calls, member access, and mixed
operand types at runtime.

## Decision

Keep the simple numeric `ExpressionProgram` fast path as a two-stage guard:

1. `ExpressionProgram` may cache a shape marker only for programs composed of
   `LoadLiteral`, `LoadIdentifier`, and the supported numeric `Binary`
   operators.
2. The runner must still prove runtime safety before taking the fast path:
   identifier caching is allowed, no `with` object is present in the
   environment chain, `arguments` is excluded, identifiers resolve through
   flat/scoped slot reads, and every operand is a number.
3. Unsupported shapes or non-number runtime values must fall back to the
   existing full expression interpreter.
4. The fast path may duplicate only the semantics proven safe for number-number
   arithmetic and numeric comparisons. Modulo must continue using
   `JsOps.MathMod`.

This marker is a candidate hint, not a semantic promise. The runtime guard is
the ownership boundary that keeps the optimization compatible with JavaScript's
dynamic lookup and coercion behavior.

## Consequences

- Future `ExpressionProgram` fast paths should be shape-filtered and
  runtime-guarded, not inferred from benchmark source text alone.
- Dynamic lookup paths, especially `with` and `arguments`, remain on the
  generic interpreter path unless a separate issue proves equivalent observable
  semantics.
- Mixed operands, string concatenation, BigInt, object coercion, calls, member
  access, optional chaining, and effectful expression opcodes are not covered by
  this decision.
- Performance claims for this path should keep noisy profile runs conservative:
  use the selected profile, report the slowest comparable final run when runs
  vary, and keep semantic proof separate from timing proof.
- This ADR complements ADR 0107's slot-proven assignment rewrite and ADR 0102's
  small expression-buffer policy.

## Related

- `docs/performance/ir-arithmetic-simple-numeric-expression-fast-path.md`
- `docs/performance/ir-arithmetic-binary-fast-path.md`
- `docs/performance/ir-arithmetic-self-assignment-compound-slot.md`
- `.claude/rules/performance-profiling-guardrails.md`
