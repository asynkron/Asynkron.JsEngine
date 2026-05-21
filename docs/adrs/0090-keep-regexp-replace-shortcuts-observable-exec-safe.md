# ADR 0090: Keep RegExp replace shortcuts observable-exec safe

## Status

Accepted

## Context

Issue #1335 / PR #1355 fixed a RegExp crash and performance bucket covering
RegExp literals, character class escapes, and the runtime bridge. The merged
delivery added narrow runtime shortcuts for legacy global non-whitespace
replacement and anchored non-word class escape matching, with focused internal
RegExp coverage and representative Test262 proof.

The final repair in the delivery was small but important: the
`/\S+/g` string replace shortcut initially treated a first match anywhere in the
input as a whole-input replacement shape. That broke normal replace prefix
semantics for inputs such as `" a".replace(/\S+/g, "X")`, which must preserve
the leading whitespace and return `" X"`.

The review pass also checked that the shortcut declines when RegExp execution is
observable through an own `exec` override or a mutated `RegExp.prototype.exec`.
Those cases must use the ordinary `RegExpExec` loop so user code observes the
right call count, match array shape, `lastIndex`, and replacement position.

## Decision

RegExp replace performance shortcuts must stay behind observable-exec guards and
must preserve replacement assembly semantics.

For the legacy global non-whitespace shape, the shortcut may apply only when:

1. the receiver resolves to the engine's internal `JsRegExp` instance for the
   exact `\S+` pattern with exactly the `g` flag;
2. the replacement is a plain string with no `$` substitution tokens;
3. neither the instance nor `RegExp.prototype` has an observable custom `exec`
   hook relative to the captured default `exec`;
4. the implementation updates `lastIndex` and legacy RegExp statics as the
   ordinary execution path would for accepted matches; and
5. whole-input early return is used only when the first accepted match starts at
   index `0`.

Any pattern, flag set, replacement, receiver, or mutation outside that envelope
must decline to the normal `RegExp.prototype[@@replace]` implementation.

## Consequences

- RegExp replace fast paths are allowed as performance repairs, but they are not
  a replacement for `RegExpExec`.
- Prefix and gap preservation are part of the proof, not an incidental string
  formatting detail.
- Tests for future replace shortcuts should include own `exec` override,
  prototype `exec` override, `lastIndex` behavior, statics/match metadata, and a
  nonzero first-match index case.
- This complements `.claude/rules/ecmascript-regexp-runtime-bridges.md` and the
  earlier RegExp runtime-bridge ADRs.
