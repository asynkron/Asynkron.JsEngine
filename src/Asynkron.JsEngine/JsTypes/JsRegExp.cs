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

    private Regex? _compiledRegex;

    public JsRegExp(string pattern, string flags = "", RealmState? realmState = null, JsObject? existingObject = null)
    {
        Pattern = pattern;
        Flags = flags;
        RealmState = realmState;
        JsObject = existingObject ?? new JsObject();

        ValidateFlags(Flags);
        var hasUnicodeFlag = Flags.Contains('u', StringComparison.Ordinal) ||
                             Flags.Contains('v', StringComparison.Ordinal);
        var normalized = NormalizePattern(pattern, hasUnicodeFlag, IgnoreCase, DotAll, Multiline);
        var sanitized = SanitizeGroupNamesForDotNet(normalized, out var nameMapping);
        var renamed = RenameDuplicateGroups(sanitized, ref nameMapping, out _duplicateGroupNames);
        _normalizedPattern = _duplicateGroupNames is not null
            ? InsertQuantifierResets(renamed, _duplicateGroupNames)
            : renamed;
        _groupNameMapping = nameMapping;

        // Convert JavaScript regex flags to .NET RegexOptions.
        // Always use Compiled: the JIT cost is amortized across matches,
        // and large property-escape patterns are orders of magnitude faster compiled.
        var options = RegexOptions.CultureInvariant | RegexOptions.Compiled;
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

        try
        {
            var regex = EnsureRegex();
            _groupReorderMap = BuildGroupReorderMap(regex, _normalizedPattern);
        }
        catch (ArgumentException ex)
        {
            throw new ParseException(ex.Message);
        }
    }

    public string Pattern { get; }

    public string Flags { get; }

    public bool Global => Flags.Contains('g', StringComparison.Ordinal);
    public bool IgnoreCase => Flags.Contains('i', StringComparison.Ordinal);
    public bool Multiline => Flags.Contains('m', StringComparison.Ordinal);
    public bool DotAll => Flags.Contains('s', StringComparison.Ordinal);
    public bool Unicode => Flags.Contains('u', StringComparison.Ordinal);
    public bool Sticky => Flags.Contains('y', StringComparison.Ordinal);
    public bool HasIndices => Flags.Contains('d', StringComparison.Ordinal);
    public bool UnicodeSets => Flags.Contains('v', StringComparison.Ordinal);

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

    private Regex EnsureRegex()
    {
        return _compiledRegex ??= new Regex(CapLargeQuantifiers(_normalizedPattern), _regexOptions);
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

    private static void ValidateFlags(string flags)
    {
        var seen = new HashSet<char>();
        var hasUnicode = false;
        var hasUnicodeSets = false;
        foreach (var flag in flags)
        {
            if (!seen.Add(flag))
            {
                throw new ParseException($"Invalid regular expression flags: duplicate '{flag}'.");
            }

            if (flag is not ('g' or 'i' or 'm' or 'u' or 'y' or 's' or 'd' or 'v'))
            {
                throw new ParseException($"Invalid regular expression flag '{flag}'.");
            }

            if (flag == 'u')
            {
                hasUnicode = true;
                if (hasUnicodeSets)
                {
                    throw new ParseException("Invalid regular expression flag 'u'.");
                }
            }

            if (flag != 'v')
            {
                continue;
            }

            hasUnicodeSets = true;
            if (hasUnicode)
            {
                throw new ParseException("Invalid regular expression flag 'v'.");
            }
        }
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

                var value = ResolveDuplicateGroupValue(regex, renamedNames, captureValues);
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
    private static JsValue ResolveDuplicateGroupValue(Regex regex, string[] renamedNames, JsValue[] values)
    {
        foreach (var renamedName in renamedNames)
        {
            var num = regex.GroupNumberFromName(renamedName);
            if (num >= 0 && num < values.Length && values[num] != JsValue.Undefined)
            {
                return values[num];
            }
        }

        return JsValue.Undefined;
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

                var value = ResolveDuplicateGroupValue(regex, renamedNames, indexValues);
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

    private static string NormalizePattern(string pattern, bool hasUnicodeFlag, bool ignoreCase, bool dotAll, bool multiline)
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
                        builder.Append('\\');
                        builder.Append(netNum.ToString(CultureInfo.InvariantCulture));
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
                            builder.Append(UnicodeEcmaNonWhitespacePattern);
                            i++;
                            continue;
                        case 'w':
                            builder.Append(useUnicodeIgnoreCaseWord ? EcmaWordClassUnicodeIgnoreCase : EcmaWordClass);
                            i++;
                            continue;
                        case 'W':
                            builder.Append(useUnicodeIgnoreCaseWord ? EcmaNonWordClassUnicodeIgnoreCase : UnicodeEcmaNonWordPattern);
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
                            builder.Append(UnicodeEcmaNonDigitPattern);
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
                var normalized = NormalizeUnicodeCharacterClass(pattern, ref i, effectiveIgnoreCase);
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
                                builder.Append('\\');
                                builder.Append(netNum.ToString(CultureInfo.InvariantCulture));
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

        return builder.ToString();
    }

    /// <summary>
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
        // Build the reset sequence: (?>(?<-n1>)?(?<-n2>)?...)
        var resetBuilder = new StringBuilder("(?>", 64);
        foreach (var names in duplicateGroupNames.Values)
        {
            foreach (var name in names)
            {
                resetBuilder.Append("(?<-");
                resetBuilder.Append(name);
                resetBuilder.Append(">)?");
            }
        }

        resetBuilder.Append(')');
        var resetStr = resetBuilder.ToString();

        // Phase 1: Walk pattern to determine which groups contain duplicate captures.
        // For each group, record: open position, depth, and whether it contains duplicates.
        // Use index into a list as the group ID.
        var groupOpenPositions = new List<int>(); // index = group ID, value = position of char AFTER opener
        var groupContainsDup = new List<bool>(); // index = group ID
        var groupParent = new List<int>(); // index = group ID, value = parent group ID (-1 for top level)
        var groupStack = new Stack<int>(); // stack of group IDs
        var topLevelAlternatives = new List<int>(); // positions of | at top level (no enclosing group)

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
                                groupStack.Push(groupId);

                                // Mark all ancestor groups as containing duplicates
                                var ancestor = parentId;
                                while (ancestor >= 0)
                                {
                                    groupContainsDup[ancestor] = true;
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

        // Phase 3: Build the new pattern with resets inserted
        var result = new StringBuilder(pattern.Length + (insertPositions.Count * resetStr.Length));
        for (i = 0; i < pattern.Length; i++)
        {
            if (insertPositions.Contains(i))
            {
                result.Append(resetStr);
            }

            result.Append(pattern[i]);
        }

        // Check if we need to insert at the very end (unlikely but handle it)
        if (insertPositions.Contains(pattern.Length))
        {
            result.Append(resetStr);
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

    private static string NormalizeUnicodeCharacterClass(string pattern, ref int index, bool unicodeIgnoreCase = false)
    {
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
                bmpRanges.Add((cp, endCp));
            }
        }

        if (cursor >= pattern.Length || pattern[cursor] != ']')
        {
            throw new ParseException("Invalid regular expression: unterminated character class.");
        }

        index = cursor;
        return BuildUnicodeClassPattern(negate, bmpRanges, astralRanges);
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
        var ranges = UnicodePropertyData.Resolve(propertyExpression);
        if (ranges is null)
        {
            throw new ParseException(
                $"Invalid regular expression: invalid unicode property escape \\{(negate ? 'P' : 'p')}{{{propertyExpression}}}.");
        }

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

        var sb = new StringBuilder();
        var first = true;

        foreach (var (start, end) in ranges)
        {
            var highStart = (char)(((start - 0x10000) >> 10) + 0xD800);
            var lowStart = (char)(((start - 0x10000) & 0x3FF) + 0xDC00);
            var highEnd = (char)(((end - 0x10000) >> 10) + 0xD800);
            var lowEnd = (char)(((end - 0x10000) & 0x3FF) + 0xDC00);

            if (highStart == highEnd)
            {
                // Single high surrogate, range of low surrogates
                if (!first) sb.Append('|');
                first = false;
                sb.Append(EscapeCharClassCodeUnit(highStart));
                sb.Append('[');
                sb.Append(EscapeCharClassCodeUnit(lowStart));
                if (lowStart != lowEnd)
                {
                    sb.Append('-');
                    sb.Append(EscapeCharClassCodeUnit(lowEnd));
                }
                sb.Append(']');
            }
            else
            {
                // Multiple high surrogates
                // First partial: highStart [lowStart-\uDFFF]
                if (!first) sb.Append('|');
                first = false;
                sb.Append(EscapeCharClassCodeUnit(highStart));
                sb.Append('[');
                sb.Append(EscapeCharClassCodeUnit(lowStart));
                if (lowStart != 0xDFFF)
                {
                    sb.Append('-');
                    sb.Append(EscapeCharClassCodeUnit(0xDFFF));
                }
                sb.Append(']');

                // Middle: [highStart+1..highEnd-1] [\uDC00-\uDFFF] (full low range)
                if (highStart + 1 <= highEnd - 1)
                {
                    sb.Append('|');
                    sb.Append('[');
                    sb.Append(EscapeCharClassCodeUnit(highStart + 1));
                    if (highStart + 1 != highEnd - 1)
                    {
                        sb.Append('-');
                        sb.Append(EscapeCharClassCodeUnit(highEnd - 1));
                    }
                    sb.Append(']');
                    sb.Append('[');
                    sb.Append(EscapeCharClassCodeUnit(0xDC00));
                    sb.Append('-');
                    sb.Append(EscapeCharClassCodeUnit(0xDFFF));
                    sb.Append(']');
                }

                // Last partial: highEnd [\uDC00-lowEnd]
                sb.Append('|');
                sb.Append(EscapeCharClassCodeUnit(highEnd));
                sb.Append('[');
                sb.Append(EscapeCharClassCodeUnit(0xDC00));
                if (0xDC00 != lowEnd)
                {
                    sb.Append('-');
                    sb.Append(EscapeCharClassCodeUnit(lowEnd));
                }
                sb.Append(']');
            }
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

    private static string BuildAstralAlternation(List<(int Start, int End)> ranges)
    {
        if (ranges.Count == 0)
        {
            return string.Empty;
        }

        var sb = new StringBuilder();
        var first = true;
        foreach (var (start, end) in ranges)
        {
            for (var cp = start; cp <= end; cp++)
            {
                if (!first)
                {
                    sb.Append('|');
                }

                sb.Append("(?:");
                sb.Append(FormatAstralAsSurrogates(cp));
                sb.Append(')');
                first = false;
            }
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
