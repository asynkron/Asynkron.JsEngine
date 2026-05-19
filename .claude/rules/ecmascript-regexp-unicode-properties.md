# ECMAScript RegExp Unicode Properties

When fixing RegExp Unicode property escapes, keep Unicode data semantics in the
generator and prove the generated resolver behavior through focused tests.

## Rules

1. Do not hand-edit
   `src/Asynkron.JsEngine/StdLib/RegExp/UnicodePropertyData.generated.cs` as
   the source of truth. Change `tools/generate_unicode_properties.py`, then
   regenerate the generated data.
2. Before constructing `Script_Extensions` inheritance data, synthesize
   implicit `Script=Unknown` ranges for code points omitted by `Scripts.txt`.
   `Scripts.txt` gaps are semantically meaningful, not absent data.
3. Preserve `Script_Extensions` override semantics: code points with explicit
   `ScriptExtensions.txt` entries belong only to the listed scripts unless the
   upstream entry explicitly includes their default Script.
4. Add focused resolver coverage for canonical names and aliases whenever a
   property-value alias is part of the bug. For issue #820 this means both
   `Script_Extensions=Unknown` and `scx=Zzzz`.
5. Keep proof narrow for Test262 property-escape issues. Use the exact failing
   fixture or the issue-supplied method group before widening.

## Why

Issue #820 / PR #1087 fixed Test262 failures for
`special-property-value-Script_Extensions-Unknown.js`. The engine recognized
the `Unknown` script value and `Zzzz` alias, but generated
`ScriptExtensionRanges` lacked `Unknown` because the generator inherited
Script data before synthesizing the implicit `Script=Unknown` gaps omitted by
Unicode `Scripts.txt`.

Future agents should treat Unicode property data gaps as part of the upstream
data model and repair the generator pipeline, not the RegExp parser or a
generated C# table by hand.
