# ADR 0121: Keep descriptor delete result semantics strictness-owned

## Status

Accepted

## Context

Issue #1751 / PR #1790 closed a Test262 compliance-gap cluster for
`language/expressions/delete/11.4.1-4-a-*.js` cases. The reported failures were
crashes around descriptor-backed property deletion, not a parser feature gap.
The final delivery added focused internal regressions for configurable data
properties, configurable accessor properties, strict non-configurable accessor
deletion, and sloppy non-configurable accessor deletion.

The important semantic boundary is the result of the ordinary property-delete
operation. Configurable own data and accessor descriptors are removable and
must return `true`; non-configurable own descriptors are not removable and must
return `false`. Strict mode interprets that failed delete as a JavaScript
`TypeError`, while sloppy mode exposes the `false` completion without throwing.

## Decision

Keep descriptor-backed `delete` semantics owned by the runtime property-delete
result and the active strictness:

1. Ordinary JavaScript `delete obj.prop` and `delete obj[key]` must route
   through descriptor-aware property deletion rather than a force-delete helper.
2. Configurable data and accessor descriptors must be removed and return
   `true`.
3. Non-configurable descriptors must remain present and return `false`.
4. Strict-mode delete must convert a failed descriptor delete into a JavaScript
   `TypeError`; sloppy-mode delete must remain non-throwing and return `false`.
5. Regression proof for this area must pair descriptor shape with strictness,
   because data/accessor and strict/sloppy paths can fail independently.

This decision does not change internal engine-owned shape mutation such as
host-function prototype removal. Those internal escape hatches remain separate
from ordinary JavaScript `delete`.

## Consequences

- Future delete fixes should start at the property-delete operation and strict
  completion handling before changing parser or expression-lowering behavior.
- Descriptor configurability remains the source of truth for ordinary delete
  success; accessors are not a special remove-by-getter path.
- Strict/sloppy delete tests should assert both the completion value or thrown
  JavaScript error and the final own-property presence.
- Internal force-delete helpers must not be reused to make ordinary JavaScript
  descriptor deletes pass.

## Related

- `.claude/rules/js-spec-property-access.md`
- `docs/adrs/0018-keep-delete-super-reference-check-before-property-key.md`
- `docs/adrs/0029-keep-host-function-prototype-removal-internal.md`
