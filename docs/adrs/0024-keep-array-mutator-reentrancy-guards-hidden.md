# ADR 0024: Keep array mutator reentrancy guards hidden

## Status

Accepted

## Context

Issue #806 / PR #999 fixed the Test262
`Intl.NumberFormat` `constructor-locales-hasproperty` fixture after
`Array.prototype.push` used a JavaScript-visible property named `__inPush__` as
its native reentrancy guard.

The fixture records locale-list `HasProperty` lookups by calling
`actualLookups.push(prop)` from a proxy trap. Because `push` wrote `__inPush__`
onto that same array, later JavaScript enumeration could observe the helper
property and the Intl test failed for the wrong reason. The same visible-marker
pattern existed in `pop`, `shift`, and `unshift`.

`splice` and `reverse` already used private `HashSet<IJsPropertyAccessor>`
state keyed by reference identity, so the project had a local hidden-state
pattern for this exact native guard problem.

## Decision

Keep Array prototype mutator reentrancy guards in native hidden state, not on
the receiver as JavaScript properties.

For array mutators and other built-ins that need recursion guards:

1. store guard state in private runtime fields keyed by the receiver/accessor;
2. use reference equality when the receiver identity is the recursion boundary;
3. always clear guard state in `finally`; and
4. do not create helper property names such as `__inPush__` on user-visible
   objects, arrays, proxies, or array-like receivers.

## Consequences

- Native recursion guards cannot affect `HasProperty`, `Get`, `Set`,
  enumeration, proxy traps, or array length behavior unless the ECMAScript
  algorithm explicitly performs those observable operations.
- Future array-mutator work should reuse the hidden guard pattern from
  `push`, `pop`, `shift`, `unshift`, `splice`, and `reverse` instead of
  introducing JavaScript marker properties.
- Focused regressions should include fixtures where callbacks or proxy traps
  mutate arrays that are later enumerated or queried for properties.
- This ADR is caused by issue #806 / PR #999 and is enforced by
  `.claude/rules/js-spec-property-access.md`.
