# ADR 0367: Keep B24h constructor-body activation captures open

## Status

Accepted

## Context

Issue
`planitem-planitem-planitem-gh3377-rebaseline-the-finite-bytecode-retirement-inven-7a98df1ce7`
and delivery PR #3417 rebaselined the finite bytecode retirement inventory after
the B24h class-expression ledger had overstated one admitted route.

B24h already admits several computed class-expression neighbors through
`LoadClassLiteral` when their environment ownership is proven:

- computed member names that avoid activation slots or use owned activation
  operations;
- computed member bodies that capture resumable activation slots;
- computed field initializers that read or capture resumable activation slots;
- selected computed `super`, private-neighbor, static-block, and direct-eval
  literal slices.

The rebaseline found that constructor-body activation captures are a different
class-definition lifetime problem. A constructor such as
`constructor() { this.value = current; }` can be invoked after the resumable
binding mutates. The current class-literal materialized body-environment route
proves member bodies and field initializer closures, but it does not yet prove
the broader class-definition environment bridge needed by that constructor
body at construction time.

## Decision

Keep public computed class-expression constructor bodies that capture resumable
activation slots as an open B24h boundary until a future class-definition
environment slice owns that lifetime end to end.

- The proof manifest row is
  `B24h-computed-member-constructor-captures-activation-declines`.
- Eligibility must decline the shape before VM entry with
  `UnsupportedPlanShape` and a reason containing
  `broader class-definition environment bridge`.
- Runtime proof should continue to check that the fallback computes correctly
  without logging the resumable generator production fast path for that shape.
- Adjacent admitted rows for member-body captures, field-initializer captures,
  computed `super`, private-neighbor, and static-block slices stay separate;
  they are not evidence that constructor-body activation captures are owned.

Future B24h route widening may flip this row only when the checklist,
`docs/bytecode-progress.md`, and
`docs/plans/bytecode-proof-manifest.json` move together with focused positive
route proof and nearby no-route proof for still-unowned class-definition
neighbors.

## Consequences

- B24h remains a finite mixed lane rather than a blanket computed-class
  admission.
- Rebase and proof-manifest agents must not collapse constructor-body captures
  into the already-admitted member-body or field-initializer materialized
  environment rows.
- The fallback route remains the correctness owner for this source shape until
  class-definition construction-time environment ownership is explicitly
  proven.

## Evidence

- ADR allocation note: local `rtk faktorial-api adr-next` was not used for this
  learn pass because the prompt required HTTP/API context first; the runtime
  allocator endpoint `POST /api/adrs/next` returned `{"adr_id":367}`. The
  prefix `0367` was checked free before writing.
- Delivery PR #3417 merged as commit
  `93f7534ffee748da290b475d945eb791440f1c0a`.
- Delivery changed:
  - `docs/bytecode-progress.md`
  - `docs/plans/bytecode-proof-manifest.json`
  - `tests/Asynkron.JsEngine.Tests/UnifiedBytecodeResumableClassExpressionTests.cs`
- The delivery replaced the manifest/runtime claim
  `B24h-computed-member-constructor-captures-activation-routes` with the open
  eligibility row
  `B24h-computed-member-constructor-captures-activation-declines`.

## Related

- `docs/plans/bytecode-burndown-checklist.md`
- `docs/plans/bytecode-proof-manifest.json`
- `docs/bytecode-progress.md`
- `docs/rules/unified-bytecode-prototypes.md`
- ADR 0360:
  `docs/adrs/0360-admit-direct-activation-calls-in-computed-class-names.md`
- ADR 0364:
  `docs/adrs/0364-keep-class-static-block-ir-fallback-classified-by-production-decline.md`
