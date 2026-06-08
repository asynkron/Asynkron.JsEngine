# ADR 0369: Keep B36 private class declaration proof split by member state

## Status

Accepted.

## Context

Issue
`planitem-planitem-gh3377-rebaseline-the-finite-bytecode-retirement-inventory-conv-f62b329733`
and delivery PR #3432 rebaselined the finite bytecode retirement inventory for
B36 private class declarations in resumable generator bodies.

B36 already admits direct root class declarations through `DeclareClass` after
the resumable invoker materializes the body environment. The inventory needed a
more exact private-member boundary: a direct root class declaration with
noncapturing private instance methods or accessors can route on the existing
resumable production path, but private fields, private static members, computed
neighbors, and private bodies that capture resumable activation slots are
different class-definition state problems.

Those neighboring shapes may still compute correctly through fallback, but a
fallback result is not proof that the production route owns the matching
private-name, field-initializer, static-member, computed-name, or captured-body
semantics.

## Decision

Keep B36 private class declaration proof split by private member state.

- Direct root private instance method and accessor declarations may be admitted
  only when they avoid `extends`, fields, static members, computed neighbors,
  and resumable activation-slot captures in private bodies.
- Private fields remain open B36 eligibility/no-route proof until the
  class-declaration route owns private field initialization and branding state
  for that family.
- Private static members remain open until static private declaration state is
  owned separately from instance method/accessor descriptors.
- Private/computed mixes remain open because the computed public neighbor can
  carry call dependencies that are not evidence for private-member ownership.
- Private member bodies that capture resumable activation slots remain open
  until the broader class-definition environment bridge owns that lifetime.
- Manifest rows must encode admitted private instance methods/accessors as
  runtime proof with a resumable production route log, and encode the remaining
  private-field/static/computed/capturing neighbors as open eligibility rows.

## Consequences

- B36 remains a mixed lane rather than a blanket private class-declaration
  admission.
- Future B36 rebaselines must not collapse private fields, static private
  members, computed neighbors, or activation-capturing private bodies into the
  admitted noncapturing private instance method/accessor subset.
- A future route-widening slice can flip one open row only by adding focused
  positive production-route proof plus nearby no-route proof for still-unowned
  private-member neighbors.

## Evidence

- ADR allocation note: local `rtk faktorial-api adr-next` was unavailable in
  this worker (`No such file or directory`), so this learn pass used the
  runtime allocator endpoint `POST /api/adrs/next`, which returned
  `{"adr_id":369}`. The prefix `0369` was checked free before writing.
- Delivery PR #3432 merged as commit
  `df7c78a3fb64e91c20ca216fd6b88171fc0f0c39`.
- Delivery changed:
  - `docs/plans/bytecode-proof-manifest.json`
  - `docs/unified-bytecode-expansion-contract.md`
- The delivery added admitted runtime manifest rows:
  - `B36-class-declaration-private-instance-method-routes`
  - `B36-class-declaration-private-instance-accessor-routes`
- The delivery added open eligibility manifest rows:
  - `B36-class-declaration-private-instance-field-declines`
  - `B36-class-declaration-private-instance-method-captures-activation-declines`
  - `B36-class-declaration-private-static-method-declines`
  - `B36-class-declaration-private-instance-method-computed-neighbor-declines`

## Related

- `docs/plans/bytecode-proof-manifest.json`
- `docs/unified-bytecode-expansion-contract.md`
- `docs/rules/unified-bytecode-prototypes.md`
- ADR 0364:
  `docs/adrs/0364-keep-class-static-block-ir-fallback-classified-by-production-decline.md`
