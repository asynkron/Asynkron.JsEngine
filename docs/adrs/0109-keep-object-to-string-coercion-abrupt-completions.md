# ADR 0109: Keep object ToString coercion abrupt completions

## Status

Accepted

## Context

Issue #1723 listed recent Test262 regressions across several independent
surfaces. The delivery slice for PR #1726 stayed on the clearest cluster:
`Array.prototype.join` separator coercion and `Array.prototype.toString`
delegation to `join`.

The failing Array fixtures made `ToString` observable through object separator
values whose `toString`, `valueOf`, or `Symbol.toPrimitive` paths either threw
or returned another object. The legacy `JsValueExtensions.ToJsString` object
carrier path called `ToPrimitive(..., string)` but then treated an object result
as `"[object Object]"`. It also needed to preserve a pending throw from the
active `EvaluationContext` immediately after `ToPrimitive` ran.

That fallback was not just a display choice. For ECMAScript `ToString`, an
object-to-primitive failure is a JavaScript `TypeError`, and a user-code throw
during coercion must remain the active abrupt completion. Returning a host
string hides the observable operation order that Array join and toString rely
on.

## Decision

Object-to-string coercion helpers that can execute JavaScript user code must
keep three outcomes distinct after `ToPrimitive` with string hint:

1. if the active context is throwing, rethrow the pending JavaScript completion;
2. if the primitive result is still an object, throw a JavaScript `TypeError`;
3. otherwise continue string conversion from the returned primitive.

Do not repair this class by falling back to `"[object Object]"`, host
`Convert.ToString`, or a context-free helper once `ToPrimitive` has been
entered. The object fallback may be a host display convention only for paths
that have proven they are not implementing ECMAScript `ToString`.

Array `join` and `toString` proofs should include observable separator coercion
fixtures, not only primitive separators or happy-path arrays.

## Consequences

- Future `ToJsString`, `ToJsStringForArray`, `ToPropertyKey`, or adjacent
  abstract-operation work should pass and inspect the active
  `EvaluationContext?` whenever object coercion can call user code.
- Returning `"[object Object]"` from a failed object-to-primitive conversion is
  a semantic regression for ECMAScript `ToString`, even if it matches a common
  host display string.
- Focused proof for this incident was the Array join/toString Test262 cluster:
  `Array_prototype_join` `S15.4.4.5_A3.*` and `Array_prototype_toString`
  `S15.4.4.*`.
- This ADR is caused by issue #1723 / PR #1726 and complements the root
  `.claude/rules/ecmascript-abstract-operations.md` rule.
