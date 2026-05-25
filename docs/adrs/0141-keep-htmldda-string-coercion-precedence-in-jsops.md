# ADR 0141: Keep HTMLDDA string coercion precedence in JsOps

## Status

Accepted

## Context

Issue `autrun-dirtf04x6gtk-01ca431d93` / PR #1907 migrated the array string
coercion path from the legacy `object?` extension helper into the `JsValue`
native `JsOps.ToJsString` helper. The migration correctly moved object
coercion, active-context throw propagation, and invariant primitive formatting
into the runtime helper, then marked the old array-specific object overload as
obsolete for core paths.

Review found one ordering regression in the extracted object-value helper:
HTMLDDA-like values also implement callable or accessor interfaces, but legacy
string coercion treated `IIsHtmlDda` as the special `undefined` value before
the callable/accessor branches. With the branch below those interfaces,
`JsOps.ToJsString(JsValue.FromObjectUnsafe(new HtmlDdaValue()))` returned native
function text instead of `"undefined"`.

This is not a local display formatting detail. HTMLDDA is a web-compat exotic
that participates in ECMAScript abstract operations as a nullish-like special
case while still having object/callable shape. Any helper extraction that
switches on host interfaces can accidentally make the ordinary object shape win
over the special ECMAScript classification.

## Decision

When extracting or migrating object string coercion into `JsValue`/`JsOps`
helpers, classify `IIsHtmlDda` before callable, accessor, host function, or
generic object fallback branches. The special value must stringify as
`"undefined"` in the core `ToString` path and in array string coercion paths
that delegate to it.

Keep the branch order explicit in the helper instead of relying on interface
ordering, host `ToString()`, or native function display text. If a future
refactor splits object coercion again, the HTMLDDA proof belongs with the
shared object-value helper, not only with the immediate caller that exposed the
regression.

## Consequences

- Core string coercion helper migrations must preserve legacy exotic-object
  precedence, not just primitive formatting and abrupt-completion behavior.
- Array `join`/stringification and any other callers routed through
  `JsOps.ToJsString` inherit the same HTMLDDA semantics from one shared helper.
- Focused proof for this incident was the review-requested check that
  `JsOps.ToJsString(JsValue.FromObjectUnsafe(new HtmlDdaValue()))` returns
  `"undefined"` after the helper migration.
- This ADR complements
  `.claude/rules/jsvalue-core-values.md` and
  `docs/adrs/0109-keep-object-to-string-coercion-abrupt-completions.md`.
