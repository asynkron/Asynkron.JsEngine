# Investigation Report: RegExp Test262 Failures (Excluding Property Escapes and UnicodeSets)

## Problem Summary
Approximately 42 Test262 test instances (21 unique test files x 2 strict/sloppy) are failing across four distinct root causes in the RegExp implementation. The failures cluster into: inline modifier groups (16 files), lookbehind in unicode mode (1 file), lookbehind back-references (2 files), quantified duplicate named groups (2 files), and unicode dot with lone surrogates (1 file).

## Affected Components
- `src/Asynkron.JsEngine/JsTypes/JsRegExp.cs` -- `NormalizePattern()` (unicode path, lines 976-1374)
- `src/Asynkron.JsEngine/JsTypes/JsRegExp.cs` -- `NormalizeLegacyPattern()` (lines 1376-1820)
- `src/Asynkron.JsEngine/JsTypes/JsRegExp.cs` -- `CreateMatchArray()` (lines 403-515)
- `src/Asynkron.JsEngine/JsTypes/JsRegExp.cs` -- `DeduplicateGroupNames()` (lines 2295-2494)
- `src/Asynkron.JsEngine/JsTypes/JsRegExp.cs` -- Constants `UnicodeDotPattern` (line 27)

## Evidence Collected

### Failure Group 1: Inline Modifier Groups (32 test instances / 16 unique files)

**Root Cause: No support for `(?ims:...)` / `(?-ims:...)` modifier group syntax**

Test filter: `Name=RegExp_regexpModifiers`
Result: Failed 32, Passed 92

The ECMAScript `regexp-modifiers` proposal adds inline modifier groups: `(?s:...)` (add dotAll), `(?-s:...)` (remove dotAll), `(?m:...)`, `(?-m:...)`, `(?i:...)`, `(?-i:...)`. The engine passes these through to .NET regex unchanged, but .NET's `RegexOptions.ECMAScript` mode does NOT support inline modifiers.

Error examples:
```
add-dotAll.js: "Pattern character '.' should match line terminators in modified group"
remove-dotAll.js: "Pattern character '.' should not match '\n' in modified group"
add-multiline.js: "$ should not match newline outside modified group"
```

The modifier syntax `(?s:^.$)` means "within this group, enable dotAll". The `.` inside should match `\n`, but the `$` and `^` outside should NOT be affected. .NET just ignores the modifier prefix, passing the literal characters through, so the pattern breaks.

**Grep confirmation:** No handling of modifier groups exists:
```
$ grep -n '(?[ims-]+:' src/Asynkron.JsEngine/JsTypes/JsRegExp.cs
2800:            builder.Append("(?-i:\\u212A)");  // unrelated
```

Failing test files:
- `add-dotAll.js`, `remove-dotAll.js`, `add-multiline.js`
- `add-dotAll-does-not-affect-alternatives-outside.js`
- `add-dotAll-does-not-affect-ignoreCase-flag.js`
- `add-dotAll-does-not-affect-multiline-flag.js`
- `add-ignoreCase-affects-slash-lower-b.js` / `upper-b.js`
- `add-ignoreCase-affects-slash-lower-w.js` / `upper-w.js`
- `changing-dotAll-flag-does-not-affect-dotAll-modifier.js`
- `nesting-dotAll-does-not-affect-alternatives-outside.js`
- `remove-dotAll-does-not-affect-alternatives-outside.js`
- `remove-dotAll-does-not-affect-ignoreCase-flag.js`
- `remove-dotAll-does-not-affect-multiline-flag.js`
- `remove-multiline-does-not-affect-dotAll-flag.js`

### Failure Group 2: Lookbehind in Unicode Mode (4 test instances / 2 unique files)

**Root Cause: `NormalizePattern` (unicode path) misidentifies `(?<=` as named group `(?<`**

Test filter: `FullyQualifiedName~RegExp_namedGroups&FullyQualifiedName~lookbehind`
Error: `SyntaxError: Invalid regular expression: invalid group name.`

The bug is at `JsRegExp.cs:1313`:
```csharp
// Named capturing group (?<name>...)
if (c == '(' && i + 2 < pattern.Length && pattern[i + 1] == '?' && pattern[i + 2] == '<')
```

This matches `(?<=` (lookbehind) in addition to `(?<name>` (named group). It then tries to parse `=(?<a>\w){3})f` as a group name, which fails.

