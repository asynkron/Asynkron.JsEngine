# ECMAScript RegExp Runtime Bridges

When fixing `JsRegExp` behavior by translating ECMAScript RegExp syntax or
semantics into .NET `Regex` constructs, preserve the JavaScript-visible capture
model explicitly.

## Rules

1. Treat generated .NET-only regex syntax as implementation detail. If a fix
   emits conditionals, helper groups, lookarounds, or other structural
   parentheses, update any normalized-pattern scanner that could otherwise count
   those constructs as ECMAScript captures.
2. Capture-reset maps, group reorder maps, duplicate-name maps, and group
   objects must be indexed by JavaScript-visible capture slots, not incidental
   .NET helper groups.
3. For backreference, quantified assertion, and nullable quantifier fixes, add
   or update focused internal tests that assert the exposed capture values and
   indexes. Match success alone is not enough proof.
4. Prefer narrow compatibility shims when .NET backtracking behavior differs
   from ECMAScript and captures are observable. Do not rewrite a broader pattern
   family unless the proof covers capture values for nearby variants.
5. For Test262 RegExp burn-down work, start with the exact affected fixture
   filters and only remove regression-pack entries after both snapshot
   parameterizations are green.
6. For custom RegExp parser/execution shims, parse only syntax whose ECMAScript
   semantics are implemented exactly. Unsupported atoms, escapes, or groups
   should make the shim decline and fall back to the normal runtime path instead
   of being guessed as literals.
7. For positive-lookbehind numeric-backreference shims, preserve all observable
   match metadata: match index, capture text, `/d` capture spans, legacy
   `RegExp` statics, and assertion zero-width behavior under relevant flags.

## Why

Issue #818 / PR #1088 fixed a RegExp Test262 batch where ECMAScript semantics
had to be layered over .NET `Regex`. The final review blocker came from
generated conditional backreferences like `(?(1)\1|)`: the conditional test
`(1)` is not a JavaScript capture, but the zero-width reset-map scanner
initially counted it as one and could reset the wrong group to `undefined`.
The review repro `/(?:(?=(a)\1))?(b)/.exec("ab")` showed that group 2 could be
lost even though the match succeeded.

Future agents should repair both the generated .NET pattern and the metadata
readers that interpret it, because ECMAScript RegExp observability includes the
capture slots returned by `exec`.

Issue #819 / PR #1091 added a narrow custom positive-lookbehind numeric
backreference path for Test262 `RegExp_lookBehind` fixtures that .NET `Regex`
could not model directly. Review found that the shim also had to preserve `/d`
capture spans and legacy `RegExp` statics, treat unescaped `^` and `$` as
zero-width assertions, and decline unsupported escapes such as `\b` / `\B`
instead of literalizing them. Future custom shims should stay narrow and
fall back whenever they cannot prove exact ECMAScript semantics.
