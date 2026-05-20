# ADR 0036: Keep RegExp lookbehind backreference shim narrow

## Status

Accepted

## Context

Issue #819 / PR #1091 fixed Test262 `RegExp_lookBehind` failures for
`back-references-to-captures.js` and `mutual-recursive.js`. The failing shapes
use numeric backreferences inside positive lookbehind assertions, where
ECMAScript captures to the right of a backreference can still participate
because lookbehind matching proceeds right-to-left.

The existing .NET `Regex` bridge could not model those semantics through a
simple normalized-pattern rewrite. The delivery therefore added a custom
execution path for a narrow family:

- non-`u` / non-`v` patterns,
- a leading positive lookbehind,
- numeric backreferences inside that lookbehind,
- no captures in the tail pattern.

The review cycle exposed the durable risk. Once a custom parser owns part of
RegExp execution, it can silently diverge from ECMAScript if it treats
unsupported syntax as close-enough literals. The delivery needed follow-up fixes
for capture spans and RegExp statics, then for unescaped `^` / `$` assertion
atoms, and finally for unsupported escapes such as `\b` / `\B`: unsupported
atoms must make the custom path decline so the normal regex bridge handles the
pattern, not be guessed as literal characters.

## Decision

Keep the positive-lookbehind numeric-backreference path as a narrow compatibility
shim, not as a general RegExp parser.

1. Enter the custom path only for the proven leading positive-lookbehind,
   non-Unicode numeric-backreference family.
2. Preserve all JavaScript-visible match metadata in that path: match index,
   capture values, `/d` capture spans, and legacy `RegExp` statics.
3. Parse only atoms whose semantics the shim implements exactly. Supported
   assertion atoms such as unescaped `^` and `$` must stay zero-width and
   multiline-aware. Unsupported escapes or groups must fail parsing and fall
   back to the normal `Regex` path.
4. Keep regression filters tied to the exact Test262 fixtures. Remove entries
   only after both fixture variants pass under the focused `RegExp_lookBehind`
   proof, with internal tests covering the observable metadata probes.

## Consequences

- Future fixes should not widen `LookbehindPatternParser` opportunistically.
  New atoms require proof for match success, capture ordering, indices, statics,
  sticky/global behavior, and fallback behavior for nearby unsupported syntax.
- The shim remains isolated from Unicode-mode RegExp handling until a separate
  proof shows that the same semantics and metadata can be preserved there.
- Review should treat "literal fallback" in the custom parser as suspicious.
  Unsupported syntax should usually decline the shim and let the normal runtime
  path decide.
- This ADR extends ADR 0033 and the root
  `.claude/rules/ecmascript-regexp-runtime-bridges.md` rule with the
  lookbehind-backreference-specific boundary caused by issue #819 / PR #1091.
