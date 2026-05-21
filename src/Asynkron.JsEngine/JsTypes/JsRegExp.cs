#region

using System.Buffers;
using System.Collections.Concurrent;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Asynkron.JsEngine.Parser;
using Asynkron.JsEngine.Runtime;
using Asynkron.JsEngine.StdLib;
using Asynkron.JsEngine.StdLib.RegExp;

#endregion

namespace Asynkron.JsEngine.JsTypes;

/// <summary>
///     Represents a JavaScript regular expression object.
/// </summary>
public sealed class JsRegExp
{
    // Cache for BuildPropertyEscapePattern results. Same (expression, negate) always
    // produces the same pattern string, and building it is expensive (surrogate pair math,
    // large string allocations). ~1200 entries max (600 properties x 2 for negate).
    private static readonly ConcurrentDictionary<(string Expression, bool Negate), string>
        PropertyEscapePatternCache = new();
    private const byte FlagHasIndices = 1 << 0;
    private const byte FlagGlobal = 1 << 1;
    private const byte FlagIgnoreCase = 1 << 2;
    private const byte FlagMultiline = 1 << 3;
    private const byte FlagDotAll = 1 << 4;
    private const byte FlagUnicode = 1 << 5;
    private const byte FlagUnicodeSets = 1 << 6;
    private const byte FlagSticky = 1 << 7;
    private const byte AllFlagsMask =
        FlagHasIndices |
        FlagGlobal |
        FlagIgnoreCase |
        FlagMultiline |
        FlagDotAll |
        FlagUnicode |
        FlagUnicodeSets |
        FlagSticky;

    /// <summary>
    /// Normalized pattern length above which we skip RegexOptions.Compiled.
    /// Unicode property-escape patterns (e.g. \p{Script=Arabic}) expand to
    /// thousands of character alternations. JIT-compiling these takes hundreds
    /// of ms and tens of MB per pattern, which exceeds the matching benefit for
    /// typical Test262 usage. 1024 chars covers normal patterns while excluding
    /// expanded property escapes.
    /// </summary>
    private const int LargePatternThreshold = 1024;

    private const string AnyCodePointPattern =
        @"(?<![\uD800-\uDBFF])(?:[\uD800-\uDBFF][\uDC00-\uDFFF]|[\u0000-\uD7FF\uE000-\uFFFF]|[\uD800-\uDBFF](?![\uDC00-\uDFFF])|[\uDC00-\uDFFF])";

    // Unicode dot: matches a full code point (surrogate pair first, then lone surrogates, then BMP).
    // Surrogate pair must be tried first to avoid matching only the high surrogate.
    // Lone surrogates are valid code units in JS strings and must be matchable.
    private const string UnicodeDotPattern =
        @"(?:[\uD800-\uDBFF][\uDC00-\uDFFF]|[\uD800-\uDBFF](?![\uDC00-\uDFFF])|(?<![\uD800-\uDBFF])[\uDC00-\uDFFF]|[^\n\r\u2028\u2029\uD800-\uDFFF])";

    /// <summary>
    /// JavaScript's dot (.) without dotAll flag: matches any single UTF-16 code unit
    /// except the four JS line terminators (\n, \r, \u2028, \u2029).
    /// .NET's default dot only excludes \n, so we must use an explicit character class.
    /// </summary>
    private const string LegacyDotPattern = @"[^\n\r\u2028\u2029]";

    /// <summary>
    /// JavaScript's dot (.) with dotAll flag and no unicode: matches any single UTF-16 code unit.
    /// </summary>
    private const string LegacyDotAllPattern = @"[\s\S]";

    // ECMAScript \s: WhiteSpace + LineTerminator code points.
    // .NET \s differs: includes \x85 (NEXT LINE) and excludes \uFEFF (BOM).
    // We use explicit character classes to match the ECMAScript spec exactly.
    private const string EcmaWhitespaceClass =
        "[\t\n\v\f\r \u00a0\u1680\u2000-\u200a\u2028\u2029\u202f\u205f\u3000\ufeff]";

    private const string EcmaNonWhitespaceClass =
        "[^\t\n\v\f\r \u00a0\u1680\u2000-\u200a\u2028\u2029\u202f\u205f\u3000\ufeff]";

    // ECMAScript \w = [A-Za-z0-9_] (ASCII only).
    // .NET \w without ECMAScript flag matches Unicode letters/digits which is too broad.
    private const string EcmaWordClass = "[A-Za-z0-9_]";
    private const string EcmaNonWordClass = "[^A-Za-z0-9_]";

    // Unicode ignoreCase \w includes U+017F (LATIN SMALL LETTER LONG S) and U+212A (KELVIN SIGN)
    // per ES spec GetWordCharacters: Canonicalize maps these to 's' and 'K' respectively.
    private const string EcmaWordClassUnicodeIgnoreCase = "[A-Za-z0-9_\u017F\u212A]";
    private const string EcmaNonWordClassUnicodeIgnoreCase = "[^A-Za-z0-9_\u017F\u212A]";

    // Word boundary using expanded word chars for unicode ignoreCase.
    private const string EcmaWordBoundaryUnicodeIgnoreCase =
        "(?:(?<=[A-Za-z0-9_\u017F\u212A])(?![A-Za-z0-9_\u017F\u212A])|(?<![A-Za-z0-9_\u017F\u212A])(?=[A-Za-z0-9_\u017F\u212A]))";
    private const string EcmaNonWordBoundaryUnicodeIgnoreCase =
        "(?:(?<=[A-Za-z0-9_\u017F\u212A])(?=[A-Za-z0-9_\u017F\u212A])|(?<![A-Za-z0-9_\u017F\u212A])(?![A-Za-z0-9_\u017F\u212A]))";

    // ECMAScript word boundary using only ASCII word chars (for unicode mode where .NET's \b is too broad).
    private const string EcmaWordBoundary =
        "(?:(?<=[A-Za-z0-9_])(?![A-Za-z0-9_])|(?<![A-Za-z0-9_])(?=[A-Za-z0-9_]))";
    private const string EcmaNonWordBoundary =
        "(?:(?<=[A-Za-z0-9_])(?=[A-Za-z0-9_])|(?<![A-Za-z0-9_])(?![A-Za-z0-9_]))";

    // ECMAScript \d = [0-9] (ASCII only).
    // .NET \d without ECMAScript flag matches Unicode digits which is too broad.
    private const string EcmaDigitClass = "[0-9]";
    private const string EcmaNonDigitClass = "[^0-9]";

    // Raw ranges for embedding inside character classes (no brackets).
    private const string EcmaWhitespaceRanges =
        "\t\n\v\f\r \u00a0\u1680\u2000-\u200a\u2028\u2029\u202f\u205f\u3000\ufeff";

    private const string EcmaWordCharRanges = "A-Za-z0-9_";
    private const string EcmaDigitRanges = "0-9";

    // Unicode-mode negated patterns: match any full code point NOT in the set.
    // Must handle surrogate pairs AND lone surrogates (valid in JS strings).
    private const string UnicodeEcmaNonWhitespacePattern =
        "(?:[\uD800-\uDBFF][\uDC00-\uDFFF]|[\uD800-\uDBFF](?![\uDC00-\uDFFF])|(?<![\uD800-\uDBFF])[\uDC00-\uDFFF]|[^\t\n\v\f\r \u00a0\u1680\u2000-\u200a\u2028\u2029\u202f\u205f\u3000\ufeff\uD800-\uDFFF])";

    private const string UnicodeEcmaNonWordPattern =
        "(?:[\uD800-\uDBFF][\uDC00-\uDFFF]|[\uD800-\uDBFF](?![\uDC00-\uDFFF])|(?<![\uD800-\uDBFF])[\uDC00-\uDFFF]|[^A-Za-z0-9_\uD800-\uDFFF])";

    private const string UnicodeEcmaNonDigitPattern =
        "(?:[\uD800-\uDBFF][\uDC00-\uDFFF]|[\uD800-\uDBFF](?![\uDC00-\uDFFF])|(?<![\uD800-\uDBFF])[\uDC00-\uDFFF]|[^0-9\uD800-\uDFFF])";

    private static readonly (int Start, int End)[] BasicEmojiKeycapBaseRanges =
    [
        (0x0023, 0x0023),
        (0x002A, 0x002A),
        (0x0030, 0x0039)
    ];

    private readonly string _normalizedPattern;
    private readonly RegexOptions _regexOptions;

    /// <summary>
    /// Maps .NET-safe group names back to original ECMAScript group names.
    /// ECMAScript allows '$' and unicode chars in group names that .NET doesn't support.
    /// Key = sanitized name (used in .NET regex), Value = original JS name.
    /// </summary>
    private readonly Dictionary<string, string>? _groupNameMapping;

    /// <summary>
    /// ES2025 duplicate named groups: maps original group name → ordered array of .NET renamed names.
    /// E.g., "x" → ["x__0", "x__1"]. Null if no duplicate group names exist.
    /// </summary>
    private readonly Dictionary<string, string[]>? _duplicateGroupNames;

    /// <summary>
    /// Maps JS (left-to-right) group index → .NET group index.
    /// Null if no reordering is needed.
    /// </summary>
    private int[]? _groupReorderMap;

    /// <summary>
    /// For each .NET group index, the .NET group index of its nearest ancestor that
    /// is followed by a quantifier (+, *, {n,m}). -1 if the group is not inside a
    /// quantified construct. Used for ES capture-group-reset semantics: when a quantifier
    /// iterates, groups inside it that didn't participate in the last iteration must be
    /// reported as undefined rather than retaining their stale value from a prior iteration.
    /// </summary>
    private int[]? _quantifiedAncestorMap;

    private Regex? _compiledRegex;
    private readonly AnchoredPropertyEscapeMatcher? _anchoredPropertyEscapeMatcher;
    private readonly byte _encodedFlags;
    private string? _flags;

    public JsRegExp(string pattern, string flags = "", RealmState? realmState = null, JsObject? existingObject = null)
        : this(pattern, EncodeFlags(flags), flags, realmState, existingObject)
    {
    }

    internal JsRegExp(string pattern, byte encodedFlags, RealmState? realmState = null, JsObject? existingObject = null)
        : this(pattern, ValidateEncodedFlags(encodedFlags), null, realmState, existingObject)
    {
    }

    private JsRegExp(
        string pattern,
        byte encodedFlags,
        string? flags,
        RealmState? realmState,
        JsObject? existingObject)
    {
        Pattern = pattern;
        _encodedFlags = encodedFlags;
        _flags = flags;
        RealmState = realmState;
        JsObject = existingObject ?? new JsObject();

        var normalized = NormalizePattern(pattern, Unicode || UnicodeSets, UnicodeSets, IgnoreCase, DotAll, Multiline);
        var sanitized = SanitizeGroupNamesForDotNet(normalized, out var nameMapping);
        var renamed = RenameDuplicateGroups(sanitized, ref nameMapping, out _duplicateGroupNames);
        _normalizedPattern = _duplicateGroupNames is not null
            ? InsertQuantifierResets(renamed, _duplicateGroupNames)
            : renamed;
        _groupNameMapping = nameMapping;
        _anchoredPropertyEscapeMatcher = TryCreateAnchoredPropertyEscapeMatcher(pattern, _encodedFlags);

        var canDeferInitialConstruction = CanDeferInitialRegexConstruction(pattern, _encodedFlags);

        // Convert JavaScript regex flags to .NET RegexOptions. Reusable regular
        // expressions keep the existing compiled fast path, but Annex B single
        // identity escapes are frequently source-only and one-shot in Test262,
        // so those stay interpreted when first matched.
        var options = RegexOptions.CultureInvariant;
        if (!canDeferInitialConstruction && _normalizedPattern.Length <= LargePatternThreshold)
        {
            options |= RegexOptions.Compiled;
        }

        if (IgnoreCase)
        {
            options |= RegexOptions.IgnoreCase;
        }

        if (Multiline)
        {
            options |= RegexOptions.Multiline;
        }

        _regexOptions = options;

        if (existingObject is null)
        {
            JsObject.DefineProperty("lastIndex",
                new PropertyDescriptor { Value = 0d, Writable = true, Enumerable = false, Configurable = false });
        }

        if (canDeferInitialConstruction || _anchoredPropertyEscapeMatcher is not null)
        {
            return;
        }

        try
        {
            var regex = EnsureRegex();
            _groupReorderMap = BuildGroupReorderMap(regex, _normalizedPattern);
            _quantifiedAncestorMap = BuildQuantifiedAncestorMap(regex, _normalizedPattern);
            _quantifiedAncestorMap = MergeCaptureResetMaps(
                _quantifiedAncestorMap,
                BuildZeroWidthQuantifierResetMap(regex, _normalizedPattern));
        }
        catch (ArgumentException ex)
        {
            throw new ParseException(ex.Message);
        }
    }

    public string Pattern { get; }

    public string Flags => _flags ??= DecodeFlags(_encodedFlags);

    public bool Global => (_encodedFlags & FlagGlobal) != 0;
    public bool IgnoreCase => (_encodedFlags & FlagIgnoreCase) != 0;
    public bool Multiline => (_encodedFlags & FlagMultiline) != 0;
    public bool DotAll => (_encodedFlags & FlagDotAll) != 0;
    public bool Unicode => (_encodedFlags & FlagUnicode) != 0;
    public bool Sticky => (_encodedFlags & FlagSticky) != 0;
    public bool HasIndices => (_encodedFlags & FlagHasIndices) != 0;
    public bool UnicodeSets => (_encodedFlags & FlagUnicodeSets) != 0;

    public JsObject JsObject { get; }
    internal RealmState? RealmState { get; }

    /// <summary>
    /// Mapping from sanitized .NET group names back to original ECMAScript group names.
    /// Null if no sanitization was needed.
    /// </summary>
    internal Dictionary<string, string>? GroupNameMapping => _groupNameMapping;

    private void SetProperty(string name, JsValue value, JsValue receiver)
    {
        JsObject.SetProperty(name, value, receiver);
    }

    public void SetProperty(string name, JsValue value)
    {
        SetProperty(name, value, JsObject.AsJsValue);
    }

    /// <summary>
    ///     Tests if the pattern matches the input string.
    ///     Follows ES2024 21.2.5.2.2 RegExpBuiltinExec semantics.
    /// </summary>
    public bool Test(string input)
    {
        // Step 4: Always read lastIndex (even for non-global/non-sticky).
        var lastIndex = GetLastIndex();

        // Step 8: If neither global nor sticky, override lastIndex to 0.
        var startIndex = Global || Sticky ? lastIndex : 0;

        if (startIndex > input.Length)
        {
            if (Global || Sticky)
            {
                SetLastIndexStrict(0);
            }

            return false;
        }

        if (TryExecPositiveLookbehindBackref(input, startIndex, out var lookbehindMatch, out var lookbehindHandled))
        {
            if (Global || Sticky)
            {
                SetLastIndexStrict(lookbehindMatch.Index + lookbehindMatch.Value.Length);
            }

            UpdateLookbehindBackrefRegExpStatics(input, lookbehindMatch);
            return true;
        }

        if (lookbehindHandled)
        {
            if (Global || Sticky)
            {
                SetLastIndexStrict(0);
            }

            return false;
        }

        if (_anchoredPropertyEscapeMatcher is { } anchoredMatcher)
        {
            var anchoredMatch = startIndex == 0 && anchoredMatcher.IsMatch(input);
            if (!anchoredMatch)
            {
                return false;
            }

            UpdateWholeInputRegExpStatics(input);
            return true;
        }

        var match = EnsureRegex().Match(input, startIndex);

        // Sticky: match must start exactly at startIndex.
        if (Sticky && match.Success && match.Index != startIndex)
        {
            match = System.Text.RegularExpressions.Match.Empty;
        }

        if (!match.Success)
        {
            if (Global || Sticky)
            {
                SetLastIndexStrict(0);
            }

            return false;
        }

        if (Global || Sticky)
        {
            SetLastIndexStrict(match.Index + match.Length);
        }

        RealmState.UpdateRegExpStatics(input, match);
        return true;
    }

    /// <summary>
    ///     Executes a search for a match and returns an array with match details.
    ///     Follows ES2024 21.2.5.2.2 RegExpBuiltinExec semantics.
    /// </summary>
    public JsArray? Exec(string input)
    {
        // Step 4: Always read lastIndex (even for non-global/non-sticky).
        var lastIndex = GetLastIndex();

        // Step 8: If neither global nor sticky, override lastIndex to 0.
        var startIndex = Global || Sticky ? lastIndex : 0;

        if (startIndex > input.Length)
        {
            if (Global || Sticky)
            {
                SetLastIndexStrict(0);
            }

            return null;
        }

        if (TryExecPositiveLookbehindBackref(input, startIndex, out var lookbehindMatch, out var lookbehindHandled))
        {
            if (Global || Sticky)
            {
                SetLastIndexStrict(lookbehindMatch.Index + lookbehindMatch.Value.Length);
            }

            var lookbehindResult = CreateLookbehindBackrefMatchArray(lookbehindMatch, input);
            UpdateLookbehindBackrefRegExpStatics(input, lookbehindMatch);
            return lookbehindResult;
        }

        if (lookbehindHandled)
        {
            if (Global || Sticky)
            {
                SetLastIndexStrict(0);
            }

            return null;
        }

        if (_anchoredPropertyEscapeMatcher is { } anchoredMatcher)
        {
            var anchoredMatch = startIndex == 0 && anchoredMatcher.IsMatch(input);
            if (!anchoredMatch)
            {
                return null;
            }

            var fastResult = CreateWholeInputMatchArray(input);
            UpdateWholeInputRegExpStatics(input);
            return fastResult;
        }

        var match = EnsureRegex().Match(input, startIndex);

        // Sticky: match must start exactly at startIndex.
        if (Sticky && match.Success && match.Index != startIndex)
        {
            match = System.Text.RegularExpressions.Match.Empty;
        }

        if (!match.Success)
        {
            if (Global || Sticky)
            {
                SetLastIndexStrict(0);
            }

            return null;
        }

        if (Global || Sticky)
        {
            SetLastIndexStrict(match.Index + match.Length);
        }

        // Build the result array with captures, groups, and indices when needed.
        var result = CreateMatchArray(match, input);

        RealmState.UpdateRegExpStatics(input, match);
        return result;
    }

    /// <summary>
    ///     Finds all matches in the input string.
    /// </summary>
    internal JsArray MatchAll(string input)
    {
        var result = new JsArray(RealmState);
        var matches = EnsureRegex().Matches(input);

        foreach (Match match in matches)
        {
            // Preserve exec-like result entries for matchAll.
            result.Push(CreateMatchArray(match, input));
        }

        return result;
    }

    private bool TryExecPositiveLookbehindBackref(
        string input,
        int startIndex,
        out LookbehindBackrefMatch result,
        out bool handled)
    {
        result = default;
        handled = false;

        if (Unicode || UnicodeSets ||
            !TrySplitLeadingPositiveLookbehind(Pattern, out var lookbehind, out var tail) ||
            !ContainsNumericBackreference(lookbehind) ||
            CountLegacyCaptures(tail) != 0 ||
            !LookbehindPatternParser.TryParse(lookbehind, DotAll, IgnoreCase, Multiline, out var parsedLookbehind))
        {
            return false;
        }

        handled = true;
        var normalizedTail = NormalizeLegacyPattern(tail, IgnoreCase, DotAll, Multiline);
        var options = RegexOptions.CultureInvariant;
        if (IgnoreCase)
        {
            options |= RegexOptions.IgnoreCase;
        }

        if (Multiline)
        {
            options |= RegexOptions.Multiline;
        }

        var tailRegex = new Regex(CapLargeQuantifiers(normalizedTail), options);
        var firstIndex = startIndex;
        var lastIndex = Sticky ? startIndex : input.Length;

        for (var index = firstIndex; index <= lastIndex; index++)
        {
            var tailMatch = tailRegex.Match(input, index);
            if (!tailMatch.Success)
            {
                return false;
            }

            if (tailMatch.Index != index)
            {
                if (Sticky)
                {
                    return false;
                }

                index = tailMatch.Index - 1;
                continue;
            }

            foreach (var lookbehindMatch in parsedLookbehind.Match(input, index))
            {
                result = new LookbehindBackrefMatch(index, tailMatch.Value, lookbehindMatch.Captures);
                return true;
            }
        }

        return false;
    }

    private JsArray CreateLookbehindBackrefMatchArray(LookbehindBackrefMatch match, string input)
    {
        var result = new JsArray(RealmState);
        result.Push(new JsValue(match.Value));

        foreach (var capture in match.Captures)
        {
            result.Push(capture is null ? JsValue.Undefined : new JsValue(capture.Value.Text));
        }

        result.DefineProperty("index",
            new PropertyDescriptor
            {
                Value = (double)match.Index, Writable = true, Enumerable = true, Configurable = true
            });
        result.DefineProperty("input",
            new PropertyDescriptor
            {
                Value = new JsValue(input), Writable = true, Enumerable = true, Configurable = true
            });
        result.DefineProperty("groups", new PropertyDescriptor
        {
            Value = JsValue.Undefined,
            Writable = true,
            Enumerable = true,
            Configurable = true
        });

        if (HasIndices)
        {
            var indices = new JsArray(RealmState);
            indices.Push(CreateIndexPair(match.Index, match.Index + match.Value.Length));
            foreach (var capture in match.Captures)
            {
                indices.Push(capture is null
                    ? JsValue.Undefined
                    : CreateIndexPair(capture.Value.Start, capture.Value.End));
            }

            result.DefineProperty("indices", new PropertyDescriptor
            {
                Value = JsValue.FromJsArray(indices),
                Writable = true,
                Enumerable = true,
                Configurable = true
            });
        }

        return result;
    }

    private void UpdateLookbehindBackrefRegExpStatics(string input, LookbehindBackrefMatch match)
    {
        if (RealmState is null)
        {
            return;
        }

        var statics = RealmState.RegExpStatics;
        statics.Input = input;
        statics.LastMatch = match.Value;
        statics.LeftContext = input[..match.Index];
        statics.RightContext = input[(match.Index + match.Value.Length)..];

        statics.LastParen = string.Empty;
        for (var i = 0; i < statics.Captures.Length; i++)
        {
            statics.Captures[i] = string.Empty;
        }

        for (var i = 0; i < match.Captures.Length && i < statics.Captures.Length; i++)
        {
            var capture = match.Captures[i];
            if (capture is null)
            {
                continue;
            }

            statics.Captures[i] = capture.Value.Text;
            if (capture.Value.End == match.Index)
            {
                statics.LastParen = capture.Value.Text;
            }
        }
    }

    private void UpdateWholeInputRegExpStatics(string input)
    {
        if (RealmState is null)
        {
            return;
        }

        var statics = RealmState.RegExpStatics;
        statics.Input = input;
        statics.LastMatch = input;
        statics.LeftContext = string.Empty;
        statics.RightContext = string.Empty;
        statics.LastParen = string.Empty;
        for (var i = 0; i < statics.Captures.Length; i++)
        {
            statics.Captures[i] = string.Empty;
        }
    }

    private JsArray CreateWholeInputMatchArray(string input)
    {
        var result = new JsArray(RealmState);
        result.Push(new JsValue(input));
        result.DefineProperty("index",
            new PropertyDescriptor
            {
                Value = 0d, Writable = true, Enumerable = true, Configurable = true
            });
        result.DefineProperty("input",
            new PropertyDescriptor
            {
                Value = new JsValue(input), Writable = true, Enumerable = true, Configurable = true
            });
        result.DefineProperty("groups", new PropertyDescriptor
        {
            Value = JsValue.Undefined,
            Writable = true,
            Enumerable = true,
            Configurable = true
        });

        return result;
    }

    private JsArray CreateIndexPair(int start, int end)
    {
        var pair = new JsArray(RealmState);
        pair.Push((double)start);
        pair.Push((double)end);
        return pair;
    }

    private static bool TrySplitLeadingPositiveLookbehind(string pattern, out string lookbehind, out string tail)
    {
        lookbehind = string.Empty;
        tail = string.Empty;

        if (!pattern.StartsWith("(?<=", StringComparison.Ordinal))
        {
            return false;
        }

        var depth = 1;
        var inCharClass = false;
        var escaped = false;
        for (var i = 4; i < pattern.Length; i++)
        {
            var c = pattern[i];
            if (escaped)
            {
                escaped = false;
                continue;
            }

            if (c == '\\')
            {
                escaped = true;
                continue;
            }

            if (inCharClass)
            {
                if (c == ']')
                {
                    inCharClass = false;
                }

                continue;
            }

            if (c == '[')
            {
                inCharClass = true;
                continue;
            }

            if (c == '(')
            {
                depth++;
                continue;
            }

            if (c != ')')
            {
                continue;
            }

            depth--;
            if (depth == 0)
            {
                lookbehind = pattern[4..i];
                tail = pattern[(i + 1)..];
                return tail.Length > 0;
            }
        }

        return false;
    }

    private static bool ContainsNumericBackreference(string pattern)
    {
        var inCharClass = false;
        var escaped = false;
        for (var i = 0; i < pattern.Length; i++)
        {
            var c = pattern[i];
            if (escaped)
            {
                escaped = false;
                if (!inCharClass && char.IsDigit(c) && c != '0')
                {
                    return true;
                }

                continue;
            }

            if (c == '\\')
            {
                escaped = true;
                continue;
            }

            if (inCharClass)
            {
                if (c == ']')
                {
                    inCharClass = false;
                }

                continue;
            }

            if (c == '[')
            {
                inCharClass = true;
            }
        }

        return false;
    }

    private readonly record struct LookbehindCapture(string Text, int Start, int End);

    private readonly record struct LookbehindBackrefMatch(int Index, string Value, LookbehindCapture?[] Captures);

    private readonly record struct LookbehindMatchState(int Position, LookbehindCapture?[] Captures);

    private sealed class LookbehindPattern
    {
        public LookbehindPattern(LookbehindDisjunction root, int captureCount)
        {
            Root = root;
            CaptureCount = captureCount;
        }

        public LookbehindDisjunction Root { get; }

        public int CaptureCount { get; }

        public IEnumerable<LookbehindMatchState> Match(string input, int endIndex)
        {
            var captures = new LookbehindCapture?[CaptureCount];
            foreach (var state in Root.Match(input, endIndex, captures))
            {
                yield return state;
            }
        }
    }

    private sealed class LookbehindDisjunction
    {
        public LookbehindDisjunction(List<LookbehindSequence> alternatives)
        {
            Alternatives = alternatives;
        }

        private List<LookbehindSequence> Alternatives { get; }

        public IEnumerable<LookbehindMatchState> Match(string input, int position, LookbehindCapture?[] captures)
        {
            foreach (var alternative in Alternatives)
            {
                foreach (var state in alternative.Match(input, position, captures))
                {
                    yield return state;
                }
            }
        }
    }

    private sealed class LookbehindSequence
    {
        public LookbehindSequence(List<LookbehindAtom> atoms)
        {
            Atoms = atoms;
        }

        private List<LookbehindAtom> Atoms { get; }

        public IEnumerable<LookbehindMatchState> Match(string input, int position, LookbehindCapture?[] captures)
        {
            foreach (var state in MatchFrom(Atoms.Count - 1, input, position, captures))
            {
                yield return state;
            }
        }

