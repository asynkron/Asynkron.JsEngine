# ADR 0366: Keep A51k1 simple value operand-span inventory separate

## Status

Accepted

## Context

Issue
`planitem-planitem-planitem-gh3377-rebaseline-the-finite-bytecode-retirement-inven-3a439da133`
and delivery PR #3414 rebaselined the finite bytecode retirement inventory after
A51k operand-span work had already admitted a bounded simple value lane.

A51k still has open compiler diagnostics for unsupported simple binary
operators, unsupported simple operands, and call/private/dynamic-neighbor
operands. At the same time, the already-admitted simple value operand-span
helpers are real production VM ownership: binary, unary, logical-control, and
conditional spans can compose already-admitted literal-value spans through
mirrored compiler appenders and eligibility walkers.

Keeping those admitted helper anchors inside the open A51k parent made the
inventory ambiguous. A future rebaseline could read the remaining A51k
diagnostics as covering the admitted helper lane, or could close the helper lane
while accidentally claiming call-target stack, private-name scope, or dynamic
environment ownership that the slice did not prove.

## Decision

Keep the simple value operand-span helper lane as explicit child row `A51k1`.

- `A51k` remains the open parent for unsupported simple binary/operator
  diagnostics, unsupported simple-operand diagnostics, and call/private/dynamic
  neighbor operands.
- `A51k1` owns only the admitted simple value span helpers and mirrored
  measurers:
  `TryAppendSimpleBinaryOperandSpan`, `TryAppendSimpleUnaryOperandSpan`,
  `TryAppendSimpleNestedOperandSpan`,
  `TryAppendBoundedSimpleControlExpressionOperandSpan`,
  `TryMeasureSimpleLiteralValueOperandSpanCore`,
  `TryMeasureSimpleBinaryOperandSpan`, `TryMeasureSimpleUnaryOperandSpan`,
  `TryMeasureSimpleControlExpressionOperandSpan`, and
  `TryMeasureSimpleConditionalExpressionOperandSpan`.
- Conditional consequent/alternate simple-literal-span diagnostics stay under
  A51h, not A51k1.
- Call-target, private-name, and dynamic-name neighbor operands stay outside
  A51k1 until a future slice proves their stack, scope, and environment
  ownership end to end.

Future movement of these anchors must update
`docs/plans/bytecode-burndown-checklist.md`,
`docs/plans/bytecode-proof-manifest.json`, and
`docs/unified-bytecode-expansion-contract.md` together.

## Consequences

- A51k remains visibly open for the unproven operand-span diagnostics instead of
  being confused with already-admitted simple value spans.
- A51k1 can be counted as a closed inventory/proof lane without hiding nearby
  call/private/dynamic-neighbor residue.
- Future operand-span widening should replace A51k source-presence anchors with
  executable route or retired-fallback proof; it should not move unproven
  neighbors into A51k1.
- Rebase conflict resolution can reconcile A51k and A51k1 independently.

## Evidence

- ADR allocation note: local `rtk faktorial-api adr-next` was unavailable in
  this runtime (`No such file or directory`), so this learn pass used the
  runtime allocator endpoint `POST /api/adrs/next`, which returned
  `{"adr_id":366}`. The prefix `0366` was checked free before writing.
- Delivery PR #3414 merged as commit
  `56971e38f3b4ef585205cb8aeab0194f251cc5a1`.
- Delivery changed:
  - `docs/plans/bytecode-burndown-checklist.md`
  - `docs/plans/bytecode-proof-manifest.json`
  - `docs/unified-bytecode-expansion-contract.md`
- The delivery added manifest source-presence rows for the A51k1 helper and
  eligibility walker inventory while keeping A51k open diagnostics separate.

## Related

- `docs/rules/unified-bytecode-prototypes.md`
- `docs/plans/bytecode-burndown-checklist.md`
- `docs/plans/bytecode-proof-manifest.json`
- `docs/unified-bytecode-expansion-contract.md`
- ADR 0359:
  `docs/adrs/0359-admit-nested-simple-operand-spans-through-bounded-recursive-walkers.md`
- ADR 0365:
  `docs/adrs/0365-keep-a51h-literal-container-spans-as-separate-inventory-lane.md`
