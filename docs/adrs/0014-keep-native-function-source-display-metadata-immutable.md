# ADR 0014: Keep native function source display metadata immutable

## Status

Accepted

## Context

Issue #788 fixed the focused Test262 `Function_prototype_toString` failures for
`built-ins/Function/prototype/toString/built-in-function-object.js` and
`setter-object.js`.

Issue #1378 exposed the same metadata boundary from the Test262 base-realm
snapshot path. `BaseRealmSnapshot` creates cloned `HostFunction` instances for
cached realms; if that clone only copies handlers, properties, constructor
flags, and realm state, it silently drops the private native display metadata
that `Function.prototype.toString` depends on. The observable failure is the
same built-in native source shape, but the bug is in an engine-owned clone path
rather than in the original built-in registration path.

`Function.prototype.toString` must return exact source text for user-defined
functions when source is available, and NativeFunction-shaped source for
built-in and host functions. The first repair made host functions derive native
source from their observable `name` property. That satisfied the broad Test262
method group, but review caught two correctness traps:

- JavaScript can redefine a built-in function object's public `name` property,
  so using it lets user code forge native source text.
- Bracketed native display names such as `[Symbol.iterator]` need a narrow
  grammar guard; malformed nested or extra bracket forms such as `[a]]` must
  fall back to the anonymous native source form.

The generator already knows each built-in method, accessor, symbol member,
constructor member, global host function, and compatibility stub display name
when it creates the `HostFunction`. That creation-time point is the stable
metadata boundary.

## Decision

Keep native source display metadata as private creation-time `HostFunction`
state. `Function.prototype.toString` should prefer exact
`ICallableMetadata.SourceReference` text for user-authored functions, then ask a
`HostFunction` for its native source string, and only then fall back to the
generic anonymous native source form for other callable objects.

Generated host functions must stamp their intended native source display name
when they are created. Do not reconstruct this display name later from mutable
JavaScript-visible properties such as `name`.

Engine-owned clone, snapshot, or realm-reuse paths that manufacture replacement
`HostFunction` instances must copy this private native-source display metadata
from the original function. Treat the metadata as part of the host function's
internal callable identity, not as an ordinary property descriptor that can be
recovered by cloning the JavaScript-visible properties object.

Validate native display names before rendering them. Plain names must be valid
identifier-like names, accessor display names must keep `get` / `set` separate
from the property name, and bracketed names must contain exactly one outer
bracket pair with non-empty content. Unsafe names render as
`function () { [native code] }`.

## Consequences

- Future host-function creation paths need to set native display metadata at
  creation time when they expect named native source output.
- Mutating a built-in function object's public `name` property must not affect
  `Function.prototype.toString` output.
- Bracketed symbol-style native names require focused guard coverage, including
  malformed nested or extra bracket forms.
- Source-reference handling remains the first branch so user-defined functions
  and accessors keep exact source text when it is available.
- Realm snapshot and clone paths must preserve native source display metadata
  explicitly; cloning the properties object is insufficient because the
  metadata is intentionally private engine state.
- Regression proof should include direct local coverage for forged `name`
  metadata, malformed bracketed display names, and the focused
  `Name=Function_prototype_toString` Test262 group before widening.
- This ADR is caused by issue #788 / PR #963.
- Snapshot-clone preservation was reinforced by issue #1378 / PR #1380.
