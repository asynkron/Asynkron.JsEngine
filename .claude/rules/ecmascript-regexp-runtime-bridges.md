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
8. For eval-source RegExp literal fast paths, first prove the source is a
   standalone RegExp literal in JavaScript source grammar. Comment prefixes
   (`//`, `/*`) and any ambiguous source shape must decline the fast path and
   fall back to normal script parsing.
9. For RegExp replace shortcuts, prove that the shortcut is not bypassing
   observable `RegExpExec` behavior. Decline when the instance has an own `exec`
   property or `RegExp.prototype.exec` has changed from the captured default.
   Preserve `lastIndex`, legacy RegExp statics, match indexes, and prefix/gap
   replacement assembly. Whole-input early returns are allowed only when the
   first match starts at index `0`.

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

Issue #1047 / PR #1285 added an eval fast path for Test262 RegExp literal
round-trip loops and widened deferred .NET `Regex` construction to simple
flagless literal-only patterns after normalization. Review found that a
comment-only eval payload such as `eval('//foo')` was misclassified as a
RegExp literal because the fast path looked at slash delimiters without first
excluding JavaScript comments. Future eval fast paths must be grammar-shaped
pre-filters: if the payload starts with a source comment or otherwise cannot be
proven to be only a RegExp literal plus optional semicolon/trivia, let the
normal parser own the result.

Issue #1335 / PR #1355 added a narrow `String.prototype.replace` fast path for
legacy `/\S+/g` to satisfy RegExp runtime performance gates. Review and the
final delivery exposed that replace shortcuts are still ECMAScript-observable:
custom own or prototype `exec` hooks must be called by the normal path, and a
nonzero first match such as `" a".replace(/\S+/g, "X")` must preserve the input
prefix rather than collapse to a whole-input replacement. Future replace
shortcuts should keep the same guard shape and add regression coverage for
custom `exec`, prefix preservation, and metadata updates.
