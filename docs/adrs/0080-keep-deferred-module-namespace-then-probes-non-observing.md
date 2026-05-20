# ADR 0080: Keep deferred module namespace then probes non-observing

## Status

Accepted

## Context

Issue #1048 / PR #1284 fixed a deferred module namespace TDZ regression around
ordinary thenable probing. The failing shape exported a binding named `then`
through a self-re-export cycle, imported the namespace with `import defer * as
ns`, and then passed that deferred namespace object to `Promise.resolve(ns)`.

The previous module namespace access path treated the string key `then` like an
ordinary namespace export lookup. For deferred namespace objects that lookup
called the live export binding resolver and then the TDZ guard before the
deferred module had evaluated. Promise thenable detection is only checking
whether the object exposes a callable `then`; it must not force evaluation or
turn an uninitialized deferred export into an early `ReferenceError`.

The same owner surface also has real module namespace internals. Ordinary
namespace reads, property descriptor reads, and self-re-export internals still
need to preserve TDZ behavior for actual exported names. A broad "skip TDZ for
namespace access" repair would hide real module errors.

## Decision

Deferred module namespace access treats the `then` probe as non-observing for
binding lookup purposes.

`ModuleNamespace.TryGetProperty` and
`ModuleNamespace.GetOwnPropertyDescriptor` short-circuit deferred `then` probes
before resolving live export bindings or running the namespace TDZ guard. The
probe returns no own property value/descriptor, so Promise thenable detection
can proceed without reading the deferred export.

Ordinary namespace internals still resolve exports and call the TDZ guard. The
self-re-export path remains observable as JavaScript TDZ behavior for real
namespace operations such as direct property access and own-property checks.

## Consequences

- Future module namespace repairs must distinguish Promise/thenable probing
  from real namespace export observation.
- Do not fix deferred namespace `then` failures by weakening TDZ checks for all
  namespace string-key lookups.
- Focused coverage should include both sides of the boundary: a deferred
  `then` probe must not throw before evaluation, while self-re-exported
  namespace internals must still throw for uninitialized exports.
- If additional thenable-probe surfaces are added, they should reuse the same
  non-observing boundary rather than duplicating binding-resolution shortcuts.