The legacy `NormalizeLegacyPattern` (line 1689-1690) correctly excludes lookbehind:
```csharp
var isNamedCapture = hasQuestion && i + 2 < pattern.Length && pattern[i + 2] == '<' &&
                     (i + 3 >= pattern.Length || (pattern[i + 3] != '=' && pattern[i + 3] != '!'));
```

But the unicode path is missing this check.

Test: `/(?<=(?<a>\w){3})f/u` matching "abcdef" works in non-unicode mode but throws in unicode mode.

Failing files:
- `built-ins/RegExp/named-groups/lookbehind.js` (non-unicode assertions pass, unicode ones fail)

### Failure Group 3: Lookbehind Back-References (.NET Engine Limitation) (4 test instances / 2 unique files)

**Root Cause: .NET regex engine evaluates lookbehind differently from ECMAScript spec**

Test filter: `Name=RegExp_lookBehind`
Result: Failed 4, Passed 30

The ECMAScript spec says lookbehind is evaluated with direction=-1, meaning captures and back-references inside lookbehind work right-to-left. .NET's regex engine does not fully implement this behavior for mutual recursive back-references.

Failing patterns verified at .NET level:
```csharp
// All return Success=false in .NET:
/(?<=\1(\w+))c/           matching "ababc"  -> expected ["c", "ab"]  (got: abab)
/(?<=a(.\2)b(\1)).{4}/    matching "aabcacbc" -> expected ["cacb", "a", ""]
/(?<=a(\2)b(..\1))b/      matching "aacbacb"  -> expected ["b", "ac", "ac"]
/(?<=(?:\1b)(aa))./        matching "aabaax"   -> expected ["x", "aa"]
```

Failing files:
- `built-ins/RegExp/lookBehind/back-references-to-captures.js`
- `built-ins/RegExp/lookBehind/mutual-recursive.js`

### Failure Group 4: Quantified Duplicate Named Groups (4 test instances / 2 unique files)

**Root Cause: Dedup wrapper groups don't reset properly under quantifier in ECMAScript mode**

Test filter: `FullyQualifiedName~RegExp_namedGroups&FullyQualifiedName~duplicate-names`
Result: Failed 4, Passed 16

The dedup wrapping transforms `(?<a>x)|(?<a>y)` into `(?<a__dup0>(?<a>x))|(?<a__dup1>(?<a>y))`. Under a `{2}` quantifier, the inner merged group `(?<a>...)` retains its value from the previous iteration in .NET's ECMAScript mode, even though ECMAScript says groups should be reset.

Verified failures:
```
/^(?:(?<a>x)|(?<a>y)|z){2}\k<a>$/.exec("xz")  -> null (expected ["xz", undefined, undefined])
/^(?:(?<a>x)|(?<a>y)|z){2}\k<a>$/.exec("yz")  -> null (expected ["yz", undefined, undefined])
/^(?:(?<a>x)|(?<a>y)|z){2}\k<a>$/.exec("xzx") -> ["xzx","x",null] (expected null)
/^(?:(?<a>x)|(?<a>y)|z){2}\k<a>$/.exec("yzy") -> ["yzy",null,"y"] (expected null)
```

When `z` matches in the second iteration, the merged inner `\k<a>` should match empty (group reset), but the inner merged group retains the first iteration's value.

Failing files:
- `built-ins/RegExp/named-groups/duplicate-names-exec.js` (lines 32-35)
- `built-ins/RegExp/named-groups/duplicate-names-match.js` (lines 32-35)

### Failure Group 5: Unicode Dot with Lone Surrogates (2 test instances / 1 unique file)

**Root Cause: `UnicodeDotPattern` excludes lone surrogates**

Test filter: `FullyQualifiedName~RegExp_dotall&FullyQualifiedName~without-dotall-unicode`
Error: `Expected true but got false`

The `UnicodeDotPattern` (line 27):
```csharp
private const string UnicodeDotPattern =
    @"(?:[\uD800-\uDBFF][\uDC00-\uDFFF]|[^\n\r\u2028\u2029\uD800-\uDFFF])";
```

The first alternative matches surrogate pairs. The second alternative explicitly excludes `\uD800-\uDFFF`. This means lone surrogates `\uD800` and `\uDFFF` are not matched. But in JavaScript, `/^.$/u` should match lone surrogates because they are valid code units in JS strings.

