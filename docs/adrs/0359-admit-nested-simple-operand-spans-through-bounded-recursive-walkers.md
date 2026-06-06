# ADR 0359: Admit nested simple operand spans through bounded recursive walkers

## Status

Accepted

## Context

Faktorial issue
`planitem-planmanual1780730299657353000-unified-bytecode-remaining-burndown-03-com-e5b877aa15`
/ PR #3345 handled A51k, the remaining simple binary, unary, control, and
conditional operand-span gap in production unified bytecode.

Flat operand spans such as `a + b`, `-a`, and already-owned control expression
programs were admitted, but representative nested value operands still declined
or failed compiler emission. Examples included:

```js
function nested(a, b, c, d, e) {
    return (a + b) * (c ? d : e);
}

function unary(a, b) {
    return -(a + b);
}
```

The first attempted repair was too permissive in the opposite direction: it
could recurse into call, dynamic, private, or other neighboring operand shapes
that A51k did not own. That would have turned a bounded value-span repair into a
route-widening slice for unrelated semantics.

## Decision

Admit nested simple binary, unary, `typeof`, logical-control, and conditional
operand spans only through bounded recursive span walkers that are mirrored by
the compiler append path.

- Eligibility and compiler emission both split flat binary/unary helpers from
  nested helpers.
- Nested helpers receive an `endExclusive` bound and must consume exactly the
  nested operand region they claim.
- Control-expression probes used as nested operands are bounded and roll back
  speculative compiler output when the candidate exceeds the caller's region.
- Recursive operand probing explicitly disables binary/unary recursion inside
  logical and conditional suboperands where doing so would let one source shape
  consume a neighboring operator outside its owned region.
- Calls, private-name neighbors, and dynamic-neighbor operands stay declined by
  their existing dependency families unless a future slice proves those
  semantics directly.
- Positive proof must assert the emitted owned opcodes for nested binary,
  unary, control, conditional, and `typeof` forms; nearby negative proof must
  keep call-neighbor operands declined.

This keeps A51k as a value-span admission, not a broad expression-program
fallback or a generic "anything nested" compiler route.

## Consequences

- Nested literal-value operand spans can enter the production unified-bytecode
  VM when every nested operation is already owned by the VM.
- Eligibility measurement and compiler emission must stay in lockstep. A shape
  is not admitted until both the measurer and appender can walk the same bounded
  region.
- Future span widening should add flat helper support first, then decide whether
  recursive composition is safe for that helper. Do not reuse the nested helper
  for calls, private names, dynamic lookup, assignment, update, delete, or other
  side-effecting neighbors without a separate proof pack.
- Failed speculative probes must restore all touched compiler builders before
  another fallback helper tries to emit the same source region.

## Evidence

- Delivery PR #3345 merged as squash commit
  `68d6e5d8c7d1564a9bb4839e7bbb4fd201190333`.
- Delivery branch commit `fa0ebb513` fixed bounded nested operand span parsing.
- Implementation changed
  `src/Asynkron.JsEngine/Execution/UnifiedBytecode/UnifiedBytecodeCompiler.cs`
  and
  `src/Asynkron.JsEngine/Execution/UnifiedBytecode/UnifiedBytecodeProductionEligibility.cs`
  to add bounded nested span walkers and mirrored compiler append helpers.
- Focused proof added
  `Evaluate_NestedBinaryConditionalOperandSpan_AcceptsOwnedOpcodes`,
  `Evaluate_NestedUnaryBinaryOperandSpan_AcceptsOwnedOpcodes`,
  `Evaluate_NestedBinaryCallOperandSpan_RemainsDeclined`, and
  `Evaluate_TypeOfUnaryConditionalOperand_AcceptsOwnedOpcodes`.
- Build-stage verification recorded
  `UnifiedBytecodeProductionEligibilityTests` with 619 passed,
  `UnifiedBytecodeProductionInvocationTests` with 571 passed,
  `ExpressionProgramCoverageMapTests` with 15 passed, `rtk git diff --check`
  clean, and the AST runner seam scan returning no matches.
- Learn-stage ADR allocation note: local `rtk faktorial-api adr-next` was not
  present in this worker (`No such file or directory`), so this pass used the
  runtime allocator endpoint `POST /api/adrs/next`, which returned
  `{"adr_id":359}`.

## Related

- `docs/rules/unified-bytecode-prototypes.md`
- `docs/unified-bytecode-expansion-contract.md`
- `docs/plans/bytecode-burndown-checklist.md`
- ADR 0305:
  `docs/adrs/0305-admit-embedded-optional-read-operands-in-control-expression-programs.md`
