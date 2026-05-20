# ADR 0039: Keep Intl constructor options ToObject coercion

## Status

Accepted

## Context

Issue #822 / PR #1110 fixed the Test262
`Intl402Tests.RelativeTimeFormat_constructor_constructor` failures for
`options-toobject*.js`. The failing fixtures exposed that
`Intl.RelativeTimeFormat` was treating a primitive `options` argument as an
invalid options object instead of following the ECMA-402 constructor path:
`undefined` means no options object, `null` throws before property reads, and
non-null primitives are boxed through `ToObject` before options such as
`localeMatcher`, `numberingSystem`, `style`, and `numeric` are read.

The repair kept the behavior in the shared Intl options helper by changing the
RelativeTimeFormat constructor to call
`IntlOptionHelpers.GetOptionsObject(..., useToObject: true)`. That matched the
existing NumberFormat and DateTimeFormat precedent, reused the existing
primitive boxing accessor, and preserved the constructor's observable option
read order.

## Decision

Intl constructors that accept an `options` parameter must use the
ToObject-compatible options path when ECMA-402 requires it.

For future Intl constructor work:

1. treat `undefined` options as absent without reading option properties;
2. reject `null` at the options-object coercion step;
3. box non-null primitive options through the shared ToObject accessor before
   reading option properties;
4. preserve each constructor's spec option read order after coercion; and
5. prove the behavior with the focused Test262 constructor method group when
   the issue comes from `options-toobject` fixtures.

## Consequences

- New Intl constructors should reuse `IntlOptionHelpers.GetOptionsObject` with
  the correct ToObject mode instead of adding constructor-local primitive
  guards.
- Review should compare adjacent Intl constructors for shared ECMA-402 option
  coercion precedent before treating a primitive options failure as
  constructor-specific.
- Focused proof for this class should include primitive options, `null`, and
  `undefined` so boxing and nullish rejection cannot drift independently.
- This ADR is caused by issue #822 / PR #1110 and complements
  `.claude/rules/ecmascript-abstract-operations.md`.
