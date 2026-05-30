# ADR 0291: Admit simple computed keys in unified bytecode span scanner

## Status

Accepted

## Context

Issue #2742 widened unified-bytecode production routing to admit **object literal
computed keys with simple (identifier or literal) key expressions** in
call-argument position and as binary-expression RHS operands.

Before this slice, `TryMeasureSimpleObjectLiteralSpan` (eligibility) and
`TryAppendSimpleObjectLiteralSpan` (compiler) only admitted the static-property
shape `[simple-value-operand, DefineObjectProperty]`. When a computed-key property
appeared as a call argument — e.g., `f({ [k]: v })` — the eligibility scan read
the key operand as if it were a value operand, then rejected the following
`DefineComputedObjectProperty` (or the actual value operand) as a non-admitted op,
causing the span scanner to decline and the call to fall through to the prototype
path.

The top-level eligibility scanner (`TryFindExpressionDecline`) already admitted
`DefineComputedObjectProperty` without restriction, so object literals in
assignment or return context with computed keys were already eligible. The span
helpers were the only remaining gap.

## Decision

Extend both span helpers to admit the four-op computed-property sequence
`[simple-key-operand, ResolvePropertyKey, simple-value-operand, DefineComputedObjectProperty]`
in addition to the existing two-op static-property pair.

The `ResolvePropertyKey` op is always emitted by the expression program compiler
between the key expression and the value expression for computed properties
(`{ [expr]: value }`), so the full four-op sequence is the canonical compiled form.

### Admission boundary for computed keys

Only computed keys that satisfy `IsSimpleOperand` (eligibility) or pass
`TryAppendSimpleOperandLoad` (compiler) are admitted. This limits admitted key
forms to:
- `LoadIdentifier` (activation-slot-resolved, non-dynamic)
- `LoadLiteral` (string, number, boolean, null, undefined)

Complex key expressions — binary ops, call expressions, `new`, template literals,
optional chains, and anything not recognized by `IsSimpleOperand` — cause the
computed-key sequence to fail the `IsSimpleOperand(firstOp)` check at the key
position (or produce a non-`ResolvePropertyKey` op as secondOp), and the span
scanner terminates normally, letting the call fall through to the prototype path.

`AllowNameInference` on `DefineComputedObjectProperty` is required to be `false`.
`{ [expr]: function() {} }` already sets `AllowNameInference = true` and continues
to be handled by the top-level eligibility scanner (assignment/return context) or
declined by the span scanner (call-argument context where name inference interacts
with the call boundary). This is a conservative choice: admitting name-inference
computed keys as call arguments would require propagating the function name into
the slot name, which is out of scope for this slice.

### Stack order

The VM pops value then key for `DefineComputedObjectProperty`:
```
computedObjectPropertyValue = stack[--stackPointer];
computedObjectPropertyKey   = stack[--stackPointer];
```
The expression program emits ops in the order `[key-load, value-load,
DefineComputedObjectProperty]` so key is pushed first and sits below value on the
stack. The span compiler mirrors this: it calls `TryAppendSimpleOperandLoad` for
the key first (while reading `firstOp`), then for the value (while reading
`secondOp`), then emits the `DefineComputedObjectProperty` boundary instruction.

### Algorithm in `TryMeasureSimpleObjectLiteralSpan` / `TryAppendSimpleObjectLiteralSpan`

```
while (i < end) {
    firstOp = program[i];
    if (!IsSimpleOperand(firstOp)) break;            // scan done
    i++;

    secondOp = program[i];
    if (secondOp.Kind == DefineObjectProperty) {
        // Static property: firstOp = value.
        check !IsPrivateName && !AllowNameInference
        i++;
    } else if (IsSimpleOperand(secondOp)) {
        // Computed property: firstOp = key, secondOp = value.
        i++;
        computedDefineOp = program[i];
        check Kind == DefineComputedObjectProperty && !AllowNameInference
        i++;
    } else {
        decline
    }
}
```

## Consequences

- `f({ [k]: v })`, `f({ ["name"]: v })`, `f({ [0]: v })` are now admitted to
  the production unified-bytecode fast path.
- Mixed static and computed properties — `f({ a: x, [k]: y })` — are also
  admitted, since the scan handles each property independently.
- Complex computed keys (`f({ [a+b]: v })`, `f({ [fn()]: v })`) remain declined;
  the span scanner breaks on the non-simple key op and the call falls through.
- The existing `TryFindExpressionDecline` top-level scanner is unchanged;
  `DefineComputedObjectProperty` at assignment/return context was already admitted.
- No new VM opcodes or instruction formats are required; the existing
  `DefineComputedObjectProperty` opcode and VM handler cover this case.
