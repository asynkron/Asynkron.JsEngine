# ADR 0029: Keep host-function prototype removal internal

## Status

Accepted

## Context

Issue #816 / PR #1016 fixed the focused `parseInt` / `Number.parseInt`
prototype-shape failure. The generated host-function registration path honored
`DeletePrototype` by emitting:

```csharp
function.Properties.Delete("prototype");
```

That looked like the correct JavaScript-level operation, but `HostFunction`
constructors can create a non-configurable own `prototype` data property. A
normal delete respects configurability and therefore left the own property in
place. Test262 then observed `parseInt.hasOwnProperty("prototype")`, even
though global `parseInt` and `Number.parseInt` are not constructor functions
and must not expose an own `prototype` property.

`HostFunction` already contains an internal `RemovePrototypeDataProperty` path
that escalates from ordinary deletion to `ForceDeleteOwnProperty` when changing
the engine-owned callable shape. The source generator's `DeletePrototype`
metadata is the same kind of internal shape correction, not user code executing
`delete`.

## Decision

Treat generated host-function `DeletePrototype` as an engine-owned observable
shape operation. When source-generated built-ins or globals mark a host function
with `DeletePrototype`, emit `ForceDeleteOwnProperty("prototype")` instead of a
normal descriptor-respecting delete.

Keep ordinary JavaScript delete semantics in the runtime property operations.
Only internal host-function construction and generated registration code may use
the force-delete path to remove a constructor-created `prototype` data property
that should never be observable on a non-constructor built-in function.

## Consequences

- Future source-generator changes that remove generated host-function
  `prototype` properties must use the internal force-delete operation, not
  ordinary JavaScript delete semantics.
- `ForceDeleteOwnProperty` remains an internal runtime escape hatch for
  engine-owned shape setup and cleanup; it must not become the default
  implementation of JavaScript `delete`.
- Regression coverage for this class should check both the global function and
  aliases such as `Number.parseInt === parseInt`, because copied/aliased
  built-ins preserve the original function object's observable shape.
- Focused proof should include the narrow internal regression and the owning
  Test262 fixture or method group before widening.
- This ADR is caused by issue #816 / PR #1016.