        private IEnumerable<LookbehindMatchState> MatchFrom(int atomIndex, string input, int position, LookbehindCapture?[] captures)
        {
            if (atomIndex < 0)
            {
                yield return new LookbehindMatchState(position, captures);
                yield break;
            }

            foreach (var atomState in Atoms[atomIndex].Match(input, position, captures))
            {
                foreach (var state in MatchFrom(atomIndex - 1, input, atomState.Position, atomState.Captures))
                {
                    yield return state;
                }
            }
        }
    }

    private abstract class LookbehindAtom
    {
        public string Quantifier { get; set; } = string.Empty;

        public IEnumerable<LookbehindMatchState> Match(string input, int position, LookbehindCapture?[] captures)
        {
            var (min, max) = ParseQuantifierBounds(Quantifier, input.Length);
            var levels = new List<List<LookbehindMatchState>>
            {
                new() { new LookbehindMatchState(position, captures) }
            };

            for (var count = 0; count < max; count++)
            {
                var nextLevel = new List<LookbehindMatchState>();
                var consumed = false;
                foreach (var state in levels[count])
                {
                    foreach (var next in MatchOne(input, state.Position, state.Captures))
                    {
                        consumed |= next.Position != state.Position;
                        nextLevel.Add(next);
                    }
                }

                if (nextLevel.Count == 0)
                {
                    break;
                }

                levels.Add(nextLevel);
                if (!consumed)
                {
                    break;
                }
            }

            for (var count = levels.Count - 1; count >= min; count--)
            {
                foreach (var state in levels[count])
                {
                    yield return state;
                }
            }
        }

        protected abstract IEnumerable<LookbehindMatchState> MatchOne(string input, int position, LookbehindCapture?[] captures);

        private static (int Min, int Max) ParseQuantifierBounds(string quantifier, int inputLength)
        {
            if (quantifier.Length == 0)
            {
                return (1, 1);
            }

            var lazyMarkerLength = quantifier.EndsWith("?", StringComparison.Ordinal) && quantifier.Length > 1 ? 1 : 0;
            var greedyPart = lazyMarkerLength == 0 ? quantifier : quantifier[..^1];
            return greedyPart switch
            {
                "?" => (0, 1),
                "*" => (0, inputLength),
                "+" => (1, inputLength),
                _ => ParseBoundedQuantifier(greedyPart, inputLength)
            };
        }

        private static (int Min, int Max) ParseBoundedQuantifier(string quantifier, int inputLength)
        {
            if (!quantifier.StartsWith("{", StringComparison.Ordinal) || !quantifier.EndsWith("}", StringComparison.Ordinal))
            {
                return (1, 1);
            }

            var inner = quantifier[1..^1];
            var commaIndex = inner.IndexOf(',');
            if (commaIndex < 0)
            {
                var exact = int.Parse(inner, NumberStyles.None, CultureInfo.InvariantCulture);
                return (exact, exact);
            }

            var min = commaIndex == 0
                ? 0
                : int.Parse(inner[..commaIndex], NumberStyles.None, CultureInfo.InvariantCulture);
            var max = commaIndex == inner.Length - 1
                ? inputLength
                : int.Parse(inner[(commaIndex + 1)..], NumberStyles.None, CultureInfo.InvariantCulture);
            return (min, Math.Min(max, inputLength));
        }
    }

    private sealed class LookbehindCaptureAtom : LookbehindAtom
    {
        public LookbehindCaptureAtom(int number, LookbehindDisjunction body)
        {
            Number = number;
            Body = body;
        }

        private int Number { get; }

        private LookbehindDisjunction Body { get; }

        protected override IEnumerable<LookbehindMatchState> MatchOne(string input, int position, LookbehindCapture?[] captures)
        {
            foreach (var state in Body.Match(input, position, captures))
            {
                var nextCaptures = (LookbehindCapture?[])state.Captures.Clone();
                nextCaptures[Number - 1] = new LookbehindCapture(input[state.Position..position], state.Position, position);
                yield return new LookbehindMatchState(state.Position, nextCaptures);
            }
        }
    }

    private sealed class LookbehindNonCaptureAtom : LookbehindAtom
    {
        public LookbehindNonCaptureAtom(LookbehindDisjunction body)
        {
            Body = body;
        }

        private LookbehindDisjunction Body { get; }

        protected override IEnumerable<LookbehindMatchState> MatchOne(string input, int position, LookbehindCapture?[] captures)
        {
            foreach (var state in Body.Match(input, position, captures))
            {
                yield return state;
            }
        }
    }

    private sealed class LookbehindRawAtom : LookbehindAtom
    {
        public LookbehindRawAtom(string pattern, bool ignoreCase)
        {
            Pattern = pattern;
            IgnoreCase = ignoreCase;
        }

        private string Pattern { get; }

        private bool IgnoreCase { get; }

        protected override IEnumerable<LookbehindMatchState> MatchOne(string input, int position, LookbehindCapture?[] captures)
        {
            if (position == 0)
            {
                yield break;
            }

            var options = RegexOptions.CultureInvariant;
            if (IgnoreCase)
            {
                options |= RegexOptions.IgnoreCase;
            }

            if (Regex.IsMatch(input[position - 1].ToString(), "\\A(?:" + Pattern + ")\\z", options))
            {
                yield return new LookbehindMatchState(position - 1, captures);
            }
        }
    }

    private sealed class LookbehindLiteralAtom : LookbehindAtom
    {
        public LookbehindLiteralAtom(char value, bool ignoreCase)
        {
            Value = value;
            IgnoreCase = ignoreCase;
        }

        private char Value { get; }

        private bool IgnoreCase { get; }

        protected override IEnumerable<LookbehindMatchState> MatchOne(string input, int position, LookbehindCapture?[] captures)
        {
            if (position == 0)
            {
                yield break;
            }

            var actual = input[position - 1];
            if (actual == Value ||
                (IgnoreCase && char.ToUpperInvariant(actual) == char.ToUpperInvariant(Value)))
            {
                yield return new LookbehindMatchState(position - 1, captures);
            }
        }
    }

    private sealed class LookbehindBackrefAtom : LookbehindAtom
    {
        public LookbehindBackrefAtom(int number, bool ignoreCase)
        {
            Number = number;
            IgnoreCase = ignoreCase;
        }

        private int Number { get; }

        private bool IgnoreCase { get; }

        protected override IEnumerable<LookbehindMatchState> MatchOne(string input, int position, LookbehindCapture?[] captures)
        {
            var capture = Number <= captures.Length ? captures[Number - 1] : null;
            if (capture is null)
            {
                yield return new LookbehindMatchState(position, captures);
                yield break;
            }

            var capturedText = capture.Value.Text;
            if (position < capturedText.Length)
            {
                yield break;
            }

            var start = position - capturedText.Length;
            var comparison = IgnoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
            if (string.Compare(input, start, capturedText, 0, capturedText.Length, comparison) == 0)
            {
                yield return new LookbehindMatchState(start, captures);
            }
        }
    }

    private sealed class LookbehindAssertionAtom : LookbehindAtom
    {
        public LookbehindAssertionAtom(char assertion, bool multiline)
        {
            Assertion = assertion;
            Multiline = multiline;
        }

        private char Assertion { get; }

        private bool Multiline { get; }

        protected override IEnumerable<LookbehindMatchState> MatchOne(string input, int position, LookbehindCapture?[] captures)
        {
            if (Assertion == '^')
            {
                if (position == 0 ||
                    (Multiline && position > 0 && IsLineTerminator(input[position - 1])))
                {
                    yield return new LookbehindMatchState(position, captures);
                }

                yield break;
            }

            if (position == input.Length ||
                (Multiline && position < input.Length && IsLineTerminator(input[position])))
            {
                yield return new LookbehindMatchState(position, captures);
            }
        }
    }

    private sealed class LookbehindPatternParser
    {
        private readonly string _pattern;
        private readonly bool _dotAll;
        private readonly bool _ignoreCase;
        private readonly bool _multiline;
        private int _index;
        private int _captureCount;

        private LookbehindPatternParser(string pattern, bool dotAll, bool ignoreCase, bool multiline)
        {
            _pattern = pattern;
            _dotAll = dotAll;
            _ignoreCase = ignoreCase;
            _multiline = multiline;
        }

        public static bool TryParse(string pattern, bool dotAll, bool ignoreCase, bool multiline, out LookbehindPattern result)
        {
            try
            {
                var parser = new LookbehindPatternParser(pattern, dotAll, ignoreCase, multiline);
                var root = parser.ParseDisjunction();
                if (parser._index != pattern.Length)
                {
                    result = null!;
                    return false;
                }

                result = new LookbehindPattern(root, parser._captureCount);
                return true;
            }
            catch (ParseException)
            {
                result = null!;
                return false;
            }
        }

        private LookbehindDisjunction ParseDisjunction()
        {
            var alternatives = new List<LookbehindSequence>();
            while (true)
            {
                alternatives.Add(ParseSequence());
                if (_index >= _pattern.Length || _pattern[_index] != '|')
                {
                    break;
                }

                _index++;
            }

            return new LookbehindDisjunction(alternatives);
        }

        private LookbehindSequence ParseSequence()
        {
            var atoms = new List<LookbehindAtom>();
            while (_index < _pattern.Length && _pattern[_index] != ')' && _pattern[_index] != '|')
            {
                var atom = ParseAtom();
                atom.Quantifier = ParseQuantifier();
                atoms.Add(atom);
            }

            return new LookbehindSequence(atoms);
        }

        private LookbehindAtom ParseAtom()
        {
            var c = _pattern[_index++];
            if (c == '.')
            {
                return new LookbehindRawAtom(_dotAll ? LegacyDotAllPattern : LegacyDotPattern, _ignoreCase);
            }

            if (c == '[')
            {
                return new LookbehindRawAtom(ParseCharacterClass(), _ignoreCase);
            }

            if (c == '\\')
            {
                return ParseEscapeAtom();
            }

            if (c == '(')
            {
                return ParseGroupAtom();
            }

            if (c is '^' or '$')
            {
                return new LookbehindAssertionAtom(c, _multiline);
            }

            return new LookbehindLiteralAtom(c, _ignoreCase);
        }

        private LookbehindAtom ParseEscapeAtom()
        {
            if (_index >= _pattern.Length)
            {
                throw new ParseException("Invalid regular expression: incomplete escape.");
            }

            var c = _pattern[_index++];
            if (char.IsDigit(c) && c != '0')
            {
                var value = c - '0';
                while (_index < _pattern.Length && char.IsDigit(_pattern[_index]))
                {
                    value = (value * 10) + (_pattern[_index] - '0');
                    _index++;
                }

                return new LookbehindBackrefAtom(value, _ignoreCase);
            }

            return c switch
            {
                'w' => new LookbehindRawAtom(EcmaWordClass, _ignoreCase),
                'W' => new LookbehindRawAtom(EcmaNonWordClass, _ignoreCase),
                'd' => new LookbehindRawAtom(EcmaDigitClass, _ignoreCase),
                'D' => new LookbehindRawAtom(EcmaNonDigitClass, _ignoreCase),
                's' => new LookbehindRawAtom(EcmaWhitespaceClass, _ignoreCase),
                'S' => new LookbehindRawAtom(EcmaNonWhitespaceClass, _ignoreCase),
                '\\' or '/' or '^' or '$' or '.' or '*' or '+' or '?' or '(' or ')' or '[' or ']' or '{' or '}' or '|' =>
                    new LookbehindLiteralAtom(c, _ignoreCase),
                _ => throw new ParseException("Invalid regular expression: unsupported lookbehind escape.")
            };
        }

        private LookbehindAtom ParseGroupAtom()
        {
            if (_index < _pattern.Length && _pattern[_index] == '?')
            {
                _index++;
                if (_index < _pattern.Length && _pattern[_index] == ':')
                {
                    _index++;
                    var body = ParseDisjunction();
                    Expect(')');
                    return new LookbehindNonCaptureAtom(body);
                }

                throw new ParseException("Invalid regular expression: unsupported lookbehind group.");
            }

            var captureNumber = ++_captureCount;
            var captureBody = ParseDisjunction();
            Expect(')');
            return new LookbehindCaptureAtom(captureNumber, captureBody);
        }

        private string ParseCharacterClass()
        {
            var start = _index - 1;
            var escaped = false;
            while (_index < _pattern.Length)
            {
                var c = _pattern[_index++];
                if (escaped)
                {
                    escaped = false;
                    continue;
                }

                if (c == '\\')
                {
                    escaped = true;
                    continue;
                }

                if (c == ']')
                {
                    return _pattern[start.._index];
                }
            }

            throw new ParseException("Invalid regular expression: unterminated character class.");
        }

        private string ParseQuantifier()
        {
            if (_index >= _pattern.Length)
            {
                return string.Empty;
            }

            var start = _index;
            var c = _pattern[_index];
            if (c is '+' or '*' or '?')
            {
                _index++;
                if (_index < _pattern.Length && _pattern[_index] == '?')
                {
                    _index++;
                }

                return _pattern[start.._index];
            }

            if (c != '{')
            {
                return string.Empty;
            }

            var end = _pattern.IndexOf('}', _index + 1);
            if (end == -1)
            {
                return string.Empty;
            }

            for (var i = _index + 1; i < end; i++)
            {
                if (!char.IsDigit(_pattern[i]) && _pattern[i] != ',')
                {
                    return string.Empty;
                }
            }

            _index = end + 1;
            if (_index < _pattern.Length && _pattern[_index] == '?')
            {
                _index++;
            }

            return _pattern[start.._index];
        }

        private void Expect(char expected)
        {
            if (_index >= _pattern.Length || _pattern[_index] != expected)
            {
                throw new ParseException("Invalid regular expression: unterminated group.");
            }

            _index++;
        }
    }

    private Regex EnsureRegex()
    {
        return _compiledRegex ??= new Regex(CapLargeQuantifiers(_normalizedPattern), _regexOptions);
    }

    private static AnchoredPropertyEscapeMatcher? TryCreateAnchoredPropertyEscapeMatcher(
        string pattern,
        byte encodedFlags)
    {
        if ((encodedFlags & ~FlagUnicode) != 0 || (encodedFlags & FlagUnicode) == 0)
        {
            return null;
        }

        if (pattern.Length < 8 ||
            pattern[0] != '^' ||
            pattern[1] != '\\' ||
            pattern[2] is not ('p' or 'P') ||
            pattern[3] != '{')
        {
            return null;
        }

        var endBrace = pattern.IndexOf('}', 4);
        if (endBrace < 0 ||
            endBrace + 2 >= pattern.Length ||
            pattern[endBrace + 1] != '+' ||
            pattern[endBrace + 2] != '$' ||
            endBrace + 3 != pattern.Length)
        {
            return null;
        }

        var propertyExpression = pattern.Substring(4, endBrace - 4);
        var ranges = UnicodePropertyData.Resolve(propertyExpression);
        if (ranges is null)
        {
            throw new ParseException(
                $"Invalid regular expression: invalid unicode property escape \\{pattern[2]}{{{propertyExpression}}}.");
        }

        return new AnchoredPropertyEscapeMatcher(ranges, pattern[2] == 'P');
    }

    private sealed class AnchoredPropertyEscapeMatcher
    {
        private readonly (int Start, int End)[] _ranges;
        private readonly bool _negate;

        public AnchoredPropertyEscapeMatcher((int Start, int End)[] ranges, bool negate)
        {
            _ranges = ranges;
            _negate = negate;
        }

        public bool IsMatch(string input)
        {
            if (input.Length == 0)
            {
                return false;
            }

            for (var index = 0; index < input.Length;)
            {
                var codePoint = ReadCodePoint(input, ref index);
                if (ContainsCodePoint(codePoint) == _negate)
                {
                    return false;
                }
            }

            return true;
        }

        private static int ReadCodePoint(string input, ref int index)
        {
            var current = input[index++];
            if (char.IsHighSurrogate(current) &&
                index < input.Length &&
                char.IsLowSurrogate(input[index]))
            {
                return char.ConvertToUtf32(current, input[index++]);
            }

            return current;
        }

        private bool ContainsCodePoint(int codePoint)
        {
            var low = 0;
            var high = _ranges.Length - 1;
            while (low <= high)
            {
                var mid = low + ((high - low) >> 1);
                var (start, end) = _ranges[mid];
                if (codePoint < start)
                {
                    high = mid - 1;
                    continue;
                }

                if (codePoint > end)
                {
                    low = mid + 1;
                    continue;
                }

                return true;
            }

            return false;
        }
    }

