# ADR 0030: Keep RegExp Script_Extensions Unknown generator-owned

## Status

Accepted

## Context

Issue #820 / PR #1087 fixed the Test262
`RegExp_propertyEscapes` failures for
`special-property-value-Script_Extensions-Unknown.js`. The resolver already knew
the `Unknown` script value and its `Zzzz` alias, but generated
`ScriptExtensionRanges` did not include an `Unknown` entry. That made
`\p{Script_Extensions=Unknown}` behave like an invalid property escape even
though the script value itself was recognized.

The trap was in Unicode data generation, not in RegExp parsing. `Scripts.txt`
omits unassigned and unspecified code point gaps; those gaps implicitly belong
to `Script=Unknown`. `Script_Extensions` then inherits each code point's Script
value unless `ScriptExtensions.txt` provides an explicit override. Without first
synthesizing the omitted `Unknown` script ranges, the inheritance step has no
source data to carry into `ScriptExtensionRanges`.

The repo's generated-file rule also matters here: the generated C# data is a
derivative artifact. Hand-editing
`src/Asynkron.JsEngine/StdLib/RegExp/UnicodePropertyData.generated.cs` would
hide the source-of-truth defect in `tools/generate_unicode_properties.py`.

## Decision

Keep RegExp Unicode property data semantics in the generator. Before building
`Script_Extensions` inheritance data, the generator must synthesize implicit
`Script=Unknown` ranges from code points omitted by `Scripts.txt`. The
inheritance algorithm then starts from the complete Script data, removes code
points with explicit `ScriptExtensions.txt` overrides from their default
Script, and adds those override ranges only to the listed scripts.

For this boundary, future fixes should:

1. update `tools/generate_unicode_properties.py` first;
2. regenerate `UnicodePropertyData.generated.cs` from the generator;
3. verify both canonical and alias lookup paths, such as
   `Script_Extensions=Unknown` and `scx=Zzzz`; and
4. run the focused Test262 RegExp property-escape method group or exact fixture
   that exposed the regression.

## Consequences

- `Script_Extensions=Unknown` remains data-driven and follows the Unicode UCD
  inheritance rule rather than a resolver special case.
- Explicit `ScriptExtensions.txt` entries still override inherited Script data;
  the default Script is not added back unless the upstream scx entry lists it.
- Future Unicode data refreshes must preserve the generator-owned implicit
  `Unknown` synthesis before comparing generated-file diffs.
- This ADR complements the root
  `.claude/rules/ecmascript-regexp-unicode-properties.md` rule caused by issue
  #820 / PR #1087.
