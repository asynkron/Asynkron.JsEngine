# ADR 0033: Keep RegExp .NET bridge capture metadata explicit

## Status

Accepted

## Context

Issue #818 / PR #1088 fixed a batch of Test262 `BuiltInsTests.RegExp`
failures that came from bridging ECMAScript RegExp behavior onto .NET `Regex`.
The delivery added compatibility handling for unmatched numeric
backreferences, legacy word-boundary escapes, nullable quantifier progress,
unicode ignore-case fold pairs, and quantified zero-width assertion capture
resets.

The final review blocker exposed the durable risk. The implementation generated
.NET conditional backreferences such as `(?(1)\1|)` to model an ECMAScript
backreference that can match empty when its group is unmatched. A later scanner,
`BuildZeroWidthQuantifierResetMap`, walks the normalized .NET pattern to decide
which ECMAScript captures must be reset after quantified zero-width assertions.
That scanner initially treated the `(1)` conditional test as if it were a real
capture group and could close the outer conditional group early. The result was
metadata drift: reset markers could land on the wrong JavaScript capture slot,
for example losing group 2 in `/(?:(?=(a)\1))?(b)/.exec("ab")`.

This is not only a parser bug. `JsRegExp` contains several normalized-pattern
passes that use .NET regex constructs as implementation shims while still
exposing ECMAScript capture numbering, group participation, and exec-array
results. Every generated .NET-only construct can become observable if the
metadata scanners count it as JavaScript syntax.

## Decision

Keep RegExp bridge fixes explicit about both syntax domains:

1. Generated .NET-only syntax must be represented in a way that preserves the
   ECMAScript observable capture model.
2. Any scanner over the normalized .NET pattern must recognize generated
   constructs such as conditional tests and must not count their structural
   parentheses as ECMAScript captures.
3. Capture-reset metadata remains a separate compatibility layer over the .NET
   match result. It may use sentinel entries for proven ECMAScript reset cases,
   but those sentinels must be indexed by JavaScript-visible capture slots.
4. Narrow shims are preferred over broad rewrites when .NET backtracking differs
   from ECMAScript and capture observability is involved. The nullable
   quantifier repair intentionally stayed exact-pattern scoped after review
   showed a broader rewrite changed the exposed capture value.

## Consequences

- Future `JsRegExp` changes that generate .NET regex syntax must update the
  metadata scanners in the same patch when the generated syntax contains
  parentheses, named groups, conditionals, or backreferences.
- Review should include repros that assert both match success and capture-slot
  values, not only that a Test262 fixture passes.
- Exact Test262 fixture filters remain the first proof for this class of work,
  followed by focused internal RegExp tests that preserve the bridge contract.
- This ADR complements the root `.claude/rules/ecmascript-regexp-runtime-bridges.md`
  rule caused by issue #818 / PR #1088.
