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
6. If generated property-escape fixtures crash or time out while the Unicode
   range data is already correct, treat .NET regex pattern size as a runtime
   encoder problem. Compact astral surrogate-pair output by grouping high
   surrogates that share the same normalized low-surrogate class; do not mask
   the issue with broad Test262 timeouts or generated-data edits.
7. If exact generated property-escape fixtures of the form
   `/^\p{...}+$/u` or `/^\P{...}+$/u` still pass too slowly after range data and
   pattern encoding are correct, keep the repair as a narrow `JsRegExp`
   runtime matcher. The matcher must stay `u`-only, non-global, non-sticky, and
   exact-full-string; it must read by code point, preserve whole-input `exec`
   and RegExp statics, and decline all mixed or capture-bearing patterns back to
   the normal .NET regex bridge.
8. Do not put normal lexer identifier classification on
   `UnicodePropertyData.Resolve(...)` or other RegExp Unicode-property resolver
   paths. Parser hot paths such as `UnicodeIdentifier` should classify
   ECMAScript `ID_Start` / `ID_Continue` directly, with explicit compatibility
   handling for `Other_ID_Start` / `Other_ID_Continue` code points. Prove this
   boundary with focused identifier tests and the issue-supplied
   `Name=Identifiers` Test262 method group before widening.

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

Issue #821 / PR #1114 fixed generated Test262 property-escape crashes where
large astral-heavy ranges produced oversized .NET regex patterns. The durable
lesson is to separate Unicode data correctness from runtime pattern-size
correctness: when the data is right, compact `JsRegExp` surrogate-pair output
instead of changing generated Unicode tables or widening harness timeouts.

Issue #1040 / PR #1283 exposed the opposite boundary from the parser side. A
lexer fix initially reused `UnicodePropertyData.Resolve(...)` for identifier
classification, which made ordinary identifier tokenization initialize the
heavy RegExp Unicode property dataset. Future agents should keep ECMAScript
identifier classification lightweight and parser-owned, using direct Unicode
category checks plus the small `Other_ID_*` compatibility set instead of
coupling lexer hot paths to RegExp property escape infrastructure.

Issue #1332 / PR #1346 exposed the match-time side of the same RegExp property
escape performance boundary. Generated fixtures for `Alphabetic`, `Any`, and
`ASCII_Hex_Digit` were semantically correct, but exact anchored full-string
property escape checks still paid heavy .NET regex construction and matching
cost. The durable lesson is to keep this as a narrow runtime matcher over the
resolved Unicode ranges, not as a generated-data edit or a Test262 timeout
exception. Review sent the delivery back until the representative strict and
non-strict Test262 cases were below the explicit under-10s gate.
