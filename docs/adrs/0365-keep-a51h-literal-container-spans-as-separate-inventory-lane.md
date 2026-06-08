# ADR 0365: Keep A51h literal-container spans as a separate inventory lane

## Status

Accepted

## Context

Issue
`planitem-planitem-planitem-gh3377-rebaseline-the-finite-bytecode-retirement-inven-c83a8e6022`
and delivery PR #3408 rebaselined the finite bytecode retirement inventory after
nearby A51h work had moved object methods/accessors and nested operand spans.

A51h had been carrying multiple different literal/span residues:

- method/accessor/private/name-inference object-literal restrictions and
  logical-control operands;
- spread-source and computed object-key span restrictions;
- the array/object/template literal-container appender and measurer helper
  lane itself.

Keeping all of those anchors under one row made rebase and checklist updates
ambiguous. A conflict resolution could easily preserve the old count while
misplacing source-presence anchors, or collapse helper-presence blockers into
spread/computed-key work that does not own the same compiler/eligibility
surfaces.

## Decision

Keep the literal/span inventory split into three explicit lanes:

- `A51h`: non-container residue for method/accessor/private/name-inference
  object-literal restrictions and logical-control operands inside simple
  literal spans.
- `A51h1`: spread-source and computed object-key literal-span restrictions.
- `A51h2`: array/object/template literal-container helper ownership, including
  `TryAppendSimpleArrayLiteralSpan`, `TryAppendSimpleObjectLiteralSpan`,
  `TryAppendSimpleTemplateLiteralSpan`, and matching
  `UnifiedBytecodeProductionEligibility` measurers.

Future inventory rebaselines or route-widening work must update the checklist,
expansion contract, and proof manifest together when moving any of these
anchors. Conditional consequent/alternate span boundaries belong with the
simple operand-span lane once A51k owns that classification, not as generic
A51h catch-all evidence.

## Consequences

- Literal-container helper removal now has its own source-presence proof lane
  instead of being hidden behind spread/computed-key or non-container literal
  rows.
- A51h/A51h1/A51h2 counts can be reconciled independently during rebases.
- Future agents should not mark A51h closed just because spread/computed-key
  or method/accessor neighbors changed; the A51h2 helper anchors must also move
  or disappear.
- Future A51h2 runtime work must touch both compiler appenders and eligibility
  measurers, then update `docs/plans/bytecode-proof-manifest.json`,
  `docs/plans/bytecode-burndown-checklist.md`, and
  `docs/unified-bytecode-expansion-contract.md` in the same slice.

## Evidence

- ADR allocation note: local `rtk faktorial-api adr-next` was unavailable in
  this runtime (`No such file or directory`), so this learn pass used the
  runtime allocator endpoint `POST /api/adrs/next`, which returned
  `{"adr_id":365}`. The prefix `0365` was checked free before writing.
- Delivery PR #3408 merged as commit
  `588703f4fc1d07391a10d98deb18fae5d7884284`.
- Delivery changed:
  - `docs/plans/bytecode-burndown-checklist.md`
  - `docs/plans/bytecode-proof-manifest.json`
  - `docs/unified-bytecode-expansion-contract.md`
- Build-stage verification recorded canonical `make quality`, including
  `rtk git diff --check` and the internal test suite.

## Related

- `docs/rules/unified-bytecode-prototypes.md`
- `docs/plans/bytecode-burndown-checklist.md`
- `docs/plans/bytecode-proof-manifest.json`
- `docs/unified-bytecode-expansion-contract.md`
- ADR 0356:
  `docs/adrs/0356-admit-object-method-literal-spans-through-mirrored-compiler-emission.md`
- ADR 0359:
  `docs/adrs/0359-admit-nested-simple-operand-spans-through-bounded-recursive-walkers.md`
