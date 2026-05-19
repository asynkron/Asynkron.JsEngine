# Host Function Observable Shape

When creating or generating `HostFunction` built-ins, keep engine-owned callable
shape corrections separate from ordinary JavaScript property operations.

## Rules

1. If generated host-function metadata says `DeletePrototype`, remove the own
   `prototype` property with the internal force-delete path, not
   descriptor-respecting JavaScript delete semantics.
2. Do not broaden this into normal `delete` behavior. User-visible property
   deletion must continue to respect configurability and existing ECMAScript
   semantics.
3. Treat `ForceDeleteOwnProperty` as an internal construction/registration
   escape hatch for callable shape setup, similar to `HostFunction`'s own
   prototype-data-property cleanup.
4. When changing built-in function object shape, add focused tests for the
   exact observable property and any aliases that share the same function
   object.

## Why

Issue #816 / PR #1016 fixed global `parseInt` and `Number.parseInt` after the
source generator emitted `Properties.Delete("prototype")` for
`DeletePrototype`. `HostFunction` had created a non-configurable own
`prototype` data property, so ordinary deletion correctly left it in place and
Test262 could observe it with `hasOwnProperty`. The durable rule is that
generated non-constructor built-ins need internal shape cleanup; JavaScript
delete semantics are the wrong abstraction for removing engine-created
non-configurable prototype data properties.

Related ADR: `docs/adrs/0029-keep-host-function-prototype-removal-internal.md`.
