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
8. Create the exact anchored property matcher before RegExp normalization and
   .NET regex construction. If the matcher accepts the original ECMAScript
   source and flags, return from construction without expanding property ranges
   into `_normalizedPattern`, building capture metadata, or compiling a .NET
   regex. Profile Unicode data warm-up, RegExp construction, sample string
   construction, positive match, and negated match as separate phases so agents
   do not confuse one-time resolver initialization with per-pattern cost.
9. Keep exact single-codepoint anchored property escapes in the same narrow
   `JsRegExp` runtime matcher family as one-or-more anchored property escapes.
   Accept `^\p{...}$` and `^\P{...}$` only when the pattern remains `u`-only,
   non-global, non-sticky, capture-free, and whole-input. The non-quantified
   shape must require exactly one consumed Unicode code point; do not treat it
   as equivalent to `+`, and decline all mixed RegExp shapes back to the normal
   bridge.
10. Do not put normal lexer identifier classification on
   `UnicodePropertyData.Resolve(...)` or other RegExp Unicode-property resolver
   paths. Parser hot paths such as `UnicodeIdentifier` should classify
   ECMAScript `ID_Start` / `ID_Continue` directly, with explicit compatibility
   handling for `Other_ID_Start` / `Other_ID_Continue` code points. Prove this
   boundary with focused identifier tests and the issue-supplied
   `Name=Identifiers` Test262 method group before widening.
11. For punctuation `General_Category` property-escape crash reports, prove the
   exact generated Test262 category filters first. If `Close_Punctuation` and
   `Connector_Punctuation` are already green on current main, keep the closeout
   to focused internal regression coverage that exercises both anchored
   full-string and unanchored membership shapes with positive and negative
   samples. Do not edit generated Unicode tables, widen runtime matchers, or
   change Test262 harness policy without a current failing row.
12. Normalize and clamp astral scalar ranges at the surrogate-pair encoder
    boundary before calculating high and low surrogate classes. Even when
    Unicode property data is expected to be sorted and scalar, `JsRegExp`
    runtime pattern construction must not let a malformed, complemented, or
    narrow script row produce invalid .NET regex ranges. Keep the fix in
    `BuildSurrogatePairRanges` or its direct caller, and add focused coverage
    for both positive and negated property escapes before widening.
13. Keep bare `u`-only property escapes such as `/\p{...}/u` and `/\P{...}/u`
    in the same narrow `JsRegExp` runtime matcher family when generated
    Test262 fixtures use them as unanchored searches. The matcher must return
    the first matching code point and correct match index/statics, remain
    non-global, non-sticky, capture-free, and decline mixed patterns back to
    the normal .NET regex bridge. When adding regression samples for
    `Script_Extensions`, choose code points from the resolved generated ranges
    instead of assuming a combining mark belongs to `Inherited`.

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

Issue #1377 / PR #1384 exposed the construction-order side of the anchored
matcher boundary. The exact generated Test262 property-escape shape was already
runtime-matchable, but creating the matcher after `NormalizePattern(...)` still
paid expanded .NET regex construction cost for large property ranges. Future
agents should recognize the narrow anchored matcher before normalization and
measure warm-up, compile, sample-build, and positive/negated match phases
separately before claiming a RegExp property-escape performance repair.

Issue #1743 / PR #1766 exposed the cardinality side of that same anchored
matcher boundary. Generated Unicode property escape fixtures include exact
single-codepoint checks such as `/^\p{Extended_Pictographic}$/u`, not only
one-or-more full-string checks. Future agents should keep the single-codepoint
form runtime-owned but prove that it consumes exactly one Unicode code point,
including astral code points represented by surrogate pairs, instead of falling
back to expanded .NET regex patterns or accidentally accepting repeated matches.

Issue #2005 / PR #2016 repeated the generated property-escape crash-list shape
for punctuation general categories. The focused current-main Test262 filters
for `Close_Punctuation` and `Connector_Punctuation` were green, so the durable
artifact was an internal regression covering anchored and unanchored
punctuation category behavior rather than a Unicode data, `JsRegExp`, or
harness patch.

Issue #2565 / PR #2572 fixed generated Test262 crash rows for astral-only
script property escapes `Script=Marchen` and `Script=Masaram_Gondi`. The
generated Unicode data already resolved the scripts, so the repair stayed in
`JsRegExp` surrogate-pair range generation: normalize and clamp astral ranges
before grouping high surrogates by low-surrogate class, with focused internal
coverage for positive and negated forms plus the issue-listed Release Test262
rows. Future agents should treat similar astral script-property crashes as a
runtime encoder boundary first, not as permission to hand-edit generated data
or widen harness policy.

Issue #2882 / PR #2894 exposed the unanchored-search side of the generated
property-escape performance boundary. The existing anchored matcher avoided
large .NET regex construction for whole-input generated fixtures, but bare
`u`-only `Script_Extensions=Common` and `Script_Extensions=Inherited` property
escapes still routed through the expanded .NET pattern. The durable lesson is
to extend the narrow matcher by RegExp shape, not by generated-data edits or
harness timeouts, and to prove `exec` result/index/statics for positive and
negated searches. The focused regression also showed that `\u0300` was not a
valid `Script_Extensions=Inherited` sample in the generated data; future tests
should pick samples from the actual resolved ranges, such as U+030F for the
issue #2882 inherited fixture.