Verified with .NET test: current pattern fails for `\uD800` and `\uDFFF`, but adding lone surrogate alternatives fixes it:
```csharp
// Fixed pattern:
@"(?:[\uD800-\uDBFF][\uDC00-\uDFFF]|[\uD800-\uDBFF](?![\uDC00-\uDFFF])|(?<![\uD800-\uDBFF])[\uDC00-\uDFFF]|[^\n\r\u2028\u2029\uD800-\uDFFF])"
```

Failing file:
- `built-ins/RegExp/dotall/without-dotall-unicode.js`

## Root Cause Analysis

### Issue 1 (Highest Impact -- 32 tests): Inline Modifier Groups Not Implemented
The `(?s:...)`, `(?m:...)`, `(?i:...)` and their negated forms `(?-s:...)` etc. are ES2024 modifier group syntax. The pattern normalization in both `NormalizePattern` and `NormalizeLegacyPattern` has zero handling for this syntax. The `(?s:...)` is passed through verbatim to .NET which treats it as a syntax error in ECMAScript mode or ignores it.

**Fix approach:** During pattern normalization, detect `(?[ims]*-?[ims]*:...)` modifier groups. Track a modifier state stack. When entering a modifier group:
- For `s` (dotAll): replace `.` inside the group with the appropriate dot-all or non-dot-all pattern
- For `m` (multiline): replace `^` and `$` with `(?:^|\n)` / `(?:$|\n)` equivalents or use `(?:(?<=^|\n))` etc.
- For `i` (ignoreCase): This is harder -- would need to toggle case sensitivity inline. .NET non-ECMAScript mode supports `(?i:...)` natively but the engine uses ECMAScript mode. Could potentially drop ECMAScript mode and handle the differences manually, or expand case-insensitive character ranges.
- Strip the modifier prefix from the output, converting to a non-capturing group: `(?s:X)` -> `(?:X)` with `.` expanded

**Complexity: Medium-High.** The dotAll modifier is straightforward (replace `.` with `[\s\S]` or `[^\n\r\u2028\u2029]` based on modifier). The multiline modifier requires handling `^` and `$`. The ignoreCase modifier is the hardest.

### Issue 2 (4 tests): Missing Lookbehind Check in Unicode NormalizePattern
**Fix: One-line change at JsRegExp.cs:1313**

Add the same check that exists in `NormalizeLegacyPattern`:
```csharp
if (c == '(' && i + 2 < pattern.Length && pattern[i + 1] == '?' && pattern[i + 2] == '<'
    && i + 3 < pattern.Length && pattern[i + 3] != '=' && pattern[i + 3] != '!')
```

**Complexity: Trivial.** Single condition addition.

### Issue 3 (4 tests): .NET Lookbehind Back-Reference Semantics
This is a fundamental limitation of .NET's regex engine. The ECMAScript spec says lookbehind evaluates right-to-left with different capture/back-reference semantics. .NET does not implement this.

**Fix options:**
- A: Accept as a known limitation (document it)
- B: Implement a custom regex engine for these patterns (extremely complex)
- C: Detect patterns with back-references inside lookbehind and use a different evaluation strategy

**Complexity: Very High (for fix) / None (for acceptance).**

### Issue 4 (4 tests): Quantified Duplicate Named Groups
The dedup wrapper approach fails under quantification because .NET ECMAScript mode doesn't reset the inner merged group `(?<a>...)` between quantifier iterations the way JS expects.

**Fix approach:** Change the dedup strategy for quantified contexts. Instead of wrapping `(?<a__dup0>(?<a>x))`, use a different approach where the inner group name is also deduplicated and backreferences are rewritten to use conditional patterns that check all variants.

**Complexity: High.** The current dedup architecture assumes the merged inner group works for `\k<>` backreferences, but this breaks under quantification.

### Issue 5 (2 tests): Unicode Dot Pattern Missing Lone Surrogates
**Fix: Update `UnicodeDotPattern` constant at JsRegExp.cs:27**

```csharp
private const string UnicodeDotPattern =
    @"(?:[\uD800-\uDBFF][\uDC00-\uDFFF]|[\uD800-\uDBFF](?![\uDC00-\uDFFF])|(?<![\uD800-\uDBFF])[\uDC00-\uDFFF]|[^\n\r\u2028\u2029\uD800-\uDFFF])";
```

Also need to update `AnyCodePointPattern` (line 22) similarly if `.` with dotAll flag in unicode mode also needs lone surrogate support.

**Complexity: Low.** Constant update plus verification.

## Recommended Fix Priority