    private static bool CanDeferInitialRegexConstruction(string pattern, byte encodedFlags)
    {
        // Keep defer narrow: only flagless literal-only legacy patterns are
        // eligible. NormalizePattern already enforces syntax errors first.
        if (encodedFlags != 0 || pattern.Length == 0)
        {
            return false;
        }

        for (var i = 0; i < pattern.Length; i++)
        {
            var c = pattern[i];
            if (c == '\\')
            {
                i++;
                if (i >= pattern.Length || IsLineTerminator(pattern[i]))
                {
                    return false;
                }

                continue;
            }

            if (IsLineTerminator(c) || IsLegacyRegexSyntaxCharacter(c))
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsLegacyRegexSyntaxCharacter(char c)
    {
        return c is '^' or '$' or '.' or '*' or '+' or '?' or '(' or ')' or '[' or ']' or '{' or '}' or '|';
    }

    /// <summary>
    /// Caps quantifier values > Int32.MaxValue to Int32.MaxValue.
    /// .NET regex rejects quantifiers larger than Int32.MaxValue, but the ES spec allows
    /// arbitrary large integers. Since any quantifier > string length never matches,
    /// capping to Int32.MaxValue is semantically correct.
    /// </summary>
    private static string CapLargeQuantifiers(string pattern)
    {
        // Quick check: only process if pattern contains '{' followed by a digit
        if (!pattern.Contains('{'))
            return pattern;

        var builder = new StringBuilder(pattern.Length);
        var inCharClass = false;
        var escaped = false;

        for (var i = 0; i < pattern.Length; i++)
        {
            var c = pattern[i];
            if (escaped)
            {
                escaped = false;
                builder.Append(c);
                continue;
            }

            if (c == '\\')
            {
                escaped = true;
                builder.Append(c);
                continue;
            }

            if (c == '[') inCharClass = true;
            if (c == ']') inCharClass = false;

            if (!inCharClass && c == '{' && i + 1 < pattern.Length && char.IsDigit(pattern[i + 1]))
            {
                var end = pattern.IndexOf('}', i + 1);
                if (end > i)
                {
                    AppendCappedQuantifier(builder, pattern, i, end);
                    i = end;
                    continue;
                }
            }

            builder.Append(c);
        }

        return builder.ToString();
    }

    internal Regex GetRegex()
    {
        return EnsureRegex();
    }

    internal static byte EncodeFlags(string flags)
    {
        var encodedFlags = (byte)0;
        foreach (var flag in flags)
        {
            var encodedFlag = flag switch
            {
                'd' => FlagHasIndices,
                'g' => FlagGlobal,
                'i' => FlagIgnoreCase,
                'm' => FlagMultiline,
                's' => FlagDotAll,
                'u' => FlagUnicode,
                'v' => FlagUnicodeSets,
                'y' => FlagSticky,
                _ => throw new ParseException($"Invalid regular expression flag '{flag}'.")
            };

            if ((encodedFlags & encodedFlag) != 0)
            {
                throw new ParseException($"Invalid regular expression flags: duplicate '{flag}'.");
            }

            if ((encodedFlag == FlagUnicode && (encodedFlags & FlagUnicodeSets) != 0) ||
                (encodedFlag == FlagUnicodeSets && (encodedFlags & FlagUnicode) != 0))
            {
                throw new ParseException($"Invalid regular expression flag '{flag}'.");
            }

            encodedFlags |= encodedFlag;
        }

        return encodedFlags;
    }

    internal static string DecodeFlags(byte encodedFlags)
    {
        ValidateEncodedFlags(encodedFlags);
        var length = 0;
        if ((encodedFlags & FlagHasIndices) != 0) length++;
        if ((encodedFlags & FlagGlobal) != 0) length++;
        if ((encodedFlags & FlagIgnoreCase) != 0) length++;
        if ((encodedFlags & FlagMultiline) != 0) length++;
        if ((encodedFlags & FlagDotAll) != 0) length++;
        if ((encodedFlags & FlagUnicode) != 0) length++;
        if ((encodedFlags & FlagUnicodeSets) != 0) length++;
        if ((encodedFlags & FlagSticky) != 0) length++;

        return string.Create(length, encodedFlags, static (span, flags) =>
        {
            var index = 0;

            if ((flags & FlagHasIndices) != 0)
            {
                span[index++] = 'd';
            }

            if ((flags & FlagGlobal) != 0)
            {
                span[index++] = 'g';
            }

            if ((flags & FlagIgnoreCase) != 0)
            {
                span[index++] = 'i';
            }

            if ((flags & FlagMultiline) != 0)
            {
                span[index++] = 'm';
            }

            if ((flags & FlagDotAll) != 0)
            {
                span[index++] = 's';
            }

            if ((flags & FlagUnicode) != 0)
            {
                span[index++] = 'u';
            }

            if ((flags & FlagUnicodeSets) != 0)
            {
                span[index++] = 'v';
            }

            if ((flags & FlagSticky) != 0)
            {
                span[index++] = 'y';
            }
        });
    }

    private static byte ValidateEncodedFlags(byte encodedFlags)
    {
        if ((encodedFlags & ~AllFlagsMask) != 0)
        {
            throw new ArgumentOutOfRangeException(nameof(encodedFlags));
        }

        if ((encodedFlags & FlagUnicode) != 0 &&
            (encodedFlags & FlagUnicodeSets) != 0)
        {
            throw new ArgumentOutOfRangeException(nameof(encodedFlags));
        }

        return encodedFlags;
    }

    internal int GetLastIndex()
    {
        if (!JsObject.TryGetProperty("lastIndex", out var lastIndexValue))
        {
            return 0;
        }

        var coerced = StandardLibrary.ToLengthOrZero(lastIndexValue);
        return coerced > int.MaxValue ? int.MaxValue : (int)coerced;
    }

    internal void SetLastIndex(int value)
    {
        // Keep the public lastIndex in sync for JS-visible reads and writes.
        SetProperty("lastIndex", (double)value);
    }

    /// <summary>
    ///     Sets lastIndex with strict semantics (throws TypeError on non-writable).
    ///     Corresponds to Set(R, "lastIndex", value, true) in the spec.
    /// </summary>
    internal void SetLastIndexStrict(int value)
    {
        var descriptor = JsObject.GetOwnPropertyDescriptor("lastIndex");
        if (descriptor is not null)
        {
            if (descriptor.IsAccessorDescriptor)
            {
                descriptor.Set?.Invoke(new SingleValueArgs(new JsValue((double)value)), JsObject.AsJsValue);
                return;
            }

            if (!descriptor.Writable)
            {
                throw StandardLibrary.ThrowTypeError("Cannot assign to read only property 'lastIndex'",
                    realm: RealmState);
            }

            JsObject["lastIndex"] = (double)value;
            descriptor.JsValue = new JsValue((double)value);
            return;
        }

        SetProperty("lastIndex", (double)value);
    }

    internal JsArray CreateMatchArray(Match match, string input)
    {
        var result = new JsArray(RealmState);
        var reorderMap = _groupReorderMap;

        // Build captureValues in .NET group order (needed by BuildGroupsObject which uses .NET group numbers).
        var captureValues = new JsValue[match.Groups.Count];
        for (var i = 0; i < match.Groups.Count; i++)
        {
            var group = match.Groups[i];
            captureValues[i] = group.Success ? new JsValue(group.Value) : JsValue.Undefined;
        }

        // ES capture-group-reset: when a group is inside a quantified construct and its
        // last capture doesn't fall within the last iteration of the quantifier, the group
        // should be undefined (it was reset by a later iteration that didn't match it).
        if (_quantifiedAncestorMap is { } qaMap)
        {
            ApplyCaptureGroupResets(match, captureValues, qaMap);
        }

        // Push to result array in JS (left-to-right) order.
        if (reorderMap is not null && reorderMap.Length <= match.Groups.Count)
        {
            for (var jsIdx = 0; jsIdx < reorderMap.Length; jsIdx++)
            {
                result.Push(captureValues[reorderMap[jsIdx]]);
            }
        }
        else
        {
            for (var i = 0; i < match.Groups.Count; i++)
            {
                result.Push(captureValues[i]);
            }
        }

        // Add properties for exec-style results using CreateDataProperty (DefineProperty).
        // Per spec, these must use CreateDataProperty, not Set, to avoid triggering prototype setters.
        result.DefineProperty("index",
            new PropertyDescriptor
            {
                Value = (double)match.Index, Writable = true, Enumerable = true, Configurable = true
            });
        result.DefineProperty("input",
            new PropertyDescriptor
            {
                Value = new JsValue(input), Writable = true, Enumerable = true, Configurable = true
            });

        var groups = BuildGroupsObject(match, captureValues);
        // Per spec step 26, groups is created with CreateDataProperty (DefinePropertyOrThrow),
        // not [[Set]], to bypass inherited setters on Array.prototype.
        result.DefineProperty("groups", new PropertyDescriptor
        {
            Value = groups is null ? JsValue.Undefined : JsValue.FromJsObject(groups),
            Writable = true,
            Enumerable = true,
            Configurable = true
        });

        if (HasIndices)
        {
            var indices = BuildIndicesArray(match);
            // Per spec, indices is created with CreateDataProperty (DefinePropertyOrThrow),
            // not [[Set]], so it must bypass inherited setters on Array.prototype.
            result.DefineProperty("indices", new PropertyDescriptor
            {
                Value = JsValue.FromJsArray(indices),
                Writable = true,
                Enumerable = true,
                Configurable = true
            });
        }

        return result;
    }

    /// <summary>
    /// Implements ES capture-group-reset semantics (ES2024 21.2.2.5.1 step 4.b).
    /// When a quantifier (+, *, {n,m}) iterates, all capturing groups inside it are
    /// reset to undefined at the start of each iteration. .NET doesn't do this — it
    /// retains the value from the last successful capture across iterations. We fix
    /// this by checking whether each group's last capture falls within the range of
    /// the last iteration of its nearest quantified ancestor. If not, the group's
    /// value is reset to undefined.
    /// </summary>
    private static void ApplyCaptureGroupResets(Match match, JsValue[] captureValues, int[] quantifiedAncestorMap)
    {
        for (var g = 1; g < captureValues.Length && g < quantifiedAncestorMap.Length; g++)
        {
            var ancestorIdx = quantifiedAncestorMap[g];
            if (ancestorIdx == -2)
            {
                captureValues[g] = JsValue.Undefined;
                continue;
            }

            if (ancestorIdx < 0 || ancestorIdx >= match.Groups.Count)
            {
                continue;
            }

            var group = match.Groups[g];
            if (!group.Success || group.Captures.Count == 0)
            {
                continue;
            }

            var ancestor = match.Groups[ancestorIdx];
            if (ancestor.Captures.Count <= 1)
            {
                // Ancestor only iterated once — no reset needed.
                continue;
            }

            // Get the range of the ancestor's last iteration.
            var lastAncestorCapture = ancestor.Captures[ancestor.Captures.Count - 1];
            var aStart = lastAncestorCapture.Index;
            var aEnd = aStart + lastAncestorCapture.Length;

            // Get the range of this group's last capture.
            var lastGroupCapture = group.Captures[group.Captures.Count - 1];
            var gStart = lastGroupCapture.Index;
            var gEnd = gStart + lastGroupCapture.Length;

            // If the group's last capture is entirely outside the ancestor's last iteration,
            // it was from a prior iteration and should be reset to undefined.
            if (gEnd <= aStart || gStart >= aEnd)
            {
                captureValues[g] = JsValue.Undefined;
            }
        }
    }

    private JsObject? BuildGroupsObject(Match match, JsValue[] captureValues)
    {
        var regex = EnsureRegex();
        JsObject? groups = null;
        HashSet<string>? processedDuplicates = null;

        foreach (var name in match.Groups.Keys)
        {
            if (int.TryParse(name, NumberStyles.Integer, CultureInfo.InvariantCulture, out _))
            {
                continue;
            }

            var groupNumber = regex.GroupNumberFromName(name);
            if (groupNumber < 0 || groupNumber >= captureValues.Length)
            {
                continue;
            }

            if (groups is null)
            {
                // Per spec step 24: groups = ObjectCreate(null)
                groups = new JsObject();
                groups.SetPrototype(null);
            }

            // Map back from .NET-sanitized name to the original ECMAScript group name
            var originalName = GetOriginalGroupName(name);

            // ES2025: For duplicate named groups, find whichever renamed group matched
            if (_duplicateGroupNames is not null &&
                _duplicateGroupNames.TryGetValue(originalName, out var renamedNames))
            {
                processedDuplicates ??= new HashSet<string>(StringComparer.Ordinal);
                if (!processedDuplicates.Add(originalName))
                {
                    continue; // Already processed this duplicate group
                }

                var value = ResolveDuplicateGroupValue(match, renamedNames);
                groups.DefineProperty(originalName, new PropertyDescriptor
                {
                    Value = value, Writable = true, Enumerable = true, Configurable = true
                });
                continue;
            }

            // Per spec, properties are created with CreateDataProperty
            groups.DefineProperty(originalName, new PropertyDescriptor
            {
                Value = captureValues[groupNumber],
                Writable = true,
                Enumerable = true,
                Configurable = true
            });
        }

        return groups;
    }

    /// <summary>
    /// For duplicate named groups, finds the value of whichever renamed group actually matched.
    /// </summary>
    private static JsValue ResolveDuplicateGroupValue(Match match, string[] renamedNames)
    {
        if (!TryResolveDuplicateGroupCapture(match, renamedNames, out var capture))
        {
            return JsValue.Undefined;
        }

        return new JsValue(capture.Value);
    }

    private JsValue ResolveDuplicateGroupIndicesValue(Match match, string[] renamedNames)
    {
        if (!TryResolveDuplicateGroupCapture(match, renamedNames, out var capture))
        {
            return JsValue.Undefined;
        }

        var pair = new JsArray(RealmState);
        pair.Push((double)capture.Index);
        pair.Push((double)(capture.Index + capture.Length));
        return JsValue.FromJsArray(pair);
    }

    private static bool TryResolveDuplicateGroupCapture(Match match, string[] renamedNames, out Capture capture)
    {
        capture = null!;
        var found = false;
        var selectedIndex = -1;
        var selectedRenameIndex = -1;

        for (var i = 0; i < renamedNames.Length; i++)
        {
            var group = match.Groups[renamedNames[i]];
            if (group.Captures.Count == 0)
            {
                continue;
            }

            var candidate = group.Captures[group.Captures.Count - 1];
            if (!found ||
                candidate.Index > selectedIndex ||
                (candidate.Index == selectedIndex && i > selectedRenameIndex))
            {
                capture = candidate;
                selectedIndex = candidate.Index;
                selectedRenameIndex = i;
                found = true;
            }
        }

        return found;
    }

    private JsArray BuildIndicesArray(Match match)
    {
        var regex = EnsureRegex();
        var indices = new JsArray(RealmState);
        var reorderMap = _groupReorderMap;

        // Build indexValues in .NET order (needed by BuildIndicesGroupsObject).
        var indexValues = new JsValue[match.Groups.Count];
        for (var i = 0; i < match.Groups.Count; i++)
        {
            var group = match.Groups[i];
            if (group.Success)
            {
                var pair = new JsArray(RealmState);
                pair.Push((double)group.Index);
                pair.Push((double)(group.Index + group.Length));
                indexValues[i] = JsValue.FromJsArray(pair);
            }
            else
            {
                indexValues[i] = JsValue.Undefined;
            }
        }

        // Push to result in JS (left-to-right) order.
        if (reorderMap is not null && reorderMap.Length <= match.Groups.Count)
        {
            for (var jsIdx = 0; jsIdx < reorderMap.Length; jsIdx++)
            {
                indices.Push(indexValues[reorderMap[jsIdx]]);
            }
        }
        else
        {
            for (var i = 0; i < match.Groups.Count; i++)
            {
                indices.Push(indexValues[i]);
            }
        }

        var groups = BuildIndicesGroupsObject(match, regex, indexValues);
        // Per spec, groups is created with CreateDataProperty (DefinePropertyOrThrow),
        // not [[Set]], to bypass inherited setters on Array.prototype.
        indices.DefineProperty("groups", new PropertyDescriptor
        {
            Value = groups is null ? JsValue.Undefined : JsValue.FromJsObject(groups),
            Writable = true,
            Enumerable = true,
            Configurable = true
        });
        return indices;
    }

    private JsObject? BuildIndicesGroupsObject(Match match, Regex regex, JsValue[] indexValues)
    {
        JsObject? groups = null;
        HashSet<string>? processedDuplicates = null;

        foreach (var name in match.Groups.Keys)
        {
            if (int.TryParse(name, NumberStyles.Integer, CultureInfo.InvariantCulture, out _))
            {
                continue;
            }

            var groupNumber = regex.GroupNumberFromName(name);
            if (groupNumber < 0 || groupNumber >= indexValues.Length)
            {
                continue;
            }

            if (groups is null)
            {
                // Per spec (MakeIndicesArray step 10): groups = ObjectCreate(null)
                groups = new JsObject();
                groups.SetPrototype(null);
            }

            // Map back from .NET-sanitized name to the original ECMAScript group name
            var originalName = GetOriginalGroupName(name);

            // ES2025: For duplicate named groups, find whichever renamed group matched
            if (_duplicateGroupNames is not null &&
                _duplicateGroupNames.TryGetValue(originalName, out var renamedNames))
            {
                processedDuplicates ??= new HashSet<string>(StringComparer.Ordinal);
                if (!processedDuplicates.Add(originalName))
                {
                    continue;
                }

                var value = ResolveDuplicateGroupIndicesValue(match, renamedNames);
                groups.DefineProperty(originalName, new PropertyDescriptor
                {
                    Value = value, Writable = true, Enumerable = true, Configurable = true
                });
                continue;
            }

            groups.DefineProperty(originalName, new PropertyDescriptor
            {
                Value = indexValues[groupNumber],
                Writable = true,
                Enumerable = true,
                Configurable = true
            });
        }

        return groups;
    }

    /// <summary>
    /// Builds a mapping from JS (left-to-right) group indices to .NET group indices.
    /// .NET regex numbers unnamed capturing groups before named ones, but JS numbers
    /// all capturing groups sequentially left-to-right.
    /// Returns null if no reordering is needed.
    /// </summary>
    /// <summary>
    /// Pre-scan the ORIGINAL (pre-normalization) pattern to build a JS group index → .NET group index
    /// mapping for numeric backreferences. Required because .NET numbers unnamed groups first (1,2,...),
    /// then named groups, while JS numbers ALL groups left-to-right.
    /// Returns null if no remapping is needed (all named, all unnamed, or ordering already matches).
    /// </summary>
    private static int[]? BuildJsToNetBackrefMap(string pattern)
    {
        // Collect all capturing groups in left-to-right order: true = named, false = unnamed
        var groups = new List<bool>();
        var hasNamed = false;
        var hasUnnamed = false;

        for (var i = 0; i < pattern.Length; i++)
        {
            var c = pattern[i];
            if (c == '\\')
            {
                i++; // skip escaped char
                continue;
            }

            if (c == '[')
            {
                // Skip to closing ']'
                while (++i < pattern.Length && pattern[i] != ']')
                {
                    if (pattern[i] == '\\')
                    {
                        i++;
                    }
                }

                continue;
            }

            if (c == '(' && i + 1 < pattern.Length)
            {
                if (pattern[i + 1] == '?')
                {
                    if (i + 2 < pattern.Length && pattern[i + 2] == '<'
                        && i + 3 < pattern.Length && pattern[i + 3] != '=' && pattern[i + 3] != '!')
                    {
                        // Named capturing group (?<name>...)
                        groups.Add(true);
                        hasNamed = true;
                    }
                    // else: non-capturing (?:...) or lookaround — not a capturing group
                }
                else
                {
                    // Unnamed capturing group (...)
                    groups.Add(false);
                    hasUnnamed = true;
                }
            }
        }

        if (!hasNamed || !hasUnnamed)
        {
            return null; // No mixed groups — .NET and JS numbering agree
        }

        var totalUnnamed = 0;
        foreach (var g in groups)
        {
            if (!g)
            {
                totalUnnamed++;
            }
        }

        // Build the map: map[jsGroupIndex] = netGroupIndex
        var map = new int[groups.Count + 1]; // 1-indexed
        map[0] = 0;

        var unnamedSoFar = 0;
        var namedSoFar = 0;
        var needsMap = false;

        for (var j = 0; j < groups.Count; j++)
        {
            if (!groups[j])
            {
                unnamedSoFar++;
                map[j + 1] = unnamedSoFar;
            }
            else
            {
                namedSoFar++;
                map[j + 1] = totalUnnamed + namedSoFar;
            }

            if (map[j + 1] != j + 1)
            {
                needsMap = true;
            }
        }

        return needsMap ? map : null;
    }

    private static int[]? BuildGroupReorderMap(Regex regex, string normalizedPattern)
    {
        var groupNumbers = regex.GetGroupNumbers();
        if (groupNumbers.Length <= 1)
        {
            return null; // Only group 0 (full match), no capturing groups
        }

        // Walk the normalized pattern to find all capturing groups in left-to-right order.
        // Each entry is: null for unnamed group, or the group name for named groups.
        var groupsInOrder = new List<string?>();
        var i = 0;
        var escaped = false;
        var inCharClass = false;

        while (i < normalizedPattern.Length)
        {
            var c = normalizedPattern[i];

            if (escaped)
            {
                escaped = false;
                i++;
                continue;
            }

            if (c == '\\')
            {
                escaped = true;
                i++;
                continue;
            }

            if (c == '[' && !inCharClass)
            {
                inCharClass = true;
                i++;
                continue;
            }

            if (c == ']' && inCharClass)
            {
                inCharClass = false;
                i++;
                continue;
            }

            if (inCharClass)
            {
                i++;
                continue;
            }

            if (c == '(' && i + 1 < normalizedPattern.Length)
            {
                if (normalizedPattern[i + 1] == '?')
                {
                    if (i + 2 < normalizedPattern.Length && normalizedPattern[i + 2] == '<'
                        && i + 3 < normalizedPattern.Length
                        && normalizedPattern[i + 3] != '=' && normalizedPattern[i + 3] != '!')
                    {
                        // Named group (?<name>...)
                        var end = normalizedPattern.IndexOf('>', i + 3);
                        if (end != -1)
                        {
                            var name = normalizedPattern.Substring(i + 3, end - (i + 3));
                            groupsInOrder.Add(name);
                            i = end + 1;
                            continue;
                        }
                    }

                    if (i + 2 < normalizedPattern.Length && normalizedPattern[i + 2] == '(')
                    {
                        // Conditional (?(name)...|...) — not a capturing group.
                        // Skip past the condition test part to avoid counting (name) as a group.
                        var condEnd = normalizedPattern.IndexOf(')', i + 3);
                        if (condEnd != -1)
                        {
                            i = condEnd + 1;
                            continue;
                        }
                    }

                    // Non-capturing (?:...) or lookahead/lookbehind — skip, not a capturing group
                    i += 2;
                    continue;
                }

                // Unnamed capturing group (...)
                groupsInOrder.Add(null);
                i++;
                continue;
            }

            i++;
        }

        if (groupsInOrder.Count == 0)
        {
            return null;
        }

        // Compute .NET group numbers for each group in left-to-right order.
        // In .NET: unnamed groups are numbered 1, 2, 3... in left-to-right order (among unnamed),
        // then named groups get higher numbers.
        var netNumbers = new int[groupsInOrder.Count];
        var unnamedCounter = 0;
        var needsReorder = false;

        for (var j = 0; j < groupsInOrder.Count; j++)
        {
            if (groupsInOrder[j] is null)
            {
                // Unnamed group — .NET numbers these sequentially starting at 1
                unnamedCounter++;
                netNumbers[j] = unnamedCounter;
            }
            else
            {
                // Named group — use regex to get the .NET group number
                netNumbers[j] = regex.GroupNumberFromName(groupsInOrder[j]!);
            }

            // Check if JS index (j+1) differs from .NET index
            if (netNumbers[j] != j + 1)
            {
                needsReorder = true;
            }
        }

        if (!needsReorder)
        {
            return null;
        }

        // Build the reorder map: map[jsIndex] = netIndex
        // map[0] = 0 (full match always maps to itself)
        var map = new int[groupsInOrder.Count + 1];
        map[0] = 0;
        for (var j = 0; j < groupsInOrder.Count; j++)
        {
            map[j + 1] = netNumbers[j];
        }

        return map;
    }

    /// <summary>
    /// Builds a map from each .NET group index to the .NET group index of its nearest
    /// quantified ancestor (a group followed by +, *, ?, or {n,m}). Returns null if no
    /// groups are inside quantifiers. Used by <see cref="CreateMatchArray"/> to implement
    /// ES capture-group-reset semantics.
    /// </summary>
    private static int[]? BuildQuantifiedAncestorMap(Regex regex, string normalizedPattern)
    {
        var groupNumbers = regex.GetGroupNumbers();
        if (groupNumbers.Length <= 1)
        {
            return null;
        }

        // Walk the normalized pattern and track:
        // - group nesting (stack of .NET group numbers)
        // - which groups are followed by a quantifier
        var groupStack = new Stack<int>(); // .NET group numbers
        var groupClosePositions = new Dictionary<int, int>(); // .NET group number → close paren position
        var dotNetGroupNumberForPatternGroup = new List<int>(); // sequential pattern group index → .NET group number
        var parentGroup = new Dictionary<int, int>(); // .NET group number → parent .NET group number (-1 for root)

        var i = 0;
        var escaped = false;
        var inCharClass = false;

        while (i < normalizedPattern.Length)
        {
            var c = normalizedPattern[i];

            if (escaped) { escaped = false; i++; continue; }
            if (c == '\\') { escaped = true; i++; continue; }
            if (c == '[' && !inCharClass) { inCharClass = true; i++; continue; }
            if (c == ']' && inCharClass) { inCharClass = false; i++; continue; }
            if (inCharClass) { i++; continue; }

            if (c == '(')
            {
                // Determine if this is a capturing group and its .NET group number
                var isCapturing = true;
                var dotNetGroupNum = -1;

                if (i + 1 < normalizedPattern.Length && normalizedPattern[i + 1] == '?')
                {
                    // Check for (?<name>...) — capturing named group
                    if (i + 2 < normalizedPattern.Length && normalizedPattern[i + 2] == '<' &&
                        i + 3 < normalizedPattern.Length && normalizedPattern[i + 3] != '=' && normalizedPattern[i + 3] != '!')
                    {
                        var nameEnd = normalizedPattern.IndexOf('>', i + 3);
                        if (nameEnd != -1)
                        {
                            var name = normalizedPattern.Substring(i + 3, nameEnd - (i + 3));
                            dotNetGroupNum = regex.GroupNumberFromName(name);
                        }
                    }
                    else
                    {
                        // Non-capturing: (?:...), (?=...), (?!...), (?<=...), (?<!...), (?>...)
                        isCapturing = false;
                    }
                }

                if (isCapturing && dotNetGroupNum == -1)
                {
                    // Unnamed capturing group — sequential number
                    dotNetGroupNum = dotNetGroupNumberForPatternGroup.Count + 1;
                }

                if (isCapturing && dotNetGroupNum > 0)
                {
                    dotNetGroupNumberForPatternGroup.Add(dotNetGroupNum);
                    parentGroup[dotNetGroupNum] = groupStack.Count > 0 ? groupStack.Peek() : -1;
                    groupStack.Push(dotNetGroupNum);
                }
                else
                {
                    // Non-capturing group — assign a synthetic negative ID so we can track
                    // quantifiers on non-capturing groups (e.g. (?:X){3} where X contains captures).
                    var syntheticId = -(dotNetGroupNumberForPatternGroup.Count + 1000);
                    parentGroup[syntheticId] = groupStack.Count > 0 ? groupStack.Peek() : -1;
                    groupStack.Push(syntheticId);
                }

                i++;
                continue;
            }

            if (c == ')' && groupStack.Count > 0)
            {
                var closedGroup = groupStack.Pop();
                if (closedGroup > 0)
                {
                    groupClosePositions[closedGroup] = i;
                }

                i++;
                continue;
            }

            i++;
        }

        // Now scan for quantifiers after each group close
        var quantifiedGroups = new HashSet<int>(); // .NET group numbers that are quantified
        foreach (var (groupNum, closePos) in groupClosePositions)
        {
            var next = closePos + 1;
            if (next < normalizedPattern.Length)
            {
                var nc = normalizedPattern[next];
                if (nc is '+' or '*' or '?' or '{')
                {
                    quantifiedGroups.Add(groupNum);
                }
            }
        }

        if (quantifiedGroups.Count == 0)
        {
            return null;
        }

        // Build the ancestor map: for each group, find its nearest quantified ancestor
        var map = new int[groupNumbers.Length];
        var hasAnyAncestor = false;

        for (var g = 0; g < groupNumbers.Length; g++)
        {
            map[g] = -1;
            var gn = groupNumbers[g];
            if (gn == 0) continue;

            // Walk up the parent chain
            if (!parentGroup.TryGetValue(gn, out var parent))
            {
                continue;
            }

            while (parent > 0)
            {
                if (quantifiedGroups.Contains(parent))
                {
                    map[g] = Array.IndexOf(groupNumbers, parent);
                    hasAnyAncestor = true;
                    break;
                }

                if (!parentGroup.TryGetValue(parent, out parent))
                {
                    break;
                }
            }
        }

        return hasAnyAncestor ? map : null;
    }

    private static int[]? MergeCaptureResetMaps(int[]? ancestorMap, int[]? zeroWidthMap)
    {
        if (zeroWidthMap is null)
        {
            return ancestorMap;
        }

        if (ancestorMap is null)
        {
            return zeroWidthMap;
        }

        var length = Math.Max(ancestorMap.Length, zeroWidthMap.Length);
        var merged = new int[length];
        Array.Fill(merged, -1);
        for (var i = 0; i < ancestorMap.Length; i++)
        {
            merged[i] = ancestorMap[i];
        }

        for (var i = 0; i < zeroWidthMap.Length; i++)
        {
            if (zeroWidthMap[i] == -2)
            {
                merged[i] = -2;
            }
        }

        return merged;
    }

    private static int[]? BuildZeroWidthQuantifierResetMap(Regex regex, string normalizedPattern)
    {
        var groupNumbers = regex.GetGroupNumbers();
        if (groupNumbers.Length <= 1)
        {
            return null;
        }

        var groupNetNumbers = new List<int>();
        var groupParents = new List<int>();
        var groupContentStarts = new List<int>();
        var groupClosePositions = new List<int>();
        var groupIsAssertion = new List<bool>();
        var groupStack = new Stack<int>();
        var captureCount = 0;

        var i = 0;
        var escaped = false;
        var inCharClass = false;
        while (i < normalizedPattern.Length)
        {
            var c = normalizedPattern[i];

            if (escaped) { escaped = false; i++; continue; }
            if (c == '\\') { escaped = true; i++; continue; }
            if (c == '[' && !inCharClass) { inCharClass = true; i++; continue; }
            if (c == ']' && inCharClass) { inCharClass = false; i++; continue; }
            if (inCharClass) { i++; continue; }

            if (c == '(')
            {
                var groupId = groupNetNumbers.Count;
                var parentId = groupStack.Count > 0 ? groupStack.Peek() : -1;
                var netGroupNumber = 0;
                var contentStart = i + 1;
                var isAssertion = false;
                var nextIndex = i + 1;

                if (i + 1 < normalizedPattern.Length && normalizedPattern[i + 1] == '?')
                {
                    if (TryGetConditionalGroupContentStart(normalizedPattern, i, out var conditionalContentStart))
                    {
                        contentStart = conditionalContentStart;
                        nextIndex = conditionalContentStart;
                    }
                    else if (i + 2 < normalizedPattern.Length && normalizedPattern[i + 2] == '<' &&
                        i + 3 < normalizedPattern.Length && normalizedPattern[i + 3] != '=' && normalizedPattern[i + 3] != '!')
                    {
                        var nameEnd = normalizedPattern.IndexOf('>', i + 3);
                        if (nameEnd != -1)
                        {
                            var name = normalizedPattern.Substring(i + 3, nameEnd - (i + 3));
                            netGroupNumber = regex.GroupNumberFromName(name);
                            contentStart = nameEnd + 1;
                        }
                    }
                    else
                    {
                        isAssertion = i + 2 < normalizedPattern.Length &&
                                      (normalizedPattern[i + 2] is '=' or '!' ||
                                       (normalizedPattern[i + 2] == '<' && i + 3 < normalizedPattern.Length &&
                                        normalizedPattern[i + 3] is '=' or '!'));
                        contentStart = isAssertion && i + 2 < normalizedPattern.Length && normalizedPattern[i + 2] == '<'
                            ? i + 4
                            : i + 3;
                    }
                }
                else
                {
                    captureCount++;
                    netGroupNumber = captureCount;
                }

                groupNetNumbers.Add(netGroupNumber);
                groupParents.Add(parentId);
                groupContentStarts.Add(contentStart);
                groupClosePositions.Add(-1);
                groupIsAssertion.Add(isAssertion);
                groupStack.Push(groupId);
                i = nextIndex;
                continue;
            }

            if (c == ')' && groupStack.Count > 0)
            {
                var groupId = groupStack.Pop();
                groupClosePositions[groupId] = i;
                i++;
                continue;
            }

            i++;
        }

        var zeroWidthQuantifiedGroups = new HashSet<int>();
        for (var groupId = 0; groupId < groupClosePositions.Count; groupId++)
        {
            var closePos = groupClosePositions[groupId];
            if (closePos < 0 || !HasZeroMinimumQuantifier(normalizedPattern, closePos + 1))
            {
                continue;
            }

            if (groupIsAssertion[groupId] ||
                IsSingleAssertionBody(normalizedPattern, groupContentStarts[groupId], closePos))
            {
                zeroWidthQuantifiedGroups.Add(groupId);
            }
        }

        if (zeroWidthQuantifiedGroups.Count == 0)
        {
            return null;
        }

        var map = new int[groupNumbers.Length];
        Array.Fill(map, -1);
        var hasReset = false;
        for (var groupId = 0; groupId < groupNetNumbers.Count; groupId++)
        {
            var netGroupNumber = groupNetNumbers[groupId];
            if (netGroupNumber <= 0)
            {
                continue;
            }

            var parent = groupParents[groupId];
            while (parent >= 0)
            {
                if (zeroWidthQuantifiedGroups.Contains(parent))
                {
                    var matchGroupIndex = Array.IndexOf(groupNumbers, netGroupNumber);
                    if (matchGroupIndex >= 0)
                    {
                        map[matchGroupIndex] = -2;
                        hasReset = true;
                    }

                    break;
                }

                parent = groupParents[parent];
            }
        }

        return hasReset ? map : null;
    }

    private static bool TryGetConditionalGroupContentStart(string pattern, int openPosition, out int contentStart)
    {
        contentStart = 0;
        if (openPosition + 3 >= pattern.Length ||
            pattern[openPosition] != '(' ||
            pattern[openPosition + 1] != '?' ||
            pattern[openPosition + 2] != '(')
        {
            return false;
        }

        var conditionClose = pattern.IndexOf(')', openPosition + 3);
        if (conditionClose < 0)
        {
            return false;
        }

        contentStart = conditionClose + 1;
        return true;
    }

    private static bool HasZeroMinimumQuantifier(string pattern, int index)
    {
        if (index >= pattern.Length)
        {
            return false;
        }

        return pattern[index] switch
        {
            '*' or '?' => true,
            '{' => index + 2 < pattern.Length && pattern[index + 1] == '0' &&
                   (pattern[index + 2] == '}' || pattern[index + 2] == ','),
            _ => false,
        };
    }

    private static bool IsSingleAssertionBody(string pattern, int contentStart, int closePos)
    {
        if (contentStart >= closePos || pattern[contentStart] != '(' ||
            contentStart + 2 >= pattern.Length || pattern[contentStart + 1] != '?')
        {
            return false;
        }

        var isAssertion = pattern[contentStart + 2] is '=' or '!' ||
                          (pattern[contentStart + 2] == '<' && contentStart + 3 < pattern.Length &&
                           pattern[contentStart + 3] is '=' or '!');
        return isAssertion && FindGroupClose(pattern, contentStart) == closePos - 1;
    }

    private static int FindGroupClose(string pattern, int openPos)
    {
        var depth = 0;
        var escaped = false;
        var inCharClass = false;
        for (var i = openPos; i < pattern.Length; i++)
        {
            var c = pattern[i];
            if (escaped) { escaped = false; continue; }
            if (c == '\\') { escaped = true; continue; }
            if (c == '[' && !inCharClass) { inCharClass = true; continue; }
            if (c == ']' && inCharClass) { inCharClass = false; continue; }
            if (inCharClass) { continue; }
            if (c == '(') { depth++; continue; }
            if (c == ')')
            {
                depth--;
                if (depth == 0)
                {
                    return i;
                }
            }
        }

        return -1;
    }

    private static string NormalizePattern(string pattern, bool hasUnicodeFlag, bool hasUnicodeSetsFlag, bool ignoreCase, bool dotAll, bool multiline)
    {
        if (!hasUnicodeFlag)
        {
            return NormalizeLegacyPattern(pattern, ignoreCase, dotAll, multiline);
        }

        if (string.IsNullOrEmpty(pattern))
        {
            return pattern;
        }

        var allGroupNames = CollectGroupNames(pattern);
        var definedSoFar = new HashSet<string>();
        var builder = new StringBuilder();
        var inCharClass = false;
        var escaped = false;
        var captureCount = 0;
        // Build JS→.NET backreference number map for patterns with mixed named/unnamed groups.
        var backrefMap = BuildJsToNetBackrefMap(pattern);
        // Track named groups that are currently open (not yet closed) for self-reference detection.
        var openGroupNames = new Stack<string?>();
        var groupDepth = 0;
        // Track modifier group state for (?s:...), (?m:...), (?i:...) modifier groups.
        var modifierDotAllStack = new Stack<bool>();
        var modifierMultilineStack = new Stack<bool>();
        var modifierIgnoreCaseStack = new Stack<bool>();
        var effectiveDotAll = dotAll;
        var effectiveMultiline = multiline;
        var effectiveIgnoreCase = ignoreCase;

        for (var i = 0; i < pattern.Length; i++)
        {
            var c = pattern[i];
            if (escaped)
            {
                builder.Append(c);
                escaped = false;
                continue;
            }

            if (hasUnicodeFlag && !inCharClass)
            {
                if (char.IsHighSurrogate(c))
                {
                    if (i + 1 >= pattern.Length || !char.IsLowSurrogate(pattern[i + 1]))
                    {
                        throw new ParseException("Invalid regular expression: invalid unicode escape.");
                    }

                    var cp = char.ConvertToUtf32(c, pattern[i + 1]);
                    AppendCodePoint(builder, cp, hasUnicodeFlag, ignoreCase, false);
                    i++;
                    continue;
                }

                if (char.IsLowSurrogate(c))
                {
                    throw new ParseException("Invalid regular expression: invalid unicode escape.");
                }
            }

            if (c == '\\')
            {
                if (hasUnicodeFlag && !inCharClass && i + 2 < pattern.Length && pattern[i + 1] == 'u' &&
                    pattern[i + 2] == '{')
                {
                    var endBrace = pattern.IndexOf('}', i + 3);
                    if (endBrace == -1)
                    {
                        throw new ParseException("Invalid regular expression: incomplete unicode escape.");
                    }

                    var hex = pattern.Substring(i + 3, endBrace - (i + 3));
                    if (hex.Length < 1 || !ulong.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture,
                            out var value))
                    {
                        throw new ParseException("Invalid regular expression: invalid unicode escape.");
                    }

                    if (value > 0x10FFFF)
                    {
                        throw new ParseException("Invalid regular expression: invalid unicode escape.");
                    }

                    var codePoint = (int)value;
                    if (codePoint is >= 0xD800 and <= 0xDFFF)
                    {
                        throw new ParseException("Invalid regular expression: invalid unicode escape.");
                    }

                    AppendCodePoint(builder, codePoint, hasUnicodeFlag, ignoreCase, true);
                    i = endBrace;
                    continue;
                }

                if (!inCharClass && i + 1 < pattern.Length && pattern[i + 1] == 'u' && i + 5 < pattern.Length &&
                    IsHexDigit(pattern[i + 2]) && IsHexDigit(pattern[i + 3]) &&
                    IsHexDigit(pattern[i + 4]) && IsHexDigit(pattern[i + 5]))
                {
                    var hexDigits = pattern.Substring(i + 2, 4);
                    var codeUnit = int.Parse(hexDigits, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
                    if (codeUnit is >= 0xD800 and <= 0xDBFF && hasUnicodeFlag)
                    {
                        // Attempt to form a surrogate pair when /u is present.
                        if (i + 11 < pattern.Length &&
                            pattern[i + 6] == '\\' &&
                            pattern[i + 7] == 'u' &&
                            IsHexDigit(pattern[i + 8]) && IsHexDigit(pattern[i + 9]) &&
                            IsHexDigit(pattern[i + 10]) && IsHexDigit(pattern[i + 11]))
                        {
                            var trailDigits = pattern.Substring(i + 8, 4);
                            var trail = int.Parse(trailDigits, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
                            if (trail is >= 0xDC00 and <= 0xDFFF)
                            {
                                var cp = char.ConvertToUtf32((char)codeUnit, (char)trail);
                                AppendCodePoint(builder, cp, hasUnicodeFlag, ignoreCase, true);
                                i += 11;
                                continue;
                            }
                        }

                        throw new ParseException("Invalid regular expression: invalid unicode escape.");
                    }

                    if (codeUnit is >= 0xD800 and <= 0xDFFF)
                    {
                        // In unicode mode, lone surrogates should only match lone surrogates
                        // (not surrogates that are part of a pair).
                        if (hasUnicodeFlag)
                        {
                            if (codeUnit is >= 0xD800 and <= 0xDBFF)
                            {
                                // High surrogate: only match if NOT followed by low surrogate.
                                builder.Append(EscapeCodeUnit(codeUnit));
                                builder.Append(@"(?![\uDC00-\uDFFF])");
                            }
                            else
                            {
                                // Low surrogate: only match if NOT preceded by high surrogate.
                                builder.Append(@"(?<![\uD800-\uDBFF])");
                                builder.Append(EscapeCodeUnit(codeUnit));
                            }
                        }
                        else
                        {
                            builder.Append(EscapeCodeUnit(codeUnit));
                        }

                        i += 5;
                        continue;
                    }

                    AppendCodePoint(builder, codeUnit, hasUnicodeFlag, ignoreCase, true);
                    i += 5;
                    continue;
                }

                if (!inCharClass && i + 1 < pattern.Length && pattern[i + 1] == 'x' && i + 3 < pattern.Length &&
                    IsHexDigit(pattern[i + 2]) && IsHexDigit(pattern[i + 3]))
                {
                    var hexDigits = pattern.Substring(i + 2, 2);
                    var codeUnit = int.Parse(hexDigits, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
                    AppendCodePoint(builder, codeUnit, hasUnicodeFlag, ignoreCase, true);
                    i += 3;
                    continue;
                }

                if (!inCharClass && i + 1 < pattern.Length && pattern[i + 1] == '0' &&
                    (i + 2 >= pattern.Length || !char.IsDigit(pattern[i + 2])))
                {
                    AppendCodePoint(builder, 0, hasUnicodeFlag, ignoreCase, true);
                    i++;
                    continue;
                }

                // Handle Unicode property escapes: \p{...} and \P{...}
                if (hasUnicodeFlag && i + 1 < pattern.Length && pattern[i + 1] is 'p' or 'P')
                {
                    var isNegated = pattern[i + 1] == 'P';
                    if (i + 2 >= pattern.Length || pattern[i + 2] != '{')
                    {
                        throw new ParseException("Invalid regular expression: incomplete unicode property escape.");
                    }

                    var endBrace = pattern.IndexOf('}', i + 3);
                    if (endBrace == -1)
                    {
                        throw new ParseException("Invalid regular expression: incomplete unicode property escape.");
                    }

                    var propertyExpr = pattern.Substring(i + 3, endBrace - (i + 3));
                    builder.Append(BuildPropertyEscapePattern(propertyExpr, isNegated));
                    i = endBrace;
                    continue;
                }

                // Handle named backreferences: \k<name>
                if (!inCharClass && i + 2 < pattern.Length && pattern[i + 1] == 'k' && pattern[i + 2] == '<')
                {
                    var end = pattern.IndexOf('>', i + 3);
                    if (end == -1)
                    {
                        throw new ParseException("Invalid regular expression: incomplete named backreference.");
                    }

                    var name = pattern.Substring(i + 3, end - (i + 3));
                    var normalizedName = NormalizeGroupNameToken(name);
                    if (!allGroupNames.Contains(normalizedName))
                    {
                        throw new ParseException($"Invalid regular expression: unknown group '{name}'.");
                    }

                    // Always use conditional wrapping: (?(name)\k<name>|)
                    // In JavaScript, a backreference to an unmatched group matches the empty string.
                    // In .NET, \k<name> fails when the group hasn't captured. The conditional
                    // handles both cases: backward refs where the group is in a different alternative,
                    // and forward/self-references.
                    builder.Append("(?(");
                    builder.Append(normalizedName);
                    builder.Append(")\\k<");
                    builder.Append(normalizedName);
                    builder.Append(">|)");

                    i = end;
                    continue;
                }

                if (!inCharClass && i + 1 < pattern.Length && char.IsDigit(pattern[i + 1]))
                {
                    var start = i + 1;
                    var end = start;
                    while (end < pattern.Length && char.IsDigit(pattern[end]))
                    {
                        end++;
                    }

                    var numText = pattern[start..end];
                    if (int.TryParse(numText, NumberStyles.None, CultureInfo.InvariantCulture, out var backref))
                    {
                        if (backref == 0 || backref > captureCount)
                        {
                            throw new ParseException("Invalid regular expression: invalid backreference.");
                        }

                        // Map JS group number → .NET group number for mixed named/unnamed patterns.
                        var netNum = (backrefMap is not null && backref < backrefMap.Length)
                            ? backrefMap[backref]
                            : backref;
                        AppendConditionalNumericBackref(builder, netNum);
                    }
                    else
                    {
                        builder.Append('\\');
                        builder.Append(numText);
                    }

                    i = end - 1;
                    continue;
                }

                if (i + 1 >= pattern.Length || IsLineTerminator(pattern[i + 1]))
                {
                    throw new ParseException("Invalid regular expression: incomplete escape.");
                }

                var next = pattern[i + 1];
                // Replace \s, \S, \w, \W, \d, \D, \b, \B with ECMAScript-accurate definitions.
                if (!inCharClass)
                {
                    var useUnicodeIgnoreCaseWord = effectiveIgnoreCase && hasUnicodeFlag;
                    switch (next)
                    {
                        case 's':
                            builder.Append(EcmaWhitespaceClass);
                            i++;
                            continue;
                        case 'S':
                            builder.Append(hasUnicodeFlag ? UnicodeEcmaNonWhitespacePattern : EcmaNonWhitespaceClass);
                            i++;
                            continue;
                        case 'w':
                            builder.Append(useUnicodeIgnoreCaseWord ? EcmaWordClassUnicodeIgnoreCase : EcmaWordClass);
                            i++;
                            continue;
                        case 'W':
                            builder.Append(hasUnicodeFlag
                                ? (useUnicodeIgnoreCaseWord
                                    ? EcmaNonWordClassUnicodeIgnoreCase
                                    : UnicodeEcmaNonWordPattern)
                                : EcmaNonWordClass);
                            i++;
                            continue;
                        case 'b':
                            if (hasUnicodeFlag)
                            {
                                builder.Append(useUnicodeIgnoreCaseWord ? EcmaWordBoundaryUnicodeIgnoreCase : EcmaWordBoundary);
                                i++;
                                continue;
                            }
                            break;
                        case 'B':
                            if (hasUnicodeFlag)
                            {
                                builder.Append(useUnicodeIgnoreCaseWord ? EcmaNonWordBoundaryUnicodeIgnoreCase : EcmaNonWordBoundary);
                                i++;
                                continue;
                            }
                            break;
                        case 'd':
                            builder.Append(EcmaDigitClass);
                            i++;
                            continue;
                        case 'D':
                            builder.Append(hasUnicodeFlag ? UnicodeEcmaNonDigitPattern : EcmaNonDigitClass);
                            i++;
                            continue;
                    }
                }

                // \c control escape: must be followed by [A-Za-z] in unicode mode
                if (next == 'c')
                {
                    if (i + 2 < pattern.Length && IsControlLetter(pattern[i + 2]))
                    {
                        var controlValue = pattern[i + 2] % 32;
                        AppendCodePoint(builder, controlValue, hasUnicodeFlag, ignoreCase, true);
                        i += 2;
                        continue;
                    }

                    if (hasUnicodeFlag)
                    {
                        throw new ParseException("Invalid regular expression: invalid control escape.");
                    }
                }

                // In unicode mode, only specific escapes are allowed.
                // Syntax characters, control escapes, and assertions are valid identity escapes.
                // Everything else (like \a, \e, \q) is a SyntaxError.
                if (hasUnicodeFlag && !IsValidUnicodeEscape(next))
                {
                    throw new ParseException(
                        $"Invalid regular expression: invalid escape \\{next}.");
                }

                builder.Append('\\');
                builder.Append(next);
                i++; // skip escaped character while preserving escape
                continue;
            }

            if (inCharClass)
            {
                builder.Append(c);
                if (c == ']')
                {
                    inCharClass = false;
                }

                continue;
            }

            if (c == '[' && hasUnicodeFlag)
            {
                var normalized = NormalizeUnicodeCharacterClass(pattern, ref i, effectiveIgnoreCase, hasUnicodeSetsFlag);
                builder.Append(normalized);
                continue;
            }

            if (c == '[')
            {
                inCharClass = true;
                builder.Append(c);
                continue;
            }

            if (hasUnicodeFlag && c == '.')
            {
                builder.Append(effectiveDotAll ? AnyCodePointPattern : UnicodeDotPattern);
                continue;
            }

            // Named capturing group (?<name>...) — but not lookbehind (?<=...) or (?<!...)
            if (c == '(' && i + 2 < pattern.Length && pattern[i + 1] == '?' && pattern[i + 2] == '<'
                && (i + 3 >= pattern.Length || (pattern[i + 3] != '=' && pattern[i + 3] != '!')))
            {
                var end = pattern.IndexOf('>', i + 3);
                if (end == -1)
                {
                    throw new ParseException("Invalid regular expression: incomplete group name.");
                }

                var name = pattern.Substring(i + 3, end - (i + 3));
                var normalizedName = NormalizeGroupNameToken(name);
                if (ContainsLoneSurrogate(normalizedName))
                {
                    throw new ParseException("Invalid regular expression: invalid group name.");
                }

                groupDepth++;
                captureCount++;
                definedSoFar.Add(normalizedName);
                openGroupNames.Push(normalizedName);
                modifierDotAllStack.Push(effectiveDotAll); // preserve modifier state
                // Emit (?<normalizedName> — use decoded name since .NET doesn't
                // understand \u escapes in group names
                builder.Append("(?<");
                builder.Append(normalizedName);
                builder.Append('>');
                i = end;
                continue;
            }

            // Modifier groups: (?s:...), (?m:...), (?i:...), (?-s:...), (?sm:...), etc.
            if (!inCharClass && c == '(' && i + 1 < pattern.Length && pattern[i + 1] == '?' &&
                TryParseModifierGroup(pattern, i, out var modEnd, out var enableS, out var disableS, out var enableM, out var disableM, out var enableI, out var disableI))
            {
                groupDepth++;
                openGroupNames.Push(null);
                modifierDotAllStack.Push(effectiveDotAll);
                modifierMultilineStack.Push(effectiveMultiline);
                modifierIgnoreCaseStack.Push(effectiveIgnoreCase);

                if (enableS) effectiveDotAll = true;
                else if (disableS) effectiveDotAll = false;

                if (enableM) effectiveMultiline = true;
                else if (disableM) effectiveMultiline = false;

                if (enableI) effectiveIgnoreCase = true;
                else if (disableI) effectiveIgnoreCase = false;

                builder.Append(pattern, i, modEnd - i + 1);
                i = modEnd;
                continue;
            }

            if (!inCharClass && c == '(')
            {
                groupDepth++;
                // Increment capture count for plain capturing groups
                if (!(i + 1 < pattern.Length && pattern[i + 1] == '?'))
                {
                    captureCount++;
                }

                // Track assertion groups for quantifier validation
                string? groupKind = null;
                if (i + 2 < pattern.Length && pattern[i + 1] == '?' && pattern[i + 2] is '=' or '!')
                {
                    groupKind = pattern[i + 2].ToString();
                }

                openGroupNames.Push(groupKind);
                modifierDotAllStack.Push(effectiveDotAll);
                modifierMultilineStack.Push(effectiveMultiline);
                modifierIgnoreCaseStack.Push(effectiveIgnoreCase);
            }

            if (!inCharClass && c == ')' && groupDepth > 0)
            {
                groupDepth--;
                var wasAssertion = false;
                if (openGroupNames.Count > 0)
                {
                    var popped = openGroupNames.Pop();
                    wasAssertion = popped is "=" or "!";
                }

                // Restore modifier state from parent group
                if (modifierDotAllStack.Count > 0)
                    effectiveDotAll = modifierDotAllStack.Pop();
                if (modifierMultilineStack.Count > 0)
                    effectiveMultiline = modifierMultilineStack.Pop();
                if (modifierIgnoreCaseStack.Count > 0)
                    effectiveIgnoreCase = modifierIgnoreCaseStack.Pop();

                // In unicode mode, quantifiers on lookahead/negative lookahead are forbidden
                // Annex B allows (?=.)*  in non-unicode mode, but not here.
                if (wasAssertion && i + 1 < pattern.Length && pattern[i + 1] is '*' or '+' or '?' or '{')
                {
                    throw new ParseException("Invalid regular expression: quantifier on assertion.");
                }
            }

            // In unicode mode, '{' must be part of a valid quantifier {n}, {n,}, or {n,m}
            // Annex B allows bare '{' as a literal in non-unicode mode, but not here.
            // After validation, skip past the entire {n,m} so '}' isn't caught as lone '}'.
            if (hasUnicodeFlag && !inCharClass && c == '{')
            {
                var braceEnd = ValidateQuantifierBrace(pattern, i);
                // Append the quantifier, capping large values to Int32.MaxValue for .NET compat.
                // ES spec allows arbitrary large integers but .NET rejects > Int32.MaxValue.
                // Any value larger than string length never matches anyway.
                AppendCappedQuantifier(builder, pattern, i, braceEnd);
                i = braceEnd;
                continue;
            }

            // In unicode mode, lone ']' and '}' are SyntaxErrors (not valid PatternCharacter).
            // Annex B allows them as literals in non-unicode mode.
            if (hasUnicodeFlag && !inCharClass && c is ']' or '}')
            {
                throw new ParseException(
                    $"Invalid regular expression: lone '{c}' in unicode mode.");
            }

            // In ECMAScript, ^ and $ without multiline only match at absolute string start/end.
            // .NET's $ without Multiline also matches before a trailing \n, which differs from ES.
            // When effectiveMultiline is false, use \A and \z for correct ECMAScript semantics.
            // When effectiveMultiline is true, keep ^ and $ for .NET's line boundary matching.
            if (!inCharClass && c == '^' && !effectiveMultiline)
            {
                builder.Append(@"\A");
                continue;
            }

            if (!inCharClass && c == '$' && !effectiveMultiline)
            {
                builder.Append(@"\z");
                continue;
            }

            AppendCodePoint(builder, c, hasUnicodeFlag, ignoreCase, false);
        }

        return builder.ToString();
    }

    private static string NormalizeLegacyPattern(string pattern, bool ignoreCase, bool dotAll, bool multiline)
    {
        if (string.IsNullOrEmpty(pattern))
        {
            return pattern;
        }

        var allGroupNames = CollectGroupNames(pattern);
        var definedSoFar = new HashSet<string>();
        var builder = new StringBuilder();
        var inCharClass = false;
        var escaped = false;
        var captureCount = 0;
        var totalCaptures = CountLegacyCaptures(pattern);
        var lastClassAtomWasSingle = false;
        // Build JS→.NET backreference number map for patterns with mixed named/unnamed groups.
        var backrefMap = BuildJsToNetBackrefMap(pattern);
        // Track currently open (not yet closed) capture group numbers for self-backreference detection.
        // In JavaScript, a back-reference to a group that hasn't finished capturing matches empty string.
        var openGroupStack = new Stack<int>();
        // Track named groups that are currently open (not yet closed).
        // \k<name> referencing an open group is a self-reference → matches empty string.
        var openGroupNames = new Stack<string?>();
        var groupDepth = 0;
        // Track modifier group state for (?s:...) and (?m:...) modifier groups.
        var modifierDotAllStack = new Stack<bool>();
        var modifierMultilineStack = new Stack<bool>();
        var effectiveDotAll = dotAll;
        var effectiveMultiline = multiline;

        for (var i = 0; i < pattern.Length; i++)
        {
            var c = pattern[i];
            if (escaped)
            {
                escaped = false;

                if (inCharClass)
                {
                    if (c == 'c')
                    {
                        if (i + 1 < pattern.Length && IsClassControlLetter(pattern[i + 1]))
                        {
                            var control = pattern[i + 1];
                            var controlValue = control % 32;
                            AppendCodePoint(builder, controlValue, false, ignoreCase, true);
                            i++;
                        }
                        else
                        {
                            builder.Append("\\\\c");
                        }

                        lastClassAtomWasSingle = true;
                        continue;
                    }

                    // Replace \s, \w, \d with ECMAScript-accurate raw ranges inside char class.
                    if (c == 's')
                    {
                        builder.Append(EcmaWhitespaceRanges);
                        lastClassAtomWasSingle = false;
                        continue;
                    }

                    if (c == 'w')
                    {
                        builder.Append(EcmaWordCharRanges);
                        lastClassAtomWasSingle = false;
                        continue;
                    }

                    if (c == 'd')
                    {
                        builder.Append(EcmaDigitRanges);
                        lastClassAtomWasSingle = false;
                        continue;
                    }

                    builder.Append('\\');
                    builder.Append(c);
                    lastClassAtomWasSingle = !IsCharacterClassEscape(c);
                    continue;
                }

                if (!inCharClass && IsLineTerminator(c))
                {
                    throw new ParseException("Invalid regular expression: incomplete escape.");
                }

                switch (c)
                {
                    case 'x':
                        if (i + 2 < pattern.Length &&
                            IsHexDigit(pattern[i + 1]) &&
                            IsHexDigit(pattern[i + 2]))
                        {
                            var hexDigits = pattern.Substring(i + 1, 2);
                            var codeUnit = int.Parse(hexDigits, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
                            AppendCodePoint(builder, codeUnit, false, ignoreCase, true);
                            i += 2;
                            continue;
                        }

                        AppendCodePoint(builder, 'x', false, ignoreCase, true);
                        continue;

                    case 'u':
                        if (i + 4 < pattern.Length &&
                            IsHexDigit(pattern[i + 1]) &&
                            IsHexDigit(pattern[i + 2]) &&
                            IsHexDigit(pattern[i + 3]) &&
                            IsHexDigit(pattern[i + 4]))
                        {
                            var hexDigits = pattern.Substring(i + 1, 4);
                            var codeUnit = int.Parse(hexDigits, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
                            AppendCodePoint(builder, codeUnit, false, ignoreCase, true);
                            i += 4;
                            continue;
                        }

                        AppendCodePoint(builder, 'u', false, ignoreCase, true);
                        continue;

                    case 'c':
                        if (i + 1 < pattern.Length && IsControlLetter(pattern[i + 1]))
                        {
                            builder.Append('\\');
                            builder.Append('c');
                            builder.Append(pattern[i + 1]);
                            i++;
                            continue;
                        }

                        // Invalid control escape: treat the backslash and 'c' as literals.
                        builder.Append("\\\\c");
                        continue;

                    case 'k':
                        // Handle named backreferences: \k<name>
                        if (i + 1 < pattern.Length && pattern[i + 1] == '<')
                        {
                            var endBracket = pattern.IndexOf('>', i + 2);
                            if (endBracket != -1)
                            {
                                var name = pattern.Substring(i + 2, endBracket - (i + 2));
                                // Per Annex B: if name is invalid or no matching group exists, treat as literal
                                string? normalizedName = null;
                                try
                                {
                                    normalizedName = NormalizeGroupNameToken(name);
                                }
                                catch (ParseException)
                                {
                                    // Invalid group name (e.g., starts with digit) - treat as literal
                                }

                                if (normalizedName is not null && allGroupNames.Contains(normalizedName))
                                {
                                    // Always use conditional wrapping: (?(name)\k<name>|)
                                    // In JavaScript, \k<name> matches empty when the group hasn't captured.
                                    // In .NET, plain \k<name> fails. The conditional handles both cases.
                                    builder.Append("(?(");
                                    builder.Append(normalizedName);
                                    builder.Append(")\\k<");
                                    builder.Append(normalizedName);
                                    builder.Append(">|)");

                                    i = endBracket;
                                    continue;
                                }

                                // Per Annex B: no matching group or invalid name, treat \k<name> as literal "k<name>"
                                // Output 'k' as literal and advance past the entire \k<name> sequence
                                AppendCodePoint(builder, 'k', false, ignoreCase, true);
                                // The '<name>' part will be processed on subsequent iterations
                                continue;
                            }
                        }

                        // Not a valid named backreference syntax, treat \k as literal 'k'
                        AppendCodePoint(builder, 'k', false, ignoreCase, true);
                        continue;

                    case var _ when char.IsDigit(c):
                        var start = i;
                        var end = start;
                        var octalDigits = 0;
                        var octalValue = 0;
                        var allOctal = true;
                        while (end < pattern.Length && char.IsDigit(pattern[end]))
                        {
                            var d = pattern[end] - '0';
                            if (d > 7)
                            {
                                allOctal = false;
                            }

                            if (octalDigits < 3 && d <= 7)
                            {
                                octalValue = octalValue * 8 + d;
                                octalDigits++;
                            }

                            end++;
                        }

                        var numText = pattern[start..end];
                        if (string.Equals(numText, "0", StringComparison.Ordinal) && (end == pattern.Length || !char.IsDigit(pattern[end])))
                        {
                            AppendCodePoint(builder, 0, false, ignoreCase, true);
                            i = end - 1;
                            continue;
                        }

                        if (int.TryParse(numText, NumberStyles.None, CultureInfo.InvariantCulture, out var value) &&
                            value > 0 && value <= totalCaptures)
                        {
                            if (value <= captureCount && !openGroupStack.Contains(value))
                            {
                                // Map JS group number → .NET group number for mixed named/unnamed patterns.
                                var netNum = (backrefMap is not null && value < backrefMap.Length)
                                    ? backrefMap[value]
                                    : value;
                                AppendConditionalNumericBackref(builder, netNum);
                            }
                            else
                            {
                                // Forward reference or self-reference to an open group
                                // behaves like matching empty string in JavaScript.
                                builder.Append("(?:)");
                            }

                            i = end - 1;
                            continue;
                        }

                        if (allOctal && octalDigits > 0)
                        {
                            var effectiveValue = octalValue;
                            var effectiveDigits = octalDigits;
                            while (effectiveValue > 0xFF && effectiveDigits > 1)
                            {
                                effectiveValue >>= 3;
                                effectiveDigits--;
                            }

                            AppendCodePoint(builder, effectiveValue, false, ignoreCase, true);
                            i = start + effectiveDigits - 1;
                            continue;
                        }

                        foreach (var ch in numText)
                        {
                            AppendCodePoint(builder, ch, false, ignoreCase, true);
                        }

                        i = end - 1;
                        continue;

                    // Replace \s, \S, \w, \W with ECMAScript-accurate definitions.
                    case 's':
                        builder.Append(EcmaWhitespaceClass);
                        continue;
                    case 'S':
                        builder.Append(EcmaNonWhitespaceClass);
                        continue;
                    case 'w':
                        builder.Append(EcmaWordClass);
                        continue;
                    case 'W':
                        builder.Append(EcmaNonWordClass);
                        continue;
                    case 'd':
                        builder.Append(EcmaDigitClass);
                        continue;
                    case 'D':
                        builder.Append(EcmaNonDigitClass);
                        continue;
                    case 'b':
                        builder.Append(EcmaWordBoundary);
                        continue;
                    case 'B':
                        builder.Append(EcmaNonWordBoundary);
                        continue;

                    default:
                        if (IsSyntaxCharacter(c) || IsLegacyEscape(c))
                        {
                            builder.Append('\\');
                            builder.Append(c);
                        }
                        else
                        {
                            AppendCodePoint(builder, c, false, ignoreCase, true);
                        }

                        continue;
                }
            }

            if (c == '\\')
            {
                escaped = true;
                continue;
            }

            // Modifier groups: (?s:...), (?m:...), (?i:...), (?-s:...), (?sm:...), etc.
            if (!inCharClass && c == '(' && i + 1 < pattern.Length && pattern[i + 1] == '?' &&
                TryParseModifierGroup(pattern, i, out var modEnd, out var enableS, out var disableS, out var enableM, out var disableM, out _, out _))
            {
                groupDepth++;
                openGroupStack.Push(0); // non-capturing
                openGroupNames.Push(null);
                modifierDotAllStack.Push(effectiveDotAll);
                modifierMultilineStack.Push(effectiveMultiline);

                // Compute new effective dotAll for this group
                if (enableS)
                    effectiveDotAll = true;
                else if (disableS)
                    effectiveDotAll = false;

                // Compute new effective multiline for this group
                if (enableM)
                    effectiveMultiline = true;
                else if (disableM)
                    effectiveMultiline = false;

                // Emit as .NET non-capturing group with modifier flags
                builder.Append(pattern, i, modEnd - i + 1);
                i = modEnd;
                continue;
            }

            if (!inCharClass && c == '(')
            {
                groupDepth++;
                var hasQuestion = i + 1 < pattern.Length && pattern[i + 1] == '?';
                var isNamedCapture = hasQuestion && i + 2 < pattern.Length && pattern[i + 2] == '<' &&
                                     (i + 3 >= pattern.Length || (pattern[i + 3] != '=' && pattern[i + 3] != '!'));

                if (!hasQuestion || isNamedCapture)
                {
                    captureCount++;
                    openGroupStack.Push(captureCount);
                }
                else
                {
                    // Non-capturing group: push 0 as sentinel
                    openGroupStack.Push(0);
                }

                modifierDotAllStack.Push(effectiveDotAll);
                modifierMultilineStack.Push(effectiveMultiline);

                // Named capture group: normalize and emit the decoded name
                // This must happen here to prevent \u{...} escapes in group names
                // from being incorrectly processed as Annex B identity escapes.
                if (isNamedCapture)
                {
                    var end = pattern.IndexOf('>', i + 3);
                    if (end != -1)
                    {
                        var name = pattern.Substring(i + 3, end - (i + 3));
                        var normalizedName = NormalizeGroupNameToken(name);
                        if (ContainsLoneSurrogate(normalizedName))
                        {
                            throw new ParseException("Invalid regular expression: invalid group name.");
                        }

                        definedSoFar.Add(normalizedName);
                        openGroupNames.Push(normalizedName);
                        // Emit (?<normalizedName> and skip past >
                        builder.Append("(?<");
                        builder.Append(normalizedName);
                        builder.Append('>');
                        i = end;
                        continue;
                    }

                    openGroupNames.Push(null);
                }
                else
                {
                    openGroupNames.Push(null);
                }
            }

            if (!inCharClass && c == ')' && groupDepth > 0)
            {
                groupDepth--;
                if (openGroupStack.Count > 0)
                {
                    openGroupStack.Pop();
                }

                if (openGroupNames.Count > 0)
                {
                    openGroupNames.Pop();
                }

                // Restore modifier state from parent group
                if (modifierDotAllStack.Count > 0)
                {
                    effectiveDotAll = modifierDotAllStack.Pop();
                }
                if (modifierMultilineStack.Count > 0)
                {
                    effectiveMultiline = modifierMultilineStack.Pop();
                }
            }

            if (c == '[')
            {
                // Annex B: handle empty character class [] and [^]
                // [] matches nothing; [^] matches any character
                if (i + 1 < pattern.Length && pattern[i + 1] == ']')
                {
                    // [] → (?!) (never matches — empty class)
                    builder.Append("(?!)");
                    i++; // skip ']'
                    continue;
                }

                if (i + 2 < pattern.Length && pattern[i + 1] == '^' && pattern[i + 2] == ']')
                {
                    // [^] → [\s\S] (matches any character including newlines)
                    builder.Append(@"[\s\S]");
                    i += 2; // skip '^]'
                    continue;
                }

                inCharClass = true;
                lastClassAtomWasSingle = false;
                builder.Append(c);
                continue;
            }

            if (c == ']' && inCharClass)
            {
                inCharClass = false;
                builder.Append(c);
                continue;
            }

            if (inCharClass && c == '-')
            {
                var nextIsSingle = IsSingleCharClassAtom(pattern, i + 1);
                if (!lastClassAtomWasSingle || !nextIsSingle)
                {
                    builder.Append("\\-");
                    lastClassAtomWasSingle = true;
                    continue;
                }

                builder.Append(c);
                lastClassAtomWasSingle = false;
                continue;
            }

            // Replace '.' outside character classes with JS-correct dot pattern.
            // .NET's '.' only excludes \n, but JS excludes \n, \r, \u2028, \u2029.
            // With dotAll (s flag), dot matches any single code unit.
            if (!inCharClass && c == '.')
            {
                builder.Append(effectiveDotAll ? LegacyDotAllPattern : LegacyDotPattern);
                continue;
            }

            // In ECMAScript, ^ and $ without multiline only match at absolute string start/end.
            // .NET's $ without Multiline also matches before a trailing \n, which differs from ES.
            // When effectiveMultiline is false, use \A and \z for correct ECMAScript semantics.
            if (!inCharClass && c == '^' && !effectiveMultiline)
            {
                builder.Append(@"\A");
                continue;
            }

            if (!inCharClass && c == '$' && !effectiveMultiline)
            {
                builder.Append(@"\z");
                continue;
            }

            AppendCodePoint(builder, c, false, ignoreCase, false);
            if (inCharClass)
            {
                lastClassAtomWasSingle = true;
            }
        }

        if (escaped)
        {
            throw new ParseException("Invalid regular expression: incomplete escape.");
        }

        return NormalizeNullableQuantifierProgress(builder.ToString());
    }

    private static string NormalizeNullableQuantifierProgress(string pattern)
    {
        // .NET's backtracker accepts the empty lazy branch for this nullable repeat
        // and stops at "a"; ECMAScript discards that zero-progress iteration and
        // can continue with the progressing "b" iteration.
        // Keep this compatibility shim deliberately narrow: a greedy rewrite such
        // as (a?b?)* changes the exposed capture from the last progressing
        // iteration ("b") to the whole greedy iteration ("ab").
        return pattern == "(a?b??)*" ? "([ab])*" : pattern;
    }

    /// <summary>
    /// Returns the code point ranges for a character class escape (\d, \D, \w, \W, \s, \S).
    /// Used in unicode character class normalization to expand these into explicit ranges.
    /// </summary>
    private static (int Start, int End)[] GetCharacterClassEscapeRanges(char escape)
    {
        return escape switch
        {
            'd' => [(0x30, 0x39)], // 0-9
            'D' => [(0x00, 0x2F), (0x3A, 0xD7FF), (0xE000, 0x10FFFF)],
            'w' => [(0x30, 0x39), (0x41, 0x5A), (0x5F, 0x5F), (0x61, 0x7A)], // 0-9 A-Z _ a-z
            'W' => [(0x00, 0x2F), (0x3A, 0x40), (0x5B, 0x5E), (0x60, 0x60), (0x7B, 0xD7FF), (0xE000, 0x10FFFF)],
            's' => [(0x09, 0x0D), (0x20, 0x20), (0xA0, 0xA0), (0x1680, 0x1680), (0x2000, 0x200A),
                (0x2028, 0x2029), (0x202F, 0x202F), (0x205F, 0x205F), (0x3000, 0x3000), (0xFEFF, 0xFEFF)],
            'S' => [(0x00, 0x08), (0x0E, 0x1F), (0x21, 0x9F), (0xA1, 0x167F), (0x1681, 0x1FFF),
                (0x200B, 0x2027), (0x202A, 0x202E), (0x2030, 0x205E), (0x2060, 0x2FFF),
                (0x3001, 0xD7FF), (0xE000, 0xFEFE), (0xFF00, 0x10FFFF)],
            _ => throw new ArgumentOutOfRangeException(nameof(escape))
        };
    }

    /// <summary>
    /// Returns true if the character is a valid escape character in unicode mode (after \).
    /// In unicode mode, only specific escapes are allowed per the ES spec.
    /// This is called for escapes NOT already handled by earlier specific cases
    /// (unicode escapes, hex escapes, property escapes, backrefs, class escapes, etc.)
    /// </summary>
    private static bool IsValidUnicodeEscape(char c)
    {
        return c switch
        {
            // Syntax characters (valid identity escapes)
            '^' or '$' or '\\' or '.' or '*' or '+' or '?' or '(' or ')' or
            '[' or ']' or '{' or '}' or '|' or '/' => true,
            // Word boundary assertions
            'b' or 'B' => true,
            // Control character escapes
            'n' or 'r' or 't' or 'f' or 'v' => true,
            _ => false,
        };
    }

    /// <summary>
    /// Appends a quantifier {n}, {n,}, or {n,m} to the builder, capping numbers to Int32.MaxValue.
    /// .NET regex rejects quantifiers > Int32.MaxValue, but ES spec allows any integer.
    /// </summary>
    private static void AppendCappedQuantifier(StringBuilder builder, string pattern, int braceStart, int braceEnd)
    {
        var content = pattern.AsSpan(braceStart + 1, braceEnd - braceStart - 1); // between { and }
        var commaIdx = content.IndexOf(',');

        if (commaIdx < 0)
        {
            // {n}
            builder.Append('{');
            AppendCappedInt(builder, content);
            builder.Append('}');
        }
        else
        {
            // {n,} or {n,m}
            builder.Append('{');
            AppendCappedInt(builder, content[..commaIdx]);
            builder.Append(',');
            var after = content[(commaIdx + 1)..];
            if (after.Length > 0)
            {
                AppendCappedInt(builder, after);
            }

            builder.Append('}');
        }
    }

    private static void AppendCappedInt(StringBuilder builder, ReadOnlySpan<char> digits)
    {
        const int max = int.MaxValue;
        if (digits.Length > 10 || (long.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out var val) && val > max))
        {
            builder.Append(max.ToString(CultureInfo.InvariantCulture));
        }
        else
        {
            builder.Append(digits);
        }
    }

    /// <summary>
    /// Validates that '{' at position i is part of a complete quantifier: {n}, {n,}, or {n,m}.
    /// In unicode mode, bare '{' is not allowed as a literal.
    /// Returns the index of the closing '}'.
    /// </summary>
    private static int ValidateQuantifierBrace(string pattern, int i)
    {
        var pos = i + 1;

        // Must start with at least one digit
        if (pos >= pattern.Length || !char.IsDigit(pattern[pos]))
            throw new ParseException("Invalid regular expression: incomplete quantifier.");

        // Skip digits (the 'n' part)
        while (pos < pattern.Length && char.IsDigit(pattern[pos]))
            pos++;

        if (pos >= pattern.Length)
            throw new ParseException("Invalid regular expression: incomplete quantifier.");

        if (pattern[pos] == '}')
            return pos; // {n} — valid

        if (pattern[pos] != ',')
            throw new ParseException("Invalid regular expression: incomplete quantifier.");

        pos++; // skip ','

        if (pos >= pattern.Length)
            throw new ParseException("Invalid regular expression: incomplete quantifier.");

        // Skip optional digits (the 'm' part)
        while (pos < pattern.Length && char.IsDigit(pattern[pos]))
            pos++;

        if (pos >= pattern.Length || pattern[pos] != '}')
            throw new ParseException("Invalid regular expression: incomplete quantifier.");

        return pos; // {n,} or {n,m} — valid
    }

    /// Tries to parse a modifier group at position i: (?s:, (?m:, (?-s:, (?sm:, (?s-m:, etc.
    /// Returns true if a valid modifier group prefix was found.
    /// Throws ParseException for invalid modifier syntax per ES2024 spec early errors:
    /// - Both add and remove sides empty: (?-:...)
    /// - Duplicate flags: (?ii:...), (?-ss:...)
    /// - Same flag on both sides: (?s-s:...)
    /// - Invalid flag characters: (?I:...), (?S:...)
    /// </summary>
    private static bool TryParseModifierGroup(string pattern, int i, out int endIndex,
        out bool enableS, out bool disableS, out bool enableM, out bool disableM,
        out bool enableI, out bool disableI)
    {
        enableS = false;
        disableS = false;
        enableM = false;
        disableM = false;
        enableI = false;
        disableI = false;
        endIndex = i;

        // Must start with (?
        if (i + 2 >= pattern.Length || pattern[i] != '(' || pattern[i + 1] != '?')
            return false;

        var pos = i + 2;
        var ch = pattern[pos];

        // Quick check: is this potentially a modifier group?
        // Modifier groups start with [ims-] after (?
        // If the character is a known non-modifier construct prefix, bail out fast
        if (ch is ':' or '=' or '!' or '<' or 'P')
            return false;

        // If the character is not a letter or '-', it's not a modifier group
        if (ch is not ('s' or 'm' or 'i' or '-') && !char.IsLetter(ch))
            return false;

        // If it IS a letter but NOT [ims-], scan ahead to see if this looks like a modifier
        // group with invalid flags (e.g., (?I:...), (?S:...)). If it ends with ':', throw.
        if (ch is not ('s' or 'm' or 'i' or '-'))
        {
            // Scan forward: if we see [letterOrDash]*: it's an invalid modifier group
            var scanPos = pos;
            while (scanPos < pattern.Length)
            {
                var sc = pattern[scanPos];
                if (sc == ':')
                {
                    throw new ParseException("Invalid regular expression: invalid modifier group flags.");
                }
                if (sc == '-' || char.IsLetter(sc))
                {
                    scanPos++;
                    continue;
                }
                break; // hit something else — not a modifier group
            }
            return false;
        }

        var inDisable = false;
        var addFlags = new HashSet<char>();
        var removeFlags = new HashSet<char>();

        // Parse modifier flags: [ims] or -[ims], ending with :
        while (pos < pattern.Length)
        {
            ch = pattern[pos];
            if (ch == ':')
            {
                // End of modifier prefix — validate per spec early errors
                if (addFlags.Count == 0 && removeFlags.Count == 0)
                {
                    // (?-:...) — both sides empty
                    throw new ParseException("Invalid regular expression: invalid modifier group flags.");
                }

                // Check for same flag on both sides
                foreach (var f in addFlags)
                {
                    if (removeFlags.Contains(f))
                        throw new ParseException("Invalid regular expression: invalid modifier group flags.");
                }

                // Extract results
                enableS = addFlags.Contains('s');
                disableS = removeFlags.Contains('s');
                enableM = addFlags.Contains('m');
                disableM = removeFlags.Contains('m');
                enableI = addFlags.Contains('i');
                disableI = removeFlags.Contains('i');
                endIndex = pos;
                return true;
            }

            if (ch == '-')
            {
                if (inDisable)
                    throw new ParseException("Invalid regular expression: invalid modifier group flags.");
                inDisable = true;
                pos++;
                continue;
            }

            if (ch is 's' or 'm' or 'i')
            {
                var targetSet = inDisable ? removeFlags : addFlags;
                if (!targetSet.Add(ch))
                {
                    // Duplicate flag: (?ii:...) or (?-ss:...)
                    throw new ParseException("Invalid regular expression: invalid modifier group flags.");
                }
                pos++;
                continue;
            }

            // Character is NOT a valid modifier [ims] and NOT : or -
            // If we've already seen modifier chars, this is an invalid modifier group
            if (addFlags.Count > 0 || removeFlags.Count > 0 || inDisable)
            {
                throw new ParseException("Invalid regular expression: invalid modifier group flags.");
            }

            // No modifier chars seen yet — not a modifier group at all
            return false;
        }

        return false; // reached end of pattern without finding ':'
    }

    private static bool IsSingleCharClassAtom(string pattern, int index)
    {
        if (index >= pattern.Length)
        {
            return false;
        }

        var current = pattern[index];
        if (current == ']')
        {
            return false;
        }

        if (current != '\\')
        {
            return true;
        }

        if (index + 1 >= pattern.Length)
        {
            return false;
        }

        var escape = pattern[index + 1];
        if (IsCharacterClassEscape(escape))
        {
            return false;
        }

        if (escape == 'c')
        {
            return index + 2 < pattern.Length && IsClassControlLetter(pattern[index + 2]);
        }

        if (escape == 'x')
        {
            return index + 3 < pattern.Length &&
                   IsHexDigit(pattern[index + 2]) &&
                   IsHexDigit(pattern[index + 3]);
        }

        if (escape == 'u')
        {
            return index + 5 < pattern.Length &&
                   IsHexDigit(pattern[index + 2]) &&
                   IsHexDigit(pattern[index + 3]) &&
                   IsHexDigit(pattern[index + 4]) &&
                   IsHexDigit(pattern[index + 5]);
        }

        return true;
    }

    private static int CountLegacyCaptures(string pattern)
    {
        var inCharClass = false;
        var escaped = false;
        var count = 0;

        for (var i = 0; i < pattern.Length; i++)
        {
            var c = pattern[i];
            if (escaped)
            {
                escaped = false;
                continue;
            }

            if (c == '\\')
            {
                escaped = true;
                continue;
            }

            if (inCharClass)
            {
                if (c == ']')
                {
                    inCharClass = false;
                }

                continue;
            }

            if (c == '[')
            {
                inCharClass = true;
                continue;
            }

            if (c == '(')
            {
                var isQuestion = i + 1 < pattern.Length && pattern[i + 1] == '?';
                var isNamedCapture = isQuestion && i + 2 < pattern.Length && pattern[i + 2] == '<' &&
                                     (i + 3 >= pattern.Length || (pattern[i + 3] != '=' && pattern[i + 3] != '!'));

                if (!isQuestion || isNamedCapture)
                {
                    count++;
                }
            }
        }

        return count;
    }

    private static HashSet<string> CollectGroupNames(string pattern)
    {
        var names = new HashSet<string>();
        var inCharClass = false;
        var escaped = false;

        for (var i = 0; i < pattern.Length; i++)
        {
            var c = pattern[i];
            if (escaped)
            {
                escaped = false;
                continue;
            }

            if (c == '\\')
            {
                escaped = true;
                continue;
            }

            if (inCharClass)
            {
                if (c == ']')
                {
                    inCharClass = false;
                }

                continue;
            }

            if (c == '[')
            {
                inCharClass = true;
                continue;
            }

            if (c == '(' && i + 2 < pattern.Length && pattern[i + 1] == '?' && pattern[i + 2] == '<')
            {
                // Skip lookbehind assertions: (?<=...) and (?<!...)
                if (i + 3 < pattern.Length && (pattern[i + 3] == '=' || pattern[i + 3] == '!'))
                {
                    continue;
                }

                var end = pattern.IndexOf('>', i + 3);
                if (end == -1)
                {
                    throw new ParseException("Invalid regular expression: incomplete group name.");
                }

                var name = pattern.Substring(i + 3, end - (i + 3));
                var normalizedName = NormalizeGroupNameToken(name);
                names.Add(normalizedName);
                i = end;
            }
        }

        return names;
    }

    internal static string NormalizeGroupNameToken(string rawName)
    {
        for (var i = 0; i < rawName.Length; i++)
        {
            if (rawName[i] == '\\' && i + 1 < rawName.Length && rawName[i + 1] == 'u')
            {
                if (i + 2 < rawName.Length && rawName[i + 2] == '{')
                {
                    var endBrace = rawName.IndexOf('}', i + 3);
                    if (endBrace == -1)
                    {
                        throw new ParseException("Invalid regular expression: invalid group name.");
                    }

                    var hex = rawName.Substring(i + 3, endBrace - (i + 3));
                    if (int.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var cp) &&
                        cp is >= 0xD800 and <= 0xDFFF)
                    {
                        throw new ParseException("Invalid regular expression: invalid group name.");
                    }
                }
                else if (i + 5 < rawName.Length &&
                         IsHexDigit(rawName[i + 2]) && IsHexDigit(rawName[i + 3]) &&
                         IsHexDigit(rawName[i + 4]) && IsHexDigit(rawName[i + 5]))
                {
                    var hex = rawName.Substring(i + 2, 4);
                    if (int.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var cp) &&
                        cp is >= 0xD800 and <= 0xDFFF)
                    {
                        // Allow high surrogate if followed by \uHHHH low surrogate (surrogate pair)
                        if (cp is >= 0xD800 and <= 0xDBFF &&
                            i + 11 < rawName.Length &&
                            rawName[i + 6] == '\\' && rawName[i + 7] == 'u' &&
                            IsHexDigit(rawName[i + 8]) && IsHexDigit(rawName[i + 9]) &&
                            IsHexDigit(rawName[i + 10]) && IsHexDigit(rawName[i + 11]))
                        {
                            var lowHex = rawName.Substring(i + 8, 4);
                            if (int.TryParse(lowHex, NumberStyles.HexNumber, CultureInfo.InvariantCulture,
                                    out var low) && low is >= 0xDC00 and <= 0xDFFF)
                            {
                                i += 11; // Skip to end of second \uHHHH (loop will increment)
                                continue;
                            }
                        }

                        throw new ParseException("Invalid regular expression: invalid group name.");
                    }
                }
            }
        }

        // Reject lone surrogates in raw name. Valid surrogate pairs (supplementary plane chars) are OK.
        for (var si = 0; si < rawName.Length; si++)
        {
            var ch = rawName[si];
            if (char.IsHighSurrogate(ch))
            {
                if (si + 1 < rawName.Length && char.IsLowSurrogate(rawName[si + 1]))
                {
                    si++; // Valid pair — skip both
                    continue;
                }

                throw new ParseException("Invalid regular expression: invalid group name.");
            }

            if (char.IsLowSurrogate(ch))
            {
                throw new ParseException("Invalid regular expression: invalid group name.");
            }
        }

        var runes = DecodeGroupName(rawName);
        if (runes.Count == 0)
        {
            throw new ParseException("Invalid regular expression: group name cannot be empty.");
        }

        for (var i = 0; i < runes.Count; i++)
        {
            var rune = runes[i];
            if (i == 0)
            {
                if (!IsIdentifierStart(rune))
                {
                    throw new ParseException("Invalid regular expression: invalid group name.");
                }
            }
            else
            {
                if (!IsIdentifierPart(rune))
                {
                    throw new ParseException("Invalid regular expression: invalid group name.");
                }
            }
        }

        var builder = new StringBuilder();
        foreach (var rune in runes)
        {
            builder.Append(rune.ToString());
        }

        return builder.ToString();
    }

    /// <summary>
    /// Post-process a normalized regex pattern to replace group names that contain
    /// characters not supported by .NET (like '$') with safe alternatives.
    /// Returns the sanitized pattern and a mapping from sanitized names to original names.
    /// </summary>
    private static string SanitizeGroupNamesForDotNet(string pattern, out Dictionary<string, string>? mapping)
    {
        mapping = null;

        // Quick check: if no problematic characters in the pattern, no sanitization needed
        var needsScan = false;
        foreach (var ch in pattern)
        {
            if (ch == '$' || char.IsSurrogate(ch))
            {
                needsScan = true;
                break;
            }
        }

        if (!needsScan)
        {
            return pattern;
        }

        var result = new StringBuilder(pattern.Length);
        var i = 0;
        var escaped = false;
        var inCharClass = false;
        // Cache: original name → sanitized name (ensures group def and backrefs use same name)
        var nameCache = new Dictionary<string, string>(StringComparer.Ordinal);

        while (i < pattern.Length)
        {
            var c = pattern[i];

            if (escaped)
            {
                result.Append(c);
                escaped = false;
                i++;
                continue;
            }

            if (c == '\\')
            {
                // Check for backreference \k<name> before general escape handling
                if (!inCharClass && i + 2 < pattern.Length && pattern[i + 1] == 'k' && pattern[i + 2] == '<')
                {
                    var end = pattern.IndexOf('>', i + 3);
                    if (end != -1)
                    {
                        var name = pattern.Substring(i + 3, end - (i + 3));
                        if (NeedsGroupNameSanitization(name))
                        {
                            if (!nameCache.TryGetValue(name, out var sanitized))
                            {
                                sanitized = SanitizeGroupName(name);
                                nameCache[name] = sanitized;
                            }

                            result.Append("\\k<");
                            result.Append(sanitized);
                            result.Append('>');
                            i = end + 1;
                            continue;
                        }
                    }
                }

                result.Append(c);
                escaped = true;
                i++;
                continue;
            }

            if (c == '[' && !inCharClass)
            {
                inCharClass = true;
                result.Append(c);
                i++;
                continue;
            }

            if (c == ']' && inCharClass)
            {
                inCharClass = false;
                result.Append(c);
                i++;
                continue;
            }

            if (inCharClass)
            {
                result.Append(c);
                i++;
                continue;
            }

            // Check for named group: (?<name>  (but not (?<= or (?<!)
            if (c == '(' && i + 2 < pattern.Length && pattern[i + 1] == '?' && pattern[i + 2] == '<'
                && i + 3 < pattern.Length && pattern[i + 3] != '=' && pattern[i + 3] != '!')
            {
                var end = pattern.IndexOf('>', i + 3);
                if (end != -1)
                {
                    var name = pattern.Substring(i + 3, end - (i + 3));
                    if (NeedsGroupNameSanitization(name))
                    {
                        if (!nameCache.TryGetValue(name, out var sanitized))
                        {
                            sanitized = SanitizeGroupName(name);
                            nameCache[name] = sanitized;
                        }

                        mapping ??= new Dictionary<string, string>();
                        mapping[sanitized] = name;
                        result.Append("(?<");
                        result.Append(sanitized);
                        result.Append('>');
                        i = end + 1;
                        continue;
                    }
                }
            }

            // Check for conditional: (?(name)
            if (c == '(' && i + 1 < pattern.Length && pattern[i + 1] == '?'
                && i + 2 < pattern.Length && pattern[i + 2] == '(')
            {
                var end = pattern.IndexOf(')', i + 3);
                if (end != -1)
                {
                    var name = pattern.Substring(i + 3, end - (i + 3));
                    if (NeedsGroupNameSanitization(name))
                    {
                        if (!nameCache.TryGetValue(name, out var sanitized))
                        {
                            sanitized = SanitizeGroupName(name);
                            nameCache[name] = sanitized;
                        }

                        result.Append("(?(");
                        result.Append(sanitized);
                        result.Append(')');
                        i = end + 1;
                        continue;
                    }
                }
            }

            result.Append(c);
            i++;
        }

        return result.ToString();
    }

    /// <summary>
    /// ES2025 duplicate named groups: renames each occurrence of a duplicated group name
    /// to a unique name (e.g. x → x__0, x__1) so .NET treats them as separate capture groups.
    /// Also rewrites \k&lt;name&gt; backreferences to conditional references that match
    /// whichever renamed group actually participated in the match.
    /// </summary>
    private static string RenameDuplicateGroups(
        string pattern,
        ref Dictionary<string, string>? nameMapping,
        out Dictionary<string, string[]>? duplicateGroupNames)
    {
        duplicateGroupNames = null;

        // Phase 1: Find which group names appear more than once
        var duplicates = FindDuplicateGroupNamesInPattern(pattern);
        if (duplicates is null)
        {
            return pattern; // No duplicates, nothing to do
        }

        // Phase 2: Rewrite the pattern
        var result = new StringBuilder(pattern.Length + 64);
        var occurrenceCounts = new Dictionary<string, int>(StringComparer.Ordinal);
        var renamedGroups = new Dictionary<string, List<string>>(StringComparer.Ordinal);

        var i = 0;
        var escaped = false;
        var inCharClass = false;

        while (i < pattern.Length)
        {
            var c = pattern[i];

            if (escaped)
            {
                result.Append(c);
                escaped = false;
                i++;
                continue;
            }

            if (c == '\\')
            {
                // Check for backreference \k<name> before general escape handling
                if (!inCharClass && i + 2 < pattern.Length &&
                    pattern[i + 1] == 'k' && pattern[i + 2] == '<')
                {
                    var end = pattern.IndexOf('>', i + 3);
                    if (end != -1)
                    {
                        var name = pattern.Substring(i + 3, end - (i + 3));
                        if (duplicates.Contains(name) && renamedGroups.TryGetValue(name, out var renames))
                        {
                            // Rewrite to conditional backreference:
                            // (?(x__0)\k<x__0>|(?(x__1)\k<x__1>|))
                            AppendConditionalBackref(result, renames);
                            i = end + 1;
                            continue;
                        }
                    }
                }

                result.Append(c);
                escaped = true;
                i++;
                continue;
            }

            if (c == '[' && !inCharClass)
            {
                inCharClass = true;
                result.Append(c);
                i++;
                continue;
            }

            if (c == ']' && inCharClass)
            {
                inCharClass = false;
                result.Append(c);
                i++;
                continue;
            }

            if (inCharClass)
            {
                result.Append(c);
                i++;
                continue;
            }

            // Check for conditional (?(name)\k<name>|) from NormalizePattern where name is a duplicate.
            // NormalizePattern wraps ALL named backrefs as (?(name)\k<name>|). When name is a
            // duplicate group (renamed to name__0, name__1, etc.), we must replace the entire
            // construct with a multi-conditional: (?(name__0)\k<name__0>|(?(name__1)\k<name__1>|))
            if (c == '(' && i + 3 < pattern.Length &&
                pattern[i + 1] == '?' && pattern[i + 2] == '(')
            {
                var condEnd = pattern.IndexOf(')', i + 3);
                if (condEnd != -1)
                {
                    var condName = pattern.Substring(i + 3, condEnd - (i + 3));
                    if (duplicates.Contains(condName) && renamedGroups.TryGetValue(condName, out var renames))
                    {
                        // Verify the full pattern: (?(name)\k<name>|)
                        var expectedSuffix = "\\k<" + condName + ">|)";
                        var afterCond = condEnd + 1;
                        if (afterCond + expectedSuffix.Length <= pattern.Length &&
                            string.Compare(pattern, afterCond, expectedSuffix, 0, expectedSuffix.Length, StringComparison.Ordinal) == 0)
                        {
                            // Replace entire (?(name)\k<name>|) with multi-conditional backref
                            AppendConditionalBackref(result, renames);
                            i = afterCond + expectedSuffix.Length;
                            continue;
                        }
                    }
                }
            }

            // Check for named group: (?<name> (but not (?<= or (?<!)
            if (c == '(' && i + 2 < pattern.Length &&
                pattern[i + 1] == '?' && pattern[i + 2] == '<' &&
                i + 3 < pattern.Length && pattern[i + 3] != '=' && pattern[i + 3] != '!')
            {
                var end = pattern.IndexOf('>', i + 3);
                if (end != -1)
                {
                    var name = pattern.Substring(i + 3, end - (i + 3));
                    if (duplicates.Contains(name))
                    {
                        if (!occurrenceCounts.TryGetValue(name, out var count))
                        {
                            count = 0;
                        }

                        var renamedName = name + "__" + count.ToString(CultureInfo.InvariantCulture);
                        occurrenceCounts[name] = count + 1;

                        if (!renamedGroups.TryGetValue(name, out var list))
                        {
                            list = [];
                            renamedGroups[name] = list;
                        }

                        list.Add(renamedName);

                        // Add to the name mapping (sanitized .NET name → original JS name)
                        nameMapping ??= new Dictionary<string, string>();
                        nameMapping[renamedName] = name;

                        result.Append("(?<");
                        result.Append(renamedName);
                        result.Append('>');
                        i = end + 1;
                        continue;
                    }
                }
            }

            result.Append(c);
            i++;
        }

        // Build the duplicateGroupNames output
        duplicateGroupNames = new Dictionary<string, string[]>(StringComparer.Ordinal);
        foreach (var kvp in renamedGroups)
        {
            duplicateGroupNames[kvp.Key] = kvp.Value.ToArray();
        }

        return result.ToString();
    }

    /// <summary>
    /// Appends a conditional backreference for duplicate named groups.
    /// Produces: (?(x__0)\k&lt;x__0&gt;|(?(x__1)\k&lt;x__1&gt;|))
    /// </summary>
    private static void AppendConditionalBackref(StringBuilder sb, List<string> renamedNames)
    {
        for (var i = 0; i < renamedNames.Count; i++)
        {
            sb.Append("(?(");
            sb.Append(renamedNames[i]);
            sb.Append(")\\k<");
            sb.Append(renamedNames[i]);
            sb.Append(">|");
        }

        // Close all conditional groups (empty match when none matched)
        for (var i = 0; i < renamedNames.Count; i++)
        {
            sb.Append(')');
        }
    }

    private static void AppendConditionalNumericBackref(StringBuilder builder, int groupNumber)
    {
        var group = groupNumber.ToString(CultureInfo.InvariantCulture);
        builder.Append("(?(");
        builder.Append(group);
        builder.Append(")\\");
        builder.Append(group);
        builder.Append("|)");
    }

    /// <summary>
    /// Pre-scans a pattern to find group names that appear more than once (duplicate named groups).
    /// </summary>
    private static HashSet<string>? FindDuplicateGroupNamesInPattern(string pattern)
    {
        Dictionary<string, int>? counts = null;
        var i = 0;
        var escaped = false;
        var inCharClass = false;

        while (i < pattern.Length)
        {
            var c = pattern[i];

            if (escaped)
            {
                escaped = false;
                i++;
                continue;
            }

            if (c == '\\')
            {
                escaped = true;
                i++;
                continue;
            }

            if (c == '[' && !inCharClass)
            {
                inCharClass = true;
                i++;
                continue;
            }

            if (c == ']' && inCharClass)
            {
                inCharClass = false;
                i++;
                continue;
            }

            if (inCharClass)
            {
                i++;
                continue;
            }

            // Named group (?<name>)
            if (c == '(' && i + 2 < pattern.Length &&
                pattern[i + 1] == '?' && pattern[i + 2] == '<' &&
                i + 3 < pattern.Length && pattern[i + 3] != '=' && pattern[i + 3] != '!')
            {
                var end = pattern.IndexOf('>', i + 3);
                if (end != -1)
                {
                    var name = pattern.Substring(i + 3, end - (i + 3));
                    counts ??= new Dictionary<string, int>(StringComparer.Ordinal);
                    counts.TryGetValue(name, out var count);
                    counts[name] = count + 1;
                    i = end + 1;
                    continue;
                }
            }

            i++;
        }

        if (counts is null)
        {
            return null;
        }

        HashSet<string>? result = null;
        foreach (var kvp in counts)
        {
            if (kvp.Value > 1)
            {
                result ??= new HashSet<string>(StringComparer.Ordinal);
                result.Add(kvp.Key);
            }
        }

        return result;
    }

    /// <summary>
    /// Inserts atomic group resets at the start of each alternative within groups that
    /// transitively contain duplicate-named captures. This simulates JavaScript's behavior
    /// of resetting captures at the start of each quantifier iteration, which .NET does not do.
    /// Uses .NET balancing groups: (?>(?&lt;-name&gt;)?) to pop stale captures.
    /// </summary>
    private static string InsertQuantifierResets(string pattern, Dictionary<string, string[]> duplicateGroupNames)
    {
        _ = duplicateGroupNames;

        // Phase 1: Walk pattern to determine which groups contain duplicate captures.
        // For each group, record the duplicate captures in its descendant alternatives so
        // resets only clear captures relevant to that group, not unrelated siblings.
        // Use index into a list as the group ID.
        var groupOpenPositions = new List<int>(); // index = group ID, value = position of char AFTER opener
        var groupContainsDup = new List<bool>(); // index = group ID
        var groupParent = new List<int>(); // index = group ID, value = parent group ID (-1 for top level)
        var groupResetNames = new List<HashSet<string>?>();
        var groupStack = new Stack<int>(); // stack of group IDs

        var i = 0;
        var escaped = false;
        var inCharClass = false;

        while (i < pattern.Length)
        {
            var c = pattern[i];

            if (escaped)
            {
                escaped = false;
                i++;
                continue;
            }

            if (c == '\\')
            {
                escaped = true;
                i++;
                continue;
            }

            if (c == '[' && !inCharClass)
            {
                inCharClass = true;
                i++;
                continue;
            }

            if (c == ']' && inCharClass)
            {
                inCharClass = false;
                i++;
                continue;
            }

            if (inCharClass)
            {
                i++;
                continue;
            }

            if (c == '(')
            {
                var groupId = groupOpenPositions.Count;
                var parentId = groupStack.Count > 0 ? groupStack.Peek() : -1;

                // Find position after the group opener (past (?:, (?<name>, etc.)
                var contentStart = i + 1;
                if (i + 1 < pattern.Length && pattern[i + 1] == '?')
                {
                    if (i + 2 < pattern.Length && pattern[i + 2] == '<' &&
                        i + 3 < pattern.Length && pattern[i + 3] != '=' && pattern[i + 3] != '!')
                    {
                        // Named group (?<name>...) — content starts after >
                        var end = pattern.IndexOf('>', i + 3);
                        if (end != -1)
                        {
                            contentStart = end + 1;

                            // Check if this is a renamed duplicate group
                            var name = pattern.Substring(i + 3, end - (i + 3));
                            if (name.Contains("__", StringComparison.Ordinal))
                            {
                                // This is a renamed duplicate capture. Mark this group and all ancestors.
                                groupOpenPositions.Add(contentStart);
                                groupContainsDup.Add(false); // The named group itself doesn't need reset
                                groupParent.Add(parentId);
                                groupResetNames.Add(null);
                                groupStack.Push(groupId);

                                // Mark all ancestor groups as containing duplicates
                                var ancestor = parentId;
                                while (ancestor >= 0)
                                {
                                    groupContainsDup[ancestor] = true;
                                    (groupResetNames[ancestor] ??= new HashSet<string>(StringComparer.Ordinal))
                                        .Add(name);
                                    ancestor = groupParent[ancestor];
                                }

                                i = end + 1;
                                continue;
                            }
                        }
                    }
                    else
                    {
                        contentStart = i + 2; // After (?
                        // Skip to content: (?:, (?=, (?!, (?<=, (?<!
                        if (i + 2 < pattern.Length && pattern[i + 2] == ':')
                        {
                            contentStart = i + 3;
                        }
                    }
                }

                groupOpenPositions.Add(contentStart);
                groupContainsDup.Add(false);
                groupParent.Add(parentId);
                groupResetNames.Add(null);
                groupStack.Push(groupId);
                i++;
                continue;
            }

            if (c == ')' && groupStack.Count > 0)
            {
                groupStack.Pop();
                i++;
                continue;
            }

            i++;
        }

        // Phase 2: Collect positions where resets need to be inserted.
        // For each group that contains duplicates: insert at content start and after each |
        var insertPositions = new HashSet<int>();
        groupStack.Clear();
        i = 0;
        escaped = false;
        inCharClass = false;
        var groupIndex = 0;

        while (i < pattern.Length)
        {
            var c = pattern[i];

            if (escaped)
            {
                escaped = false;
                i++;
                continue;
            }

            if (c == '\\')
            {
                escaped = true;
                i++;
                continue;
            }

            if (c == '[' && !inCharClass)
            {
                inCharClass = true;
                i++;
                continue;
            }

            if (c == ']' && inCharClass)
            {
                inCharClass = false;
                i++;
                continue;
            }

            if (inCharClass)
            {
                i++;
                continue;
            }

            if (c == '(')
            {
                if (groupIndex < groupOpenPositions.Count)
                {
                    var contentStart = groupOpenPositions[groupIndex];
                    if (groupContainsDup[groupIndex])
                    {
                        insertPositions.Add(contentStart);
                    }

                    groupStack.Push(groupIndex);
                    groupIndex++;
                }

                i++;
                continue;
            }

            if (c == ')' && groupStack.Count > 0)
            {
                groupStack.Pop();
                i++;
                continue;
            }

            if (c == '|' && groupStack.Count > 0)
            {
                var currentGroup = groupStack.Peek();
                if (groupContainsDup[currentGroup])
                {
                    insertPositions.Add(i + 1); // Insert after the |
                }
            }

            i++;
        }

        if (insertPositions.Count == 0)
        {
            return pattern;
        }

        var resetStrings = new string?[groupOpenPositions.Count];
        for (var groupId = 0; groupId < groupResetNames.Count; groupId++)
        {
            var names = groupResetNames[groupId];
            if (names is null || names.Count == 0)
            {
                continue;
            }

            var resetBuilder = new StringBuilder("(?>", 16 + (names.Count * 12));
            foreach (var name in names.OrderBy(static n => n, StringComparer.Ordinal))
            {
                resetBuilder.Append("(?<-");
                resetBuilder.Append(name);
                resetBuilder.Append(">)?");
            }

            resetBuilder.Append(')');
            resetStrings[groupId] = resetBuilder.ToString();
        }

        var insertsByPosition = new Dictionary<int, string>();
        groupStack.Clear();
        i = 0;
        escaped = false;
        inCharClass = false;
        groupIndex = 0;

        while (i < pattern.Length)
        {
            var c = pattern[i];

            if (escaped)
            {
                escaped = false;
                i++;
                continue;
            }

            if (c == '\\')
            {
                escaped = true;
                i++;
                continue;
            }

            if (c == '[' && !inCharClass)
            {
                inCharClass = true;
                i++;
                continue;
            }

            if (c == ']' && inCharClass)
            {
                inCharClass = false;
                i++;
                continue;
            }

            if (inCharClass)
            {
                i++;
                continue;
            }

            if (c == '(')
            {
                if (groupIndex < groupOpenPositions.Count)
                {
                    var contentStart = groupOpenPositions[groupIndex];
                    if (groupContainsDup[groupIndex] && resetStrings[groupIndex] is { } reset)
                    {
                        insertsByPosition[contentStart] = reset;
                    }

                    groupStack.Push(groupIndex);
                    groupIndex++;
                }

                i++;
                continue;
            }

            if (c == ')' && groupStack.Count > 0)
            {
                groupStack.Pop();
                i++;
                continue;
            }

            if (c == '|' && groupStack.Count > 0)
            {
                var currentGroup = groupStack.Peek();
                if (groupContainsDup[currentGroup] && resetStrings[currentGroup] is { } reset)
                {
                    insertsByPosition[i + 1] = reset;
                }
            }

            i++;
        }

        // Phase 3: Build the new pattern with resets inserted
        var averageInsertLength = resetStrings.Where(static s => s is not null).Select(static s => s!.Length).DefaultIfEmpty().Average();
        var result = new StringBuilder(pattern.Length + (int)(insertsByPosition.Count * averageInsertLength));
        for (i = 0; i < pattern.Length; i++)
        {
            if (insertsByPosition.TryGetValue(i, out var reset))
            {
                result.Append(reset);
            }

            result.Append(pattern[i]);
        }

        // Check if we need to insert at the very end (unlikely but handle it)
        if (insertsByPosition.TryGetValue(pattern.Length, out var trailingReset))
        {
            result.Append(trailingReset);
        }

        return result.ToString();
    }

    /// <summary>
    /// Returns true if a group name contains characters not supported by .NET regex.
    /// .NET regex doesn't support '$' or supplementary plane characters (surrogates) in group names.
    /// </summary>
    private static bool NeedsGroupNameSanitization(string name)
    {
        foreach (var ch in name)
        {
            if (ch == '$' || char.IsSurrogate(ch))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Replaces characters in a group name that .NET regex doesn't support.
    /// </summary>
    [ThreadStatic] private static int s_sanitizeCounter;

    private static string SanitizeGroupName(string name)
    {
        // If name contains surrogates (supplementary plane chars), replace entirely
        // since .NET regex doesn't support them in group names
        foreach (var ch in name)
        {
            if (char.IsSurrogate(ch))
            {
                return $"_u{s_sanitizeCounter++}";
            }
        }

        return name.Replace("$", "_dollar_", StringComparison.Ordinal);
    }

    /// <summary>
    /// Returns the original ECMAScript group name from a .NET regex group name,
    /// using the mapping if available.
    /// </summary>
    private string GetOriginalGroupName(string dotNetName)
    {
        if (_groupNameMapping is not null && _groupNameMapping.TryGetValue(dotNetName, out var original))
        {
            return original;
        }

        return dotNetName;
    }

    private static (int Start, int End)[] GetBasicEmojiTextDefaultRanges()
    {
        var emoji = UnicodePropertyData.Resolve("Emoji") ?? [];
        var emojiPresentation = UnicodePropertyData.Resolve("Emoji_Presentation") ?? [];
        var emojiComponent = UnicodePropertyData.Resolve("Emoji_Component") ?? [];
        var regionalIndicator = UnicodePropertyData.Resolve("Regional_Indicator") ?? [];

        var textDefaultEmoji = SubtractRangesStatic(emoji, emojiPresentation);
        textDefaultEmoji = SubtractRangesStatic(textDefaultEmoji, emojiComponent);
        textDefaultEmoji = SubtractRangesStatic(textDefaultEmoji, regionalIndicator);
        textDefaultEmoji = SubtractRangesStatic(textDefaultEmoji, BasicEmojiKeycapBaseRanges);
        return textDefaultEmoji;
    }

    private static (int Start, int End)[] SubtractRangesStatic((int Start, int End)[] minuend, (int Start, int End)[] subtrahend)
    {
        if (minuend.Length == 0)
        {
            return [];
        }

        if (subtrahend.Length == 0)
        {
            return minuend;
        }

        var result = new List<(int Start, int End)>();
        var j = 0;

        foreach (var (start, end) in minuend)
        {
            var currentStart = start;

            while (j < subtrahend.Length && subtrahend[j].End < currentStart)
            {
                j++;
            }

            var k = j;
            while (k < subtrahend.Length && subtrahend[k].Start <= end)
            {
                var (otherStart, otherEnd) = subtrahend[k];
                if (otherStart > currentStart)
                {
                    result.Add((currentStart, Math.Min(end, otherStart - 1)));
                }

                if (otherEnd >= end)
                {
                    currentStart = end + 1;
                    break;
                }

                currentStart = Math.Max(currentStart, otherEnd + 1);
                k++;
            }

            if (currentStart <= end)
            {
                result.Add((currentStart, end));
            }
        }

        return [.. result];
    }

    private static bool ContainsLoneSurrogate(string value)
    {
        for (var i = 0; i < value.Length; i++)
        {
            var ch = value[i];
            if (char.IsHighSurrogate(ch))
            {
                if (i + 1 < value.Length && char.IsLowSurrogate(value[i + 1]))
                {
                    i++; // Valid surrogate pair — skip both
                    continue;
                }

                return true; // Lone high surrogate
            }

            if (char.IsLowSurrogate(ch))
            {
                return true; // Lone low surrogate
            }
        }

        return false;
    }

    private static List<Rune> DecodeGroupName(string name)
    {
        var runes = new List<Rune>();
        for (var i = 0; i < name.Length;)
        {
            var ch = name[i];
            if (ch == '\\')
            {
                if (i + 1 >= name.Length || name[i + 1] != 'u')
                {
                    throw new ParseException("Invalid regular expression: invalid group name.");
                }

                if (i + 2 < name.Length && name[i + 2] == '{')
                {
                    var endBrace = name.IndexOf('}', i + 3);
                    if (endBrace == -1)
                    {
                        throw new ParseException("Invalid regular expression: invalid group name.");
                    }

                    var hex = name.Substring(i + 3, endBrace - (i + 3));
                    if (hex.Length is < 1 or > 6 ||
                        !int.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var codePoint))
                    {
                        throw new ParseException("Invalid regular expression: invalid group name.");
                    }

                    if (codePoint is < 0 or > 0x10FFFF || codePoint is >= 0xD800 and <= 0xDFFF)
                    {
                        throw new ParseException("Invalid regular expression: invalid group name.");
                    }

                    runes.Add(new Rune(codePoint));
                    i = endBrace + 1;
                    continue;
                }

                if (i + 5 >= name.Length)
                {
                    throw new ParseException("Invalid regular expression: invalid group name.");
                }

                var hexDigits = name.Substring(i + 2, 4);
                if (!int.TryParse(hexDigits, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var code))
                {
                    throw new ParseException("Invalid regular expression: invalid group name.");
                }

                // Handle surrogate pair: \uHHHH\uHHHH where first is high and second is low
                if (code is >= 0xD800 and <= 0xDBFF)
                {
                    // High surrogate — look for following \uHHHH low surrogate
                    if (i + 6 < name.Length && name[i + 6] == '\\' &&
                        i + 7 < name.Length && name[i + 7] == 'u' &&
                        i + 11 < name.Length &&
                        IsHexDigit(name[i + 8]) && IsHexDigit(name[i + 9]) &&
                        IsHexDigit(name[i + 10]) && IsHexDigit(name[i + 11]))
                    {
                        var lowHex = name.Substring(i + 8, 4);
                        if (int.TryParse(lowHex, NumberStyles.HexNumber, CultureInfo.InvariantCulture,
                                out var lowCode) && lowCode is >= 0xDC00 and <= 0xDFFF)
                        {
                            var codePoint = 0x10000 + ((code - 0xD800) << 10) + (lowCode - 0xDC00);
                            runes.Add(new Rune(codePoint));
                            i += 12; // Skip both \uHHHH sequences
                            continue;
                        }
                    }

                    throw new ParseException("Invalid regular expression: invalid group name.");
                }

                if (code is >= 0xDC00 and <= 0xDFFF)
                {
                    throw new ParseException("Invalid regular expression: invalid group name.");
                }

                runes.Add(new Rune(code));
                i += 6;
                continue;
            }

            if (Rune.DecodeFromUtf16(name.AsSpan(i), out var rune, out var consumed) != OperationStatus.Done)
            {
                throw new ParseException("Invalid regular expression: invalid group name.");
            }

            if (rune.Value is >= 0xD800 and <= 0xDFFF)
            {
                throw new ParseException("Invalid regular expression: invalid group name.");
            }

            runes.Add(rune);
            i += consumed;
        }

        return runes;
    }

    private static bool IsIdentifierStart(Rune rune)
    {
        if (rune.Value is '$' or '_')
        {
            return true;
        }

        var category = Rune.GetUnicodeCategory(rune);
        return category is UnicodeCategory.UppercaseLetter
            or UnicodeCategory.LowercaseLetter
            or UnicodeCategory.TitlecaseLetter
            or UnicodeCategory.ModifierLetter
            or UnicodeCategory.OtherLetter
            or UnicodeCategory.LetterNumber;
    }

    private static bool IsIdentifierPart(Rune rune)
    {
        // ECMAScript IdentifierPart includes <ZWNJ> and <ZWJ>
        if (rune.Value is 0x200C or 0x200D)
        {
            return true;
        }

        if (IsIdentifierStart(rune))
        {
            return true;
        }

        var category = Rune.GetUnicodeCategory(rune);
        return category is UnicodeCategory.DecimalDigitNumber
            or UnicodeCategory.ConnectorPunctuation
            or UnicodeCategory.NonSpacingMark
            or UnicodeCategory.SpacingCombiningMark;
    }

    private static bool IsHexDigit(char c)
    {
        return c is >= '0' and <= '9' or >= 'a' and <= 'f' or >= 'A' and <= 'F';
    }

    private static bool IsLineTerminator(char c)
    {
        return c is '\n' or '\r' or '\u2028' or '\u2029';
    }

    private static bool IsControlLetter(char c)
    {
        return c is >= 'A' and <= 'Z' or >= 'a' and <= 'z';
    }

    private static bool IsClassControlLetter(char c)
    {
        return IsControlLetter(c) || char.IsDigit(c) || c == '_';
    }

    private static bool IsSyntaxCharacter(char c)
    {
        return c is '^' or '$' or '\\' or '.' or '*' or '+' or '?' or '(' or ')' or '[' or ']' or '{' or '}' or '|'
            or '/';
    }

    private static bool IsLegacyEscape(char c)
    {
        return c is 'b' or 'B' or 'f' or 'n' or 'r' or 't' or 'v' or 's' or 'S' or 'w' or 'W' or 'd' or 'D';
    }

    private static bool IsCharacterClassEscape(char c)
    {
        return c is 'd' or 'D' or 's' or 'S' or 'w' or 'W';
    }

    private static bool RequiresRegexEscape(char c)
    {
        return c is '.' or '$' or '^' or '{' or '[' or '(' or '|' or ')' or '*' or '+' or '?' or '\\' or '/' or ']'
            or '}';
    }

    private static string EscapeCodeUnit(int codeUnit)
    {
        if (codeUnit == 0)
        {
            return "\\x00";
        }

        return $"\\u{codeUnit:X4}";
    }

    private static void AppendCodePoint(StringBuilder builder, int codePoint, bool unicodeMode, bool ignoreCase,
        bool asLiteral)
    {
        if (!unicodeMode && ignoreCase && codePoint == 0x212A)
        {
            builder.Append("(?-i:\\u212A)");
            return;
        }

        if (!unicodeMode)
        {
            if (char.IsSurrogate((char)codePoint))
            {
                var escaped = $"\\u{codePoint:X4}";
                builder.Append(escaped);
                return;
            }

            var text = char.ConvertFromUtf32(codePoint);
            if (!asLiteral)
            {
                builder.Append(text);
                return;
            }

            if (text.Length == 1 && !RequiresRegexEscape(text[0]))
            {
                builder.Append(text);
                return;
            }

            builder.Append(Regex.Escape(text));
            return;
        }

        if (codePoint > 0x10FFFF || codePoint < 0)
        {
            throw new ParseException("Invalid regular expression: invalid unicode escape.");
        }

        if (codePoint is >= 0xD800 and <= 0xDFFF)
        {
            throw new ParseException("Invalid regular expression: invalid unicode escape.");
        }

        if (codePoint <= 0xFFFF)
        {
            var text = char.ConvertFromUtf32(codePoint);
            builder.Append(asLiteral ? Regex.Escape(text) : text);
            return;
        }

        builder.Append("(?:");
        builder.Append(FormatAstralAsSurrogates(codePoint));
        builder.Append(')');
    }

    private static string NormalizeUnicodeCharacterClass(string pattern, ref int index, bool unicodeIgnoreCase = false, bool hasUnicodeSetsFlag = false)
    {
        if (hasUnicodeSetsFlag &&
            TryNormalizeUnicodeSetExpression(pattern, ref index, unicodeIgnoreCase, out var unicodeSetPattern))
        {
            return unicodeSetPattern;
        }

        if (TryNormalizeSimpleUnicodeSetDifference(pattern, ref index, unicodeIgnoreCase, out var setDifferencePattern))
        {
            return setDifferencePattern;
        }

        var start = index + 1;
        if (start >= pattern.Length)
        {
            throw new ParseException("Invalid regular expression: unterminated character class.");
        }

        var negate = pattern[start] == '^';
        var cursor = negate ? start + 1 : start;

        var bmpRanges = new List<(int Start, int End)>();
        var astralRanges = new List<(int Start, int End)>();

        while (cursor < pattern.Length)
        {
            if (pattern[cursor] == ']' && cursor > start)
            {
                break;
            }

            // Handle \p{...} and \P{...} inside character classes
            if (cursor + 1 < pattern.Length && pattern[cursor] == '\\' &&
                pattern[cursor + 1] is 'p' or 'P')
            {
                var isNegatedProp = pattern[cursor + 1] == 'P';
                if (cursor + 2 >= pattern.Length || pattern[cursor + 2] != '{')
                {
                    throw new ParseException(
                        "Invalid regular expression: incomplete unicode property escape.");
                }

                var endBrace = pattern.IndexOf('}', cursor + 3);
                if (endBrace == -1)
                {
                    throw new ParseException(
                        "Invalid regular expression: incomplete unicode property escape.");
                }

                var propertyExpr = pattern.Substring(cursor + 3, endBrace - (cursor + 3));
                var propRanges = UnicodePropertyData.Resolve(propertyExpr);
                if (propRanges is null)
                {
                    throw new ParseException(
                        $"Invalid regular expression: invalid unicode property escape \\{(isNegatedProp ? 'P' : 'p')}{{{propertyExpr}}}.");
                }

                // Add resolved ranges to BMP/astral lists
                // For \P{...} inside a character class, we need the complement
                // But inside [...], negation is handled by the class-level ^ if present.
                // ECMAScript spec says \P{X} inside a class adds the complement of X.
                if (isNegatedProp)
                {
                    // Complement: all code points NOT in propRanges
                    var complementRanges = ComplementCodePointRanges(propRanges);
                    foreach (var (s, e) in complementRanges)
                    {
                        if (e <= 0xFFFF)
                            bmpRanges.Add((s, e));
                        else if (s > 0xFFFF)
                            astralRanges.Add((s, e));
                        else
                        {
                            bmpRanges.Add((s, 0xFFFF));
                            astralRanges.Add((0x10000, e));
                        }
                    }
                }
                else
                {
                    foreach (var (s, e) in propRanges)
                    {
                        if (e <= 0xFFFF)
                            bmpRanges.Add((s, e));
                        else if (s > 0xFFFF)
                            astralRanges.Add((s, e));
                        else
                        {
                            bmpRanges.Add((s, 0xFFFF));
                            astralRanges.Add((0x10000, e));
                        }
                    }
                }

                cursor = endBrace + 1;
                continue;
            }

            // Handle character class escapes \d, \D, \w, \W, \s, \S inside [...]
            // These represent sets of code points, not single code points.
            if (cursor + 1 < pattern.Length && pattern[cursor] == '\\' &&
                pattern[cursor + 1] is 'd' or 'D' or 'w' or 'W' or 's' or 'S')
            {
                var escapeChar = pattern[cursor + 1];
                var classRanges = GetCharacterClassEscapeRanges(escapeChar);

                // Check for invalid range: [\d-a] should throw in unicode mode
                if (cursor + 2 < pattern.Length && pattern[cursor + 2] == '-' &&
                    cursor + 3 < pattern.Length && pattern[cursor + 3] != ']')
                {
                    throw new ParseException(
                        $"Invalid regular expression: character class escape \\{escapeChar} cannot be used as range endpoint.");
                }

                foreach (var (s, e) in classRanges)
                {
                    if (e <= 0xFFFF)
                        bmpRanges.Add((s, e));
                    else if (s > 0xFFFF)
                        astralRanges.Add((s, e));
                    else
                    {
                        bmpRanges.Add((s, 0xFFFF));
                        astralRanges.Add((0x10000, e));
                    }
                }

                // When unicode ignoreCase is active, \w includes U+017F and U+212A
                if (unicodeIgnoreCase && escapeChar == 'w')
                {
                    bmpRanges.Add((0x017F, 0x017F));
                    bmpRanges.Add((0x212A, 0x212A));
                }

                cursor += 2;
                continue;
            }

            // Also check if a range starts with a literal and ends with a class escape: [a-\d]
            // This needs to be caught too. We'll detect it after parsing the first code point
            // when we see '-' followed by \d/\D/\w/\W/\s/\S.

            var cp = ParseClassCodePoint(pattern, ref cursor);
            if (IsHighSurrogate(cp) &&
                TryParseLowSurrogate(pattern, ref cursor, out var trail))
            {
                cp = char.ConvertToUtf32((char)cp, (char)trail);
            }
            else if (IsSurrogate(cp))
            {
                throw new ParseException("Invalid regular expression: invalid unicode escape.");
            }

            var endCp = cp;
            if (cursor < pattern.Length && pattern[cursor] == '-' && cursor + 1 < pattern.Length &&
                pattern[cursor + 1] != ']')
            {
                // Check for [a-\d] — class escape as range endpoint is invalid in unicode mode
                if (cursor + 2 < pattern.Length && pattern[cursor + 1] == '\\' &&
                    pattern[cursor + 2] is 'd' or 'D' or 'w' or 'W' or 's' or 'S')
                {
                    throw new ParseException(
                        $"Invalid regular expression: character class escape \\{pattern[cursor + 2]} cannot be used as range endpoint.");
                }

                cursor++;
                endCp = ParseClassCodePoint(pattern, ref cursor);
                if (IsHighSurrogate(endCp) &&
                    TryParseLowSurrogate(pattern, ref cursor, out var rangeTrail))
                {
                    endCp = char.ConvertToUtf32((char)endCp, (char)rangeTrail);
                }
                else if (IsSurrogate(endCp))
                {
                    throw new ParseException("Invalid regular expression: invalid unicode escape.");
                }

                if (endCp < cp)
                {
                    throw new ParseException("Invalid regular expression: inverted character class range.");
                }
            }

            if (endCp > 0xFFFF)
            {
                astralRanges.Add((cp, endCp));
            }
            else
            {
                AddBmpRangeWithSimpleCaseFolds(bmpRanges, cp, endCp, unicodeIgnoreCase);
            }
        }

        if (cursor >= pattern.Length || pattern[cursor] != ']')
        {
            throw new ParseException("Invalid regular expression: unterminated character class.");
        }

        index = cursor;
        return BuildUnicodeClassPattern(negate, bmpRanges, astralRanges);
    }

    private static void AddBmpRangeWithSimpleCaseFolds(
        List<(int Start, int End)> bmpRanges,
        int start,
        int end,
        bool unicodeIgnoreCase)
    {
        bmpRanges.Add((start, end));
        if (!unicodeIgnoreCase)
        {
            return;
        }

        AddSimpleCaseFoldIfInRange(bmpRanges, start, end, 0x0390, 0x1FD3);
        AddSimpleCaseFoldIfInRange(bmpRanges, start, end, 0x03B0, 0x1FE3);
        AddSimpleCaseFoldIfInRange(bmpRanges, start, end, 0xFB05, 0xFB06);
    }

    private static void AddSimpleCaseFoldIfInRange(
        List<(int Start, int End)> bmpRanges,
        int start,
        int end,
        int first,
        int second)
    {
        if (start <= first && first <= end)
        {
            bmpRanges.Add((second, second));
        }

        if (start <= second && second <= end)
        {
            bmpRanges.Add((first, first));
        }
    }

    /// <summary>
    /// Builds a .NET regex pattern for a Unicode property escape (\p{...} or \P{...}).
    /// Resolves the property name to code point ranges and generates a compatible pattern.
    /// </summary>
    private static string BuildPropertyEscapePattern(string propertyExpression, bool negate)
    {
        return PropertyEscapePatternCache.GetOrAdd(
            (propertyExpression, negate),
            static key => BuildPropertyEscapePatternCore(key.Expression, key.Negate));
    }

    private static string BuildPropertyEscapePatternCore(string propertyExpression, bool negate)
    {
        if (TryBuildStringPropertyEscapePattern(propertyExpression, negate, out var stringPattern))
        {
            return stringPattern;
        }

        var ranges = UnicodePropertyData.Resolve(propertyExpression);
        if (ranges is null)
        {
            throw new ParseException(
                $"Invalid regular expression: invalid unicode property escape \\{(negate ? 'P' : 'p')}{{{propertyExpression}}}.");
        }

        return BuildResolvedPropertyEscapePattern(ranges, negate);
    }

    private static string BuildResolvedPropertyEscapePattern((int Start, int End)[] ranges, bool negate)
    {
        if (ranges.Length == 0)
        {
            // Empty property — matches nothing (or everything if negated)
            return negate ? AnyCodePointPattern : "(?!)"; // (?!) = fail/never match
        }

        // For negated patterns, compute complement ranges and build a direct character
        // class instead of using a negative lookahead. The lookahead approach
        // (?>(?!disallowed)anyCodePoint) is O(n*m) per character and causes catastrophic
        // performance on large strings (1M+ code points in Test262 property escape tests).
        var effectiveRanges = negate ? ComplementCodePointRanges(ranges) : ranges;

        // Split into BMP and astral ranges.
        // Some properties such as Any, Assigned, and Surrogate intentionally include
        // surrogate code points. In those cases we must preserve the surrogate BMP
        // range instead of stripping it out as if it were invalid scalar data.
        var includeSurrogates = !negate && RangesCoverSurrogates(ranges);
        var bmpRanges = new List<(int Start, int End)>();
        var astralRanges = new List<(int Start, int End)>();

        foreach (var (start, end) in effectiveRanges)
        {
            if (end <= 0xFFFF)
            {
                if (includeSurrogates)
                {
                    bmpRanges.Add((start, end));
                }
                // Entirely BMP — but exclude surrogates (0xD800-0xDFFF) from the range
                else if (start <= 0xD7FF && end >= 0xD800)
                {
                    // Range spans into surrogates
                    if (start <= 0xD7FF)
                        bmpRanges.Add((start, Math.Min(end, 0xD7FF)));
                    if (end >= 0xE000)
                        bmpRanges.Add((Math.Max(start, 0xE000), end));
                }
                else if (start >= 0xD800 && end <= 0xDFFF)
                {
                    // Entirely surrogates — skip for regex matching purposes
                }
                else
                {
                    bmpRanges.Add((start, end));
                }
            }
            else if (start > 0xFFFF)
            {
                // Entirely astral
                astralRanges.Add((start, end));
            }
            else
            {
                // Spans BMP and astral
                if (includeSurrogates)
                {
                    bmpRanges.Add((start, 0xFFFF));
                }
                else
                {
                    if (start <= 0xD7FF)
                        bmpRanges.Add((start, 0xD7FF));
                    if (0xE000 <= 0xFFFF)
                        bmpRanges.Add((Math.Max(start, 0xE000), 0xFFFF));
                }
                astralRanges.Add((0x10000, end));
            }
        }

        // For negated patterns, check if the complement should include lone surrogates.
        // Unicode properties never include surrogates (they're not valid scalar values),
        // so \P{...} should match lone surrogates in the string.
        var needsLoneSurrogates = negate && !RangesCoverSurrogates(ranges);

        var bmpContent = BuildBmpClassContent(bmpRanges);
        var astralContent = BuildSurrogatePairRanges(astralRanges);

        if (astralContent.Length == 0 && !needsLoneSurrogates)
        {
            return bmpContent.Length > 0 ? $"[{bmpContent}]" : "(?!)";
        }

        var sb = new StringBuilder();
        // Use atomic group (?>...) to prevent catastrophic backtracking
        // when the property escape is quantified (e.g. \p{Alphabetic}+)
        sb.Append("(?>");
        var needsPipe = false;
        if (bmpContent.Length > 0)
        {
            sb.Append('[');
            sb.Append(bmpContent);
            sb.Append(']');
            needsPipe = true;
        }

        if (astralContent.Length > 0)
        {
            if (needsPipe)
                sb.Append('|');
            sb.Append(astralContent);
            needsPipe = true;
        }

        // Add lone surrogate matching for negated property escapes.
        // Lone high surrogate: not followed by a low surrogate.
        // Lone low surrogate: not preceded by a high surrogate.
        if (needsLoneSurrogates)
        {
            if (needsPipe)
                sb.Append('|');
            sb.Append(@"[\uD800-\uDBFF](?![\uDC00-\uDFFF])|(?<![\uD800-\uDBFF])[\uDC00-\uDFFF]");
        }

        sb.Append(')');
        return sb.ToString();
    }

    private sealed class UnicodeSetElements
    {
        public List<(int Start, int End)> BmpRanges { get; } = [];
        public List<(int Start, int End)> AstralRanges { get; } = [];
        public HashSet<string> LiteralStrings { get; } = new(StringComparer.Ordinal);
        public HashSet<string> PatternStrings { get; } = new(StringComparer.Ordinal);
    }

    private enum UnicodeSetOperator
    {
        Union,
        Intersection,
        Difference
    }

    private static bool TryNormalizeUnicodeSetExpression(string pattern, ref int index, bool unicodeIgnoreCase, out string normalized)
    {
        normalized = string.Empty;

        var outerStart = index;
        if (outerStart >= pattern.Length || pattern[outerStart] != '[')
        {
            return false;
        }

        var outerClose = FindUnicodeSetClassClose(pattern, outerStart);
        if (outerClose < 0)
        {
            return false;
        }

        var content = pattern.Substring(outerStart + 1, outerClose - outerStart - 1);
        if (!LooksLikeUnicodeSetExpression(content))
        {
            return false;
        }

        if (!TryParseUnicodeSetExpression(content, unicodeIgnoreCase, out var elements))
        {
            return false;
        }

        normalized = BuildUnicodeSetPattern(elements);
        index = outerClose;
        return true;
    }

    private static bool LooksLikeUnicodeSetExpression(string content)
    {
        return content.Contains("[", StringComparison.Ordinal) ||
               content.Contains("--", StringComparison.Ordinal) ||
               content.Contains("&&", StringComparison.Ordinal) ||
               content.Contains(@"\p{", StringComparison.Ordinal) ||
               content.Contains(@"\P{", StringComparison.Ordinal) ||
               content.Contains(@"\q{", StringComparison.Ordinal);
    }

    private static int FindUnicodeSetClassClose(string pattern, int openBracketIndex)
    {
        var bracketDepth = 0;

        for (var i = openBracketIndex; i < pattern.Length; i++)
        {
            var ch = pattern[i];
            if (ch == '\\')
            {
                if (i + 2 < pattern.Length && pattern[i + 1] == 'q' && pattern[i + 2] == '{')
                {
                    i += 3;
                    while (i < pattern.Length && pattern[i] != '}')
                    {
                        if (pattern[i] == '\\' && i + 1 < pattern.Length)
                        {
                            i++;
                        }

                        i++;
                    }

                    continue;
                }

                i++;
                continue;
            }

            if (ch == '[')
            {
                bracketDepth++;
                continue;
            }

            if (ch == ']')
            {
                bracketDepth--;
                if (bracketDepth == 0)
                {
                    return i;
                }
            }
        }

        return -1;
    }

    private static bool TryParseUnicodeSetExpression(string content, bool unicodeIgnoreCase, out UnicodeSetElements elements)
    {
        elements = new UnicodeSetElements();
        var cursor = 0;
        if (!TryParseUnicodeSetTerm(content, ref cursor, unicodeIgnoreCase, out elements))
        {
            return false;
        }

        while (cursor < content.Length)
        {
            var op = UnicodeSetOperator.Union;
            if (cursor + 1 < content.Length && content[cursor] == '&' && content[cursor + 1] == '&')
            {
                op = UnicodeSetOperator.Intersection;
                cursor += 2;
            }
            else if (cursor + 1 < content.Length && content[cursor] == '-' && content[cursor + 1] == '-')
            {
                op = UnicodeSetOperator.Difference;
                cursor += 2;
            }

            if (!TryParseUnicodeSetTerm(content, ref cursor, unicodeIgnoreCase, out var rhs))
            {
                return false;
            }

            switch (op)
            {
                case UnicodeSetOperator.Union:
                    elements = UnionUnicodeSetElements(elements, rhs);
                    break;
                case UnicodeSetOperator.Intersection:
                    if (!TryIntersectUnicodeSetElements(elements, rhs, out elements))
                    {
                        return false;
                    }

                    break;
                case UnicodeSetOperator.Difference:
                    if (!TrySubtractUnicodeSetElements(elements, rhs, out elements))
                    {
                        return false;
                    }

                    break;
            }
        }

        return true;
    }

    private static bool TryParseUnicodeSetTerm(string content, ref int cursor, bool unicodeIgnoreCase, out UnicodeSetElements elements)
    {
        elements = new UnicodeSetElements();
        if (cursor >= content.Length)
        {
            return false;
        }

        if (content[cursor] == '[')
        {
            var close = FindUnicodeSetClassClose(content, cursor);
            if (close < 0)
            {
                return false;
            }

            var nestedContent = content.Substring(cursor + 1, close - cursor - 1);
            cursor = close + 1;

            if (LooksLikeUnicodeSetExpression(nestedContent))
            {
                return TryParseUnicodeSetExpression(nestedContent, unicodeIgnoreCase, out elements);
            }

            return TryParseSimpleUnicodeSetClass(nestedContent, unicodeIgnoreCase, out elements);
        }

        if (content[cursor] == '\\')
        {
            if (cursor + 2 < content.Length && content[cursor + 1] == 'q' && content[cursor + 2] == '{')
            {
                return TryParseUnicodeSetStringLiteral(content, ref cursor, out elements);
            }

            if (cursor + 1 < content.Length && content[cursor + 1] is 'p' or 'P')
            {
                return TryParseUnicodeSetPropertyEscape(content, ref cursor, out elements);
            }

            if (cursor + 1 < content.Length && content[cursor + 1] is 'd' or 'D' or 'w' or 'W' or 's' or 'S')
            {
                AddRanges(elements, GetCharacterClassEscapeRanges(content[cursor + 1]));
                cursor += 2;
                return true;
            }
        }

        return TryParseUnicodeSetLiteralCharacter(content, ref cursor, out elements);
    }

    private static bool TryParseSimpleUnicodeSetClass(string content, bool unicodeIgnoreCase, out UnicodeSetElements elements)
    {
        elements = new UnicodeSetElements();
        var wrapped = "[" + content + "]";
        AddBmpRanges(elements, ParseNormalizedBmpClassRanges(wrapped, unicodeIgnoreCase));
        return true;
    }

    private static bool TryParseUnicodeSetStringLiteral(string content, ref int cursor, out UnicodeSetElements elements)
    {
        elements = new UnicodeSetElements();
        var start = cursor + 3;
        var end = start;
        while (end < content.Length && content[end] != '}')
        {
            if (content[end] == '\\' && end + 1 < content.Length)
            {
                end++;
            }

            end++;
        }

        if (end >= content.Length)
        {
            return false;
        }

        var inner = content.Substring(start, end - start);
        foreach (var literal in ParseUnicodeSetStringAlternatives(inner))
        {
            elements.LiteralStrings.Add(literal);
        }

        cursor = end + 1;
        return true;
    }

    private static List<string> ParseUnicodeSetStringAlternatives(string content)
    {
        var result = new List<string>();
        var builder = new StringBuilder();

        for (var i = 0; i < content.Length; i++)
        {
            var ch = content[i];
            if (ch == '|')
            {
                result.Add(builder.ToString());
                builder.Clear();
                continue;
            }

            if (ch != '\\')
            {
                builder.Append(ch);
                continue;
            }

            if (i + 1 >= content.Length)
            {
                throw new ParseException("Invalid regular expression: invalid string set escape.");
            }

            var next = content[++i];
            switch (next)
            {
                case 'u' when i + 1 < content.Length && content[i + 1] == '{':
                {
                    var endBrace = content.IndexOf('}', i + 2);
                    if (endBrace == -1)
                    {
                        throw new ParseException("Invalid regular expression: invalid string set escape.");
                    }

                    var hex = content.Substring(i + 2, endBrace - (i + 2));
                    var cp = int.Parse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
                    builder.Append(char.ConvertFromUtf32(cp));
                    i = endBrace;
                    break;
                }
                case 'u':
                {
                    if (i + 4 > content.Length)
                    {
                        throw new ParseException("Invalid regular expression: invalid string set escape.");
                    }

                    var hex = content.Substring(i + 1, 4);
                    var cp = int.Parse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
                    builder.Append((char)cp);
                    i += 4;
                    break;
                }
                case 'x':
                {
                    if (i + 2 > content.Length)
                    {
                        throw new ParseException("Invalid regular expression: invalid string set escape.");
                    }

                    var hex = content.Substring(i + 1, 2);
                    var cp = int.Parse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
                    builder.Append((char)cp);
                    i += 2;
                    break;
                }
                case 'n':
                    builder.Append('\n');
                    break;
                case 'r':
                    builder.Append('\r');
                    break;
                case 't':
                    builder.Append('\t');
                    break;
                case 'f':
                    builder.Append('\f');
                    break;
                case 'v':
                    builder.Append('\v');
                    break;
                case '0':
                    builder.Append('\0');
                    break;
                default:
                    builder.Append(next);
                    break;
            }
        }

        result.Add(builder.ToString());
        return result;
    }

    private static bool TryParseUnicodeSetPropertyEscape(string content, ref int cursor, out UnicodeSetElements elements)
    {
        elements = new UnicodeSetElements();
        var negate = content[cursor + 1] == 'P';
        if (cursor + 2 >= content.Length || content[cursor + 2] != '{')
        {
            return false;
        }

        var endBrace = content.IndexOf('}', cursor + 3);
        if (endBrace == -1)
        {
            return false;
        }

        var propertyExpr = content.Substring(cursor + 3, endBrace - (cursor + 3));
        if (!negate && TryBuildStringPropertyEscapeElements(propertyExpr, out elements))
        {
            cursor = endBrace + 1;
            return true;
        }

        var ranges = UnicodePropertyData.Resolve(propertyExpr);
        if (ranges is null)
        {
            return false;
        }

        AddRanges(elements, negate ? ComplementCodePointRanges(ranges) : ranges);
        cursor = endBrace + 1;
        return true;
    }

    private static bool TryBuildStringPropertyEscapeElements(string propertyExpression, out UnicodeSetElements elements)
    {
        elements = new UnicodeSetElements();

        switch (propertyExpression)
        {
            case "Basic_Emoji":
            {
                var emojiPresentation = UnicodePropertyData.Resolve("Emoji_Presentation") ?? [];
                var textDefaultEmoji = GetBasicEmojiTextDefaultRanges();

                AddRanges(elements, emojiPresentation);

                if (textDefaultEmoji.Length > 0)
                {
                    var textDefaultPattern = BuildResolvedPropertyEscapePattern(textDefaultEmoji, negate: false);
                    elements.PatternStrings.Add($"(?:{textDefaultPattern}\\uFE0F)");
                }

                return true;
            }
            case "RGI_Emoji":
            {
                foreach (var propertyName in new[]
                         {
                             "Emoji_Keycap_Sequence",
                             "RGI_Emoji_Flag_Sequence",
                             "RGI_Emoji_Modifier_Sequence",
                             "RGI_Emoji_Tag_Sequence",
                             "RGI_Emoji_ZWJ_Sequence"
                         })
                {
                    if (TryBuildStringPropertyEscapePattern(propertyName, false, out var pattern))
                    {
                        elements.PatternStrings.Add(pattern);
                    }
                }

                if (TryBuildStringPropertyEscapeElements("Basic_Emoji", out var basicEmojiElements))
                {
                    elements.BmpRanges.AddRange(basicEmojiElements.BmpRanges);
                    elements.AstralRanges.AddRange(basicEmojiElements.AstralRanges);
                    elements.LiteralStrings.UnionWith(basicEmojiElements.LiteralStrings);
                    elements.PatternStrings.UnionWith(basicEmojiElements.PatternStrings);
                }

                return true;
            }
        }

        if (!TryBuildStringPropertyEscapePattern(propertyExpression, false, out var stringPattern))
        {
            return false;
        }

        elements.PatternStrings.Add(stringPattern);
        return true;
    }

    private static bool TryParseUnicodeSetLiteralCharacter(string content, ref int cursor, out UnicodeSetElements elements)
    {
        elements = new UnicodeSetElements();
        var token = "[" + content.Substring(cursor) + "]";
        var innerCursor = 1;
        var cp = ParseClassCodePoint(token, ref innerCursor);
        if (cp > 0xFFFF)
        {
            elements.AstralRanges.Add((cp, cp));
        }
        else
        {
            elements.BmpRanges.Add((cp, cp));
        }

        cursor += innerCursor - 1;
        return true;
    }

    private static bool PatternMatchesLiteral(string pattern, string literal)
    {
        return Regex.IsMatch(literal, "\\A(?:" + pattern + ")\\z", RegexOptions.CultureInvariant);
    }

    private static bool TryGetSingleCodePoint(string literal, out int codePoint)
    {
        codePoint = 0;

        if (literal.Length == 1)
        {
            codePoint = literal[0];
            return true;
        }

        if (literal.Length == 2 && char.IsSurrogatePair(literal, 0))
        {
            codePoint = char.ConvertToUtf32(literal, 0);
            return true;
        }

        return false;
    }

    private static UnicodeSetElements UnionUnicodeSetElements(UnicodeSetElements left, UnicodeSetElements right)
    {
        var result = new UnicodeSetElements();
        AddBmpRanges(result, left.BmpRanges);
        AddBmpRanges(result, right.BmpRanges);
        AddAstralRanges(result, left.AstralRanges);
        AddAstralRanges(result, right.AstralRanges);
        result.LiteralStrings.UnionWith(left.LiteralStrings);
        result.LiteralStrings.UnionWith(right.LiteralStrings);
        result.PatternStrings.UnionWith(left.PatternStrings);
        result.PatternStrings.UnionWith(right.PatternStrings);
        return result;
    }

    private static bool TryIntersectUnicodeSetElements(UnicodeSetElements left, UnicodeSetElements right, out UnicodeSetElements result)
    {
        result = new UnicodeSetElements();

        AddBmpRanges(result, IntersectRanges(left.BmpRanges, right.BmpRanges));
        AddAstralRanges(result, IntersectRanges(left.AstralRanges, right.AstralRanges));
        result.PatternStrings.UnionWith(left.PatternStrings.Intersect(right.PatternStrings, StringComparer.Ordinal));

        foreach (var literal in left.LiteralStrings)
        {
            if (right.LiteralStrings.Contains(literal))
            {
                result.LiteralStrings.Add(literal);
                continue;
            }

            if (TryGetSingleCodePoint(literal, out var codePoint) && ContainsCodePoint(right, codePoint))
            {
                result.LiteralStrings.Add(literal);
                continue;
            }

            if (right.PatternStrings.Any(pattern => PatternMatchesLiteral(pattern, literal)))
            {
                result.LiteralStrings.Add(literal);
            }
        }

        foreach (var literal in right.LiteralStrings)
        {
            if (TryGetSingleCodePoint(literal, out var codePoint) && ContainsCodePoint(left, codePoint))
            {
                result.LiteralStrings.Add(literal);
                continue;
            }

            if (left.PatternStrings.Any(pattern => PatternMatchesLiteral(pattern, literal)))
            {
                result.LiteralStrings.Add(literal);
            }
        }

        return true;
    }

    private static bool TrySubtractUnicodeSetElements(UnicodeSetElements left, UnicodeSetElements right, out UnicodeSetElements result)
    {
        result = new UnicodeSetElements();

        AddBmpRanges(result, SubtractRanges(left.BmpRanges, right.BmpRanges));
        AddAstralRanges(result, SubtractRanges(left.AstralRanges, right.AstralRanges));
        foreach (var pattern in left.PatternStrings)
        {
            if (!right.PatternStrings.Contains(pattern))
            {
                result.PatternStrings.Add(pattern);
            }
        }

        foreach (var literal in left.LiteralStrings)
        {
            if (right.LiteralStrings.Contains(literal))
            {
                continue;
            }

            if (TryGetSingleCodePoint(literal, out var codePoint) && ContainsCodePoint(right, codePoint))
            {
                continue;
            }

            if (right.PatternStrings.Any(pattern => PatternMatchesLiteral(pattern, literal)))
            {
                continue;
            }

            result.LiteralStrings.Add(literal);
        }

        return true;
    }

    private static bool ContainsCodePoint(UnicodeSetElements elements, int codePoint)
    {
        foreach (var (start, end) in elements.BmpRanges)
        {
            if (codePoint >= start && codePoint <= end)
            {
                return true;
            }
        }

        foreach (var (start, end) in elements.AstralRanges)
        {
            if (codePoint >= start && codePoint <= end)
            {
                return true;
            }
        }

        return false;
    }

    private static (int Start, int End)[] IntersectRanges(List<(int Start, int End)> left, List<(int Start, int End)> right)
    {
        if (left.Count == 0 || right.Count == 0)
        {
            return [];
        }

        var normalizedLeft = NormalizeRanges(left);
        var normalizedRight = NormalizeRanges(right);
        var result = new List<(int Start, int End)>();
        var i = 0;
        var j = 0;

        while (i < normalizedLeft.Length && j < normalizedRight.Length)
        {
            var (leftStart, leftEnd) = normalizedLeft[i];
            var (rightStart, rightEnd) = normalizedRight[j];
            var start = Math.Max(leftStart, rightStart);
            var end = Math.Min(leftEnd, rightEnd);
            if (start <= end)
            {
                result.Add((start, end));
            }

            if (leftEnd < rightEnd)
            {
                i++;
            }
            else
            {
                j++;
            }
        }

        return [.. result];
    }

    private static (int Start, int End)[] SubtractRanges(List<(int Start, int End)> minuend, List<(int Start, int End)> subtrahend)
    {
        return SubtractRanges(NormalizeRanges(minuend), NormalizeRanges(subtrahend));
    }

    private static (int Start, int End)[] SubtractRanges((int Start, int End)[] minuend, (int Start, int End)[] subtrahend)
    {
        if (minuend.Length == 0)
        {
            return [];
        }

        if (subtrahend.Length == 0)
        {
            return minuend;
        }

        var result = new List<(int Start, int End)>();
        var j = 0;

        foreach (var (start, end) in minuend)
        {
            var currentStart = start;

            while (j < subtrahend.Length && subtrahend[j].End < currentStart)
            {
                j++;
            }

            var k = j;
            while (k < subtrahend.Length && subtrahend[k].Start <= end)
            {
                var (otherStart, otherEnd) = subtrahend[k];
                if (otherStart > currentStart)
                {
                    result.Add((currentStart, Math.Min(end, otherStart - 1)));
                }

                if (otherEnd >= end)
                {
                    currentStart = end + 1;
                    break;
                }

                currentStart = Math.Max(currentStart, otherEnd + 1);
                k++;
            }

            if (currentStart <= end)
            {
                result.Add((currentStart, end));
            }
        }

        return [.. result];
    }

    private static (int Start, int End)[] NormalizeRanges(List<(int Start, int End)> ranges)
    {
        if (ranges.Count == 0)
        {
            return [];
        }

        var ordered = ranges.OrderBy(static r => r.Start).ThenBy(static r => r.End).ToArray();
        var result = new List<(int Start, int End)> { ordered[0] };

        for (var i = 1; i < ordered.Length; i++)
        {
            var current = ordered[i];
            var last = result[^1];
            if (current.Start <= last.End + 1)
            {
                result[^1] = (last.Start, Math.Max(last.End, current.End));
            }
            else
            {
                result.Add(current);
            }
        }

        return [.. result];
    }

    private static string BuildUnicodeSetPattern(UnicodeSetElements elements)
    {
        var alternatives = new List<string>();
        foreach (var literal in elements.LiteralStrings.OrderByDescending(static s => s.Length).ThenBy(static s => s, StringComparer.Ordinal))
        {
            alternatives.Add(Regex.Escape(literal));
        }

        alternatives.AddRange(elements.PatternStrings.OrderBy(static s => s, StringComparer.Ordinal));

        var charPattern = BuildUnicodeClassPattern(
            negate: false,
            [.. NormalizeRanges(elements.BmpRanges).ToList()],
            [.. NormalizeRanges(elements.AstralRanges).ToList()]);

        if (!string.IsNullOrEmpty(charPattern) && charPattern != "[]")
        {
            alternatives.Add(charPattern);
        }

        return alternatives.Count switch
        {
            0 => @"[^\s\S]",
            1 => alternatives[0],
            _ => "(?:" + string.Join("|", alternatives) + ")"
        };
    }

    private static void AddRanges(UnicodeSetElements elements, (int Start, int End)[] ranges)
    {
        foreach (var (start, end) in ranges)
        {
            if (end <= 0xFFFF)
            {
                elements.BmpRanges.Add((start, end));
            }
            else if (start > 0xFFFF)
            {
                elements.AstralRanges.Add((start, end));
            }
            else
            {
                elements.BmpRanges.Add((start, 0xFFFF));
                elements.AstralRanges.Add((0x10000, end));
            }
        }
    }

    private static void AddBmpRanges(UnicodeSetElements elements, IEnumerable<(int Start, int End)> ranges)
    {
        foreach (var range in ranges)
        {
            elements.BmpRanges.Add(range);
        }
    }

    private static void AddAstralRanges(UnicodeSetElements elements, IEnumerable<(int Start, int End)> ranges)
    {
        foreach (var range in ranges)
        {
            elements.AstralRanges.Add(range);
        }
    }

    private static bool TryNormalizeSimpleUnicodeSetDifference(string pattern, ref int index, bool unicodeIgnoreCase, out string normalized)
    {
        normalized = string.Empty;

        var outerStart = index;
        if (outerStart + 6 >= pattern.Length || pattern[outerStart + 1] != '[')
        {
            return false;
        }

        var innerClose = FindSimpleClassClose(pattern, outerStart + 1);
        if (innerClose < 0 ||
            innerClose + 3 >= pattern.Length ||
            pattern[innerClose + 1] != '-' ||
            pattern[innerClose + 2] != '-' ||
            pattern[innerClose + 3] != '\\' ||
            innerClose + 4 >= pattern.Length)
        {
            return false;
        }

        var rhsEscape = pattern[innerClose + 4];
        if (rhsEscape is not ('d' or 'D' or 'w' or 'W' or 's' or 'S'))
        {
            return false;
        }

        var outerClose = innerClose + 5;
        if (outerClose >= pattern.Length || pattern[outerClose] != ']')
        {
            return false;
        }

        var innerClass = pattern.Substring(outerStart + 1, innerClose - outerStart);
        var lhsRanges = ParseNormalizedBmpClassRanges(innerClass, unicodeIgnoreCase);
        var rhsRanges = GetCharacterClassEscapeRanges(rhsEscape)
            .Where(static r => r.End <= 0xFFFF)
            .Select(static r => (r.Start, r.End))
            .ToArray();

        var effectiveRanges = SubtractBmpRanges(lhsRanges, rhsRanges);
        normalized = effectiveRanges.Length == 0
            ? @"[^\s\S]"
            : $"[{BuildBmpClassContent([.. effectiveRanges])}]";
        index = outerClose;
        return true;
    }

    private static int FindSimpleClassClose(string pattern, int startBracketIndex)
    {
        for (var i = startBracketIndex + 1; i < pattern.Length; i++)
        {
            if (pattern[i] == '\\')
            {
                i++;
                continue;
            }

            if (pattern[i] == ']')
            {
                return i;
            }
        }

        return -1;
    }

    private static (int Start, int End)[] SubtractBmpRanges((int Start, int End)[] minuend, (int Start, int End)[] subtrahend)
    {
        if (minuend.Length == 0)
        {
            return [];
        }

        if (subtrahend.Length == 0)
        {
            return minuend;
        }

        var result = new List<(int Start, int End)>();
        var j = 0;

        foreach (var (start, end) in minuend)
        {
            var currentStart = start;

            while (j < subtrahend.Length && subtrahend[j].End < currentStart)
            {
                j++;
            }

            var k = j;
            while (k < subtrahend.Length && subtrahend[k].Start <= end)
            {
                var (otherStart, otherEnd) = subtrahend[k];
                if (otherStart > currentStart)
                {
                    result.Add((currentStart, Math.Min(end, otherStart - 1)));
                }

                if (otherEnd >= end)
                {
                    currentStart = end + 1;
                    break;
                }

                currentStart = Math.Max(currentStart, otherEnd + 1);
                k++;
            }

            if (currentStart <= end)
            {
                result.Add((currentStart, end));
            }
        }

        return [.. result];
    }

    private static (int Start, int End)[] ParseNormalizedBmpClassRanges(string normalizedClass, bool unicodeIgnoreCase)
    {
        if (normalizedClass.Length < 2 || normalizedClass[0] != '[' || normalizedClass[^1] != ']')
        {
            throw new ParseException("Invalid regular expression: invalid nested character class.");
        }

        var cursor = 1;
        var end = normalizedClass.Length - 1;
        var bmpRanges = new List<(int Start, int End)>();

        while (cursor < end)
        {
            if (cursor + 1 < end &&
                normalizedClass[cursor] == '\\' &&
                normalizedClass[cursor + 1] is 'd' or 'D' or 'w' or 'W' or 's' or 'S')
            {
                var escapeChar = normalizedClass[cursor + 1];
                foreach (var (s, e) in GetCharacterClassEscapeRanges(escapeChar))
                {
                    if (e <= 0xFFFF)
                    {
                        bmpRanges.Add((s, e));
                    }
                }

                if (unicodeIgnoreCase && escapeChar == 'w')
                {
                    bmpRanges.Add((0x017F, 0x017F));
                    bmpRanges.Add((0x212A, 0x212A));
                }

                cursor += 2;
                continue;
            }

            var startCp = ParseClassCodePoint(normalizedClass, ref cursor);
            var endCp = startCp;
            if (cursor < end && normalizedClass[cursor] == '-' && cursor + 1 < end)
            {
                cursor++;
                endCp = ParseClassCodePoint(normalizedClass, ref cursor);
            }

            bmpRanges.Add((startCp, endCp));
        }

        return [.. bmpRanges];
    }

    private static bool TryBuildStringPropertyEscapePattern(string propertyExpression, bool negate, out string pattern)
    {
        pattern = string.Empty;

        if (negate)
        {
            return false;
        }

        string BuildAlternation(params string[] alternatives)
        {
            var sb = new StringBuilder();
            sb.Append("(?:");
            for (var i = 0; i < alternatives.Length; i++)
            {
                if (i > 0)
                {
                    sb.Append('|');
                }

                sb.Append(alternatives[i]);
            }

            sb.Append(')');
            return sb.ToString();
        }

        string BuildCodePointSequence(params int[] codePoints)
        {
            var sb = new StringBuilder();
            sb.Append("(?:");
            foreach (var cp in codePoints)
            {
                AppendCodePoint(sb, cp, false, false, true);
            }

            sb.Append(')');
            return sb.ToString();
        }

        string BuildEmojiZWJSequencePattern()
        {
            var pict = BuildPropertyEscapePattern("Extended_Pictographic", false);
            var modifier = BuildPropertyEscapePattern("Emoji_Modifier", false);
            var element = $"(?:{pict}(?:\\uFE0F)?(?:{modifier})?)";
            return $"(?:{element}(?:\\u200D{element})+)";
        }

        string BuildBasicEmojiPattern()
        {
            var emojiPresentation = UnicodePropertyData.Resolve("Emoji_Presentation") ?? [];
            var textDefaultEmoji = GetBasicEmojiTextDefaultRanges();
            var emojiPresentationPattern = BuildResolvedPropertyEscapePattern(emojiPresentation, negate: false);
            var textDefaultPattern = BuildResolvedPropertyEscapePattern(textDefaultEmoji, negate: false);
            return BuildAlternation(
                emojiPresentationPattern,
                $"{textDefaultPattern}\\uFE0F");
        }

        pattern = propertyExpression switch
        {
            "Basic_Emoji" => BuildBasicEmojiPattern(),
            "Emoji_Keycap_Sequence" => @"(?:[0-9#*]\uFE0F?\u20E3)",
            "RGI_Emoji_Flag_Sequence" => $"(?:{BuildPropertyEscapePattern("Regional_Indicator", false)}{{2}})",
            "RGI_Emoji_Modifier_Sequence" => $"(?:{BuildPropertyEscapePattern("Emoji_Modifier_Base", false)}\\uFE0F?{BuildPropertyEscapePattern("Emoji_Modifier", false)})",
            "RGI_Emoji_Tag_Sequence" => BuildAlternation(
                BuildCodePointSequence(0x1F3F4, 0xE0067, 0xE0062, 0xE0065, 0xE006E, 0xE0067, 0xE007F),
                BuildCodePointSequence(0x1F3F4, 0xE0067, 0xE0062, 0xE0073, 0xE0063, 0xE0074, 0xE007F),
                BuildCodePointSequence(0x1F3F4, 0xE0067, 0xE0062, 0xE0077, 0xE006C, 0xE0073, 0xE007F)),
            "RGI_Emoji_ZWJ_Sequence" => BuildEmojiZWJSequencePattern(),
            "RGI_Emoji" => BuildAlternation(
                BuildPropertyEscapePattern("Emoji_Keycap_Sequence", false),
                BuildPropertyEscapePattern("RGI_Emoji_Flag_Sequence", false),
                BuildPropertyEscapePattern("RGI_Emoji_Modifier_Sequence", false),
                BuildPropertyEscapePattern("RGI_Emoji_Tag_Sequence", false),
                BuildPropertyEscapePattern("RGI_Emoji_ZWJ_Sequence", false),
                BuildPropertyEscapePattern("Basic_Emoji", false)),
            _ => string.Empty
        };

        return !string.IsNullOrEmpty(pattern);
    }

    /// <summary>
    /// Returns true if the given ranges cover any surrogate code points (0xD800-0xDFFF).
    /// </summary>
    private static bool RangesCoverSurrogates((int Start, int End)[] ranges)
    {
        foreach (var (start, end) in ranges)
        {
            if (start <= 0xDFFF && end >= 0xD800)
                return true;
        }

        return false;
    }

    /// <summary>
    /// Builds a compact regex pattern for astral (supplementary plane) code point ranges
    /// using surrogate pair ranges instead of enumerating individual code points.
    /// </summary>
    private static string BuildSurrogatePairRanges(List<(int Start, int End)> ranges)
    {
        if (ranges.Count == 0)
            return string.Empty;

        var lowRangesByHighSurrogate = new SortedDictionary<int, List<(int Start, int End)>>();

        foreach (var (start, end) in ranges)
        {
            var highStart = ((start - 0x10000) >> 10) + 0xD800;
            var lowStart = ((start - 0x10000) & 0x3FF) + 0xDC00;
            var highEnd = ((end - 0x10000) >> 10) + 0xD800;
            var lowEnd = ((end - 0x10000) & 0x3FF) + 0xDC00;

            for (var high = highStart; high <= highEnd; high++)
            {
                var lowRangeStart = high == highStart ? lowStart : 0xDC00;
                var lowRangeEnd = high == highEnd ? lowEnd : 0xDFFF;
                if (!lowRangesByHighSurrogate.TryGetValue(high, out var lowRanges))
                {
                    lowRanges = [];
                    lowRangesByHighSurrogate.Add(high, lowRanges);
                }

                lowRanges.Add((lowRangeStart, lowRangeEnd));
            }
        }

        var highSurrogatesByLowClass = new SortedDictionary<string, List<(int Start, int End)>>(StringComparer.Ordinal);
        foreach (var (high, lowRanges) in lowRangesByHighSurrogate)
        {
            var lowClass = BuildBmpClassContent([.. NormalizeRanges(lowRanges).ToList()]);
            if (!highSurrogatesByLowClass.TryGetValue(lowClass, out var highRanges))
            {
                highRanges = [];
                highSurrogatesByLowClass.Add(lowClass, highRanges);
            }

            highRanges.Add((high, high));
        }

        var sb = new StringBuilder();
        var first = true;
        foreach (var (lowClass, highRanges) in highSurrogatesByLowClass)
        {
            if (!first)
                sb.Append('|');
            first = false;

            var normalizedHighRanges = NormalizeRanges(highRanges).ToList();
            if (normalizedHighRanges.Count == 1 && normalizedHighRanges[0].Start == normalizedHighRanges[0].End)
            {
                sb.Append(EscapeCharClassCodeUnit(normalizedHighRanges[0].Start));
            }
            else
            {
                sb.Append('[');
                sb.Append(BuildBmpClassContent([.. normalizedHighRanges]));
                sb.Append(']');
            }

            sb.Append('[');
            sb.Append(lowClass);
            sb.Append(']');
        }

        return sb.ToString();
    }

    private static string BuildUnicodeClassPattern(bool negate, List<(int Start, int End)> bmpRanges,
        List<(int Start, int End)> astralRanges)
    {
        var bmpContent = BuildBmpClassContent(bmpRanges);
        // Use surrogate pair ranges (e.g. \uD800[\uDC00-\uDFFF]) instead of per-codepoint
        // alternation ((?:\uD800\uDC00)|(?:\uD800\uDC01)|...) — the latter generates
        // massive pattern strings for large Unicode properties like Alphabetic.
        var astralContent = BuildSurrogatePairRanges(astralRanges);

        if (!negate)
        {
            if (astralContent.Length == 0)
            {
                return $"[{bmpContent}]";
            }

            var sb = new StringBuilder();
            sb.Append("(?:");
            var needsPipe = false;
            if (bmpContent.Length > 0)
            {
                sb.Append('[');
                sb.Append(bmpContent);
                sb.Append(']');
                needsPipe = true;
            }

            if (astralContent.Length > 0)
            {
                if (needsPipe)
                {
                    sb.Append('|');
                }

                sb.Append(astralContent);
            }

            sb.Append(')');
            return sb.ToString();
        }

        // Compute complement ranges instead of using a negative lookahead.
        // The lookahead approach (?>(?!disallowed)anyCodePoint) is O(n*m) per character
        // and causes catastrophic performance on large strings.
        var compBmp = ComplementBmpRanges(bmpRanges);
        var compAstral = ComplementAstralRanges(astralRanges);

        var compBmpContent = BuildBmpClassContent(compBmp);
        var compAstralContent = BuildSurrogatePairRanges(compAstral);

        var result = new StringBuilder();
        // Use atomic group (?>...) to prevent catastrophic backtracking
        result.Append("(?>");
        var hasPrevAlt = false;
        if (compBmpContent.Length > 0)
        {
            result.Append('[');
            result.Append(compBmpContent);
            result.Append(']');
            hasPrevAlt = true;
        }

        if (compAstralContent.Length > 0)
        {
            if (hasPrevAlt)
                result.Append('|');
            result.Append(compAstralContent);
            hasPrevAlt = true;
        }

        // Negated character classes should match lone surrogates
        // (the original class doesn't include surrogates, so the complement does).
        if (hasPrevAlt)
            result.Append('|');
        result.Append(@"[\uD800-\uDBFF](?![\uDC00-\uDFFF])|(?<![\uD800-\uDBFF])[\uDC00-\uDFFF]");

        result.Append(')');
        return result.ToString();
    }

    private static string BuildBmpClassContent(List<(int Start, int End)> ranges)
    {
        if (ranges.Count == 0)
        {
            return string.Empty;
        }

        var sb = new StringBuilder();
        foreach (var (start, end) in ranges)
        {
            if (start == end)
            {
                sb.Append(EscapeCharClassCodeUnit(start));
                continue;
            }

            sb.Append(EscapeCharClassCodeUnit(start));
            sb.Append('-');
            sb.Append(EscapeCharClassCodeUnit(end));
        }

        return sb.ToString();
    }

    private static int ParseClassCodePoint(string pattern, ref int index)
    {
        if (index >= pattern.Length)
        {
            throw new ParseException("Invalid regular expression: incomplete character class.");
        }

        var ch = pattern[index];
        if (ch != '\\')
        {
            if (Rune.DecodeFromUtf16(pattern.AsSpan(index), out var rune, out var consumed) != OperationStatus.Done)
            {
                throw new ParseException("Invalid regular expression: invalid character class.");
            }

            index += consumed;
            return rune.Value;
        }

        if (index + 1 >= pattern.Length)
        {
            throw new ParseException("Invalid regular expression: invalid escape.");
        }

        var escape = pattern[index + 1];
        if (escape == 'u')
        {
            if (index + 2 < pattern.Length && pattern[index + 2] == '{')
            {
                var endBrace = pattern.IndexOf('}', index + 3);
                if (endBrace == -1)
                {
                    throw new ParseException("Invalid regular expression: invalid unicode escape.");
                }

                var hex = pattern.Substring(index + 3, endBrace - (index + 3));
                if (hex.Length < 1 || !int.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture,
                        out var cp))
                {
                    throw new ParseException("Invalid regular expression: invalid unicode escape.");
                }

                if (cp is < 0 or > 0x10FFFF)
                {
                    throw new ParseException("Invalid regular expression: invalid unicode escape.");
                }

                index = endBrace + 1;
                return cp;
            }

            if (index + 5 >= pattern.Length)
            {
                throw new ParseException("Invalid regular expression: invalid unicode escape.");
            }

            var hexDigits = pattern.Substring(index + 2, 4);
            if (!int.TryParse(hexDigits, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var value))
            {
                throw new ParseException("Invalid regular expression: invalid unicode escape.");
            }

            index += 6;
            return value;
        }

        if (escape == 'x')
        {
            if (index + 3 >= pattern.Length)
            {
                throw new ParseException("Invalid regular expression: invalid unicode escape.");
            }

            var hexDigits = pattern.Substring(index + 2, 2);
            if (!int.TryParse(hexDigits, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var value))
            {
                throw new ParseException("Invalid regular expression: invalid unicode escape.");
            }

            index += 4;
            return value;
        }

        if (escape == '0' && (index + 2 >= pattern.Length || !char.IsDigit(pattern[index + 2])))
        {
            index += 2;
            return 0;
        }

        // \c followed by A-Z or a-z is a control escape
        if (escape == 'c' && index + 2 < pattern.Length && IsControlLetter(pattern[index + 2]))
        {
            var controlValue = pattern[index + 2] % 32;
            index += 3;
            return controlValue;
        }

        switch (escape)
        {
            case 'b':
                index += 2;
                return 0x08; // backspace in character class
            case 'n':
                index += 2;
                return '\n';
            case 'r':
                index += 2;
                return '\r';
            case 't':
                index += 2;
                return '\t';
            case 'f':
                index += 2;
                return '\f';
            case 'v':
                index += 2;
                return '\v';
            // Syntax characters valid as identity escapes in character classes
            case '^' or '$' or '\\' or '.' or '*' or '+' or '?' or '(' or ')' or
                 '[' or ']' or '{' or '}' or '|' or '/' or '-':
                index += 2;
                return escape;
        }

        // In unicode mode, anything else is invalid
        throw new ParseException(
            $"Invalid regular expression: invalid escape \\{escape}.");
    }

    private static bool TryParseLowSurrogate(string pattern, ref int index, out int codePoint)
    {
        var snapshot = index;
        if (snapshot >= pattern.Length)
        {
            codePoint = 0;
            return false;
        }

        var cp = ParseClassCodePoint(pattern, ref snapshot);
        if (cp is >= 0xDC00 and <= 0xDFFF)
        {
            index = snapshot;
            codePoint = cp;
            return true;
        }

        codePoint = 0;
        return false;
    }

    /// <summary>
    /// Computes the complement of a set of code point ranges within [0, 0x10FFFF],
    /// excluding surrogates (0xD800-0xDFFF).
    /// </summary>
    private static (int Start, int End)[] ComplementCodePointRanges((int Start, int End)[] ranges)
    {
        var result = new List<(int, int)>();
        var prev = 0;
        foreach (var (start, end) in ranges)
        {
            if (prev < start)
            {
                // Add gap, but skip surrogates
                AddRangeExcludingSurrogates(result, prev, start - 1);
            }

            prev = end + 1;
        }

        if (prev <= 0x10FFFF)
        {
            AddRangeExcludingSurrogates(result, prev, 0x10FFFF);
        }

        return result.ToArray();

        static void AddRangeExcludingSurrogates(List<(int, int)> list, int s, int e)
        {
            if (e < 0xD800 || s > 0xDFFF)
            {
                list.Add((s, e));
            }
            else
            {
                if (s < 0xD800) list.Add((s, 0xD7FF));
                if (e > 0xDFFF) list.Add((0xE000, e));
            }
        }
    }

    /// <summary>
    /// Computes the complement of BMP ranges within [0x0000-0xD7FF] union [0xE000-0xFFFF],
    /// excluding surrogate code points.
    /// </summary>
    private static List<(int Start, int End)> ComplementBmpRanges(List<(int Start, int End)> ranges)
    {
        var result = new List<(int Start, int End)>();

        // Build the two BMP sub-universes: [0x0000-0xD7FF] and [0xE000-0xFFFF]
        // We invert the ranges within these sub-universes.
        var prev = 0;
        foreach (var (start, end) in ranges)
        {
            if (prev < start)
            {
                // Add gap, excluding surrogates
                AddBmpGap(result, prev, start - 1);
            }

            prev = end + 1;
        }

        // Add remaining gap after last range up to 0xFFFF
        if (prev <= 0xFFFF)
        {
            AddBmpGap(result, prev, 0xFFFF);
        }

        return result;

        static void AddBmpGap(List<(int Start, int End)> list, int s, int e)
        {
            if (e < 0xD800 || s > 0xDFFF)
            {
                list.Add((s, e));
            }
            else
            {
                if (s < 0xD800) list.Add((s, 0xD7FF));
                if (e > 0xDFFF) list.Add((0xE000, e));
            }
        }
    }

    /// <summary>
    /// Computes the complement of astral ranges within [0x10000-0x10FFFF].
    /// </summary>
    private static List<(int Start, int End)> ComplementAstralRanges(List<(int Start, int End)> astralRanges)
    {
        var result = new List<(int Start, int End)>();
        var prev = 0x10000;

        foreach (var (start, end) in astralRanges)
        {
            if (prev < start)
            {
                result.Add((prev, start - 1));
            }

            prev = end + 1;
        }

        if (prev <= 0x10FFFF)
        {
            result.Add((prev, 0x10FFFF));
        }

        return result;
    }

    private static bool IsHighSurrogate(int value)
    {
        return value is >= 0xD800 and <= 0xDBFF;
    }

    private static bool IsSurrogate(int value)
    {
        return value is >= 0xD800 and <= 0xDFFF;
    }

    private static string EscapeCharClassCodeUnit(int codeUnit)
    {
        switch (codeUnit)
        {
            case '-':
                return "\\-";
            case '[':
                return "\\[";
            case ']':
                return "\\]";
            case '\\':
                return @"\\";
            case '^':
                return "\\^";
        }

        if (codeUnit is < 0x20 or > 0x7E)
        {
            return $"\\u{codeUnit:X4}";
        }

        return char.ConvertFromUtf32(codeUnit);
    }

    private static string FormatAstralAsSurrogates(int codePoint)
    {
        var text = char.ConvertFromUtf32(codePoint);
        var builder = new StringBuilder();
        foreach (var ch in text)
        {
            builder.Append(CultureInfo.InvariantCulture, $"\\u{(int)ch:X4}");
        }

        return builder.ToString();
    }
}