### Priority 1: Lookbehind check in unicode NormalizePattern (Issue 2)
- **Impact:** 4 tests fixed
- **Risk:** Minimal
- **Effort:** 5 minutes
- **File:** `JsRegExp.cs:1313` -- add `&& pattern[i + 3] != '=' && pattern[i + 3] != '!'` check

### Priority 2: Unicode dot lone surrogates (Issue 5)
- **Impact:** 2 tests fixed
- **Risk:** Low (needs to verify no regressions in other unicode dot tests)
- **Effort:** 15 minutes
- **File:** `JsRegExp.cs:27` -- update `UnicodeDotPattern` constant

### Priority 3: Inline modifier groups (Issue 1)
- **Impact:** 32 tests fixed
- **Risk:** Medium (complex transformation, could introduce regressions)
- **Effort:** 2-4 hours for dotAll and multiline modifiers; ignoreCase modifiers harder
- **File:** `JsRegExp.cs` -- both `NormalizePattern` and `NormalizeLegacyPattern`
- **Approach:**
  1. Parse `(?[ims]*-?[ims]*:` at the start of non-capturing groups
  2. Track a stack of modifier state `{ dotAll, ignoreCase, multiline }`
  3. When transforming `.`, use modifier stack to decide pattern
  4. When transforming `^`/`$`, use modifier stack for multiline behavior
  5. Strip modifier prefix, emit `(?:` instead
  6. Defer ignoreCase modifier to a later iteration if needed

### Priority 4: Quantified duplicate named groups (Issue 4)
- **Impact:** 4 tests fixed
- **Risk:** High (fundamental architecture change for dedup)
- **Effort:** 4-8 hours
- **Note:** May require completely rethinking the dedup strategy

### Priority 5: Lookbehind back-references (Issue 3)
- **Impact:** 4 tests fixed
- **Risk:** Very High
- **Effort:** Impractical without custom regex engine
- **Recommendation:** Document as known .NET limitation

## Test Plan
- [ ] Fix Issue 2: `dotnet test tests/Asynkron.JsEngine.Tests.Test262 -c Release --filter "FullyQualifiedName~RegExp_namedGroups&FullyQualifiedName~lookbehind"`
- [ ] Fix Issue 5: `dotnet test tests/Asynkron.JsEngine.Tests.Test262 -c Release --filter "FullyQualifiedName~RegExp_dotall&FullyQualifiedName~without-dotall-unicode"`
- [ ] Fix Issue 1: `dotnet test tests/Asynkron.JsEngine.Tests.Test262 -c Release --filter "Name=RegExp_regexpModifiers"`
- [ ] Fix Issue 4: `dotnet test tests/Asynkron.JsEngine.Tests.Test262 -c Release --filter "FullyQualifiedName~RegExp_namedGroups&FullyQualifiedName~duplicate-names"`
- [ ] Regression check: `dotnet test tests/Asynkron.JsEngine.Tests.Test262 -c Release --filter "Name=RegExp_lookBehind|Name=RegExp_CharacterClassEscapes|Name=RegExp_dotall|Name=RegExp_namedGroups"`
- [ ] Full internal test suite: `dotnet test tests/Asynkron.JsEngine.Tests`

## Summary of Test Counts

| Issue | Unique Files | Test Instances | Fix Complexity | Priority |
|-------|-------------|---------------|----------------|----------|
| Inline modifiers | 16 | 32 | Medium-High | 3 |
| Lookbehind unicode parse | 1 | 2(+2 strict) | Trivial | 1 |
| Lookbehind back-refs | 2 | 4 | Impractical | 5 |
| Quantified dup groups | 2 | 4 | High | 4 |
| Unicode dot lone surr. | 1 | 2 | Low | 2 |
| **Total** | **22** | **44** | | |

**Note:** Some of the ~60 tests originally listed by the user are now passing (CharacterClassEscapes, character-class-escape-non-whitespace, language/literals/regexp tests). The actual current failure count is 44 test instances across 22 unique test files.

## Additional Notes
- The `RegexOptions.ECMAScript` flag constrains what .NET features are available. Inline modifiers `(?i:...)` are NOT supported in ECMAScript mode. Any modifier support must be implemented at the pattern transformation level.
- The lookbehind back-reference issue (Issue 3) is a fundamental .NET regex engine limitation. V8 and SpiderMonkey implement the ECMAScript spec's backward direction evaluation, but .NET has its own lookbehind semantics.
- The `AnyCodePointPattern` constant (line 22) may also need the lone surrogate fix for consistency, though it was not directly tested.
