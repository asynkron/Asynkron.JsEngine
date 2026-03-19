#region

using System.Buffers;
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
    private const string AnyCodePointPattern =
        @"(?<![\uD800-\uDBFF])(?:[\uD800-\uDBFF][\uDC00-\uDFFF]|[\u0000-\uD7FF\uE000-\uFFFF]|[\uD800-\uDBFF](?![\uDC00-\uDFFF])|[\uDC00-\uDFFF])";

    // Unicode dot: matches a full code point (surrogate pair first, then BMP non-line-terminator).
    // Surrogate pair must be tried first to avoid matching only the high surrogate.
    private const string UnicodeDotPattern =
        @"(?:[\uD800-\uDBFF][\uDC00-\uDFFF]|[^\n\r\u2028\u2029\uD800-\uDFFF])";

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
    /// Maps original JS group name → list of deduplicated .NET group names.
    /// Only set when ES2025 duplicate named groups exist in the pattern.
    /// E.g., for /(?&lt;x&gt;a)|(?&lt;x&gt;b)/, maps "x" → ["x__dup0", "x__dup1"].
    /// </summary>
    private readonly Dictionary<string, List<string>>? _duplicateGroupNames;

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
        var normalized = NormalizePattern(pattern, hasUnicodeFlag, IgnoreCase, DotAll);
        _normalizedPattern = SanitizeGroupNamesForDotNet(normalized, out _groupNameMapping);

        // ES2025: Handle duplicate named groups in alternatives.
        // .NET merges duplicate named groups into one, but JS treats them as separate capturing groups.
        // Rename duplicates (e.g., (?<x>a)|(?<x>b) → (?<x__dup0>a)|(?<x__dup1>b)) and convert
        // backreferences to conditional patterns.
        _normalizedPattern = DeduplicateGroupNames(_normalizedPattern, ref _groupNameMapping,
            out _duplicateGroupNames);

        // Convert JavaScript regex flags to .NET RegexOptions
        // ECMAScript mode is critical for correct JS semantics:
        //   - Groups are reset on each quantifier iteration (JS behavior)
        //   - Backreferences to uncaptured groups match empty string (JS behavior)
        //   - \w, \d, \s use ASCII-only ranges (matches JS; patterns already normalized anyway)
        var options = RegexOptions.CultureInvariant | RegexOptions.ECMAScript;
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
        return _compiledRegex ??= new Regex(_normalizedPattern, _regexOptions);
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

        // Track which original names we've already added (for deduped groups)
        HashSet<string>? addedOriginals = _duplicateGroupNames is not null
            ? new HashSet<string>(StringComparer.Ordinal)
            : null;

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

            // Map back from .NET-sanitized/deduped name to the original ECMAScript group name
            var originalName = GetOriginalGroupName(name);

            // For deduplicated groups, we need to find the value from whichever variant captured
            if (_duplicateGroupNames is not null &&
                _duplicateGroupNames.TryGetValue(originalName, out var variants))
            {
                if (addedOriginals!.Contains(originalName))
                {
                    continue; // Already added this original name
                }

                addedOriginals.Add(originalName);

                // Find the first variant that actually captured (Success = true)
                var value = JsValue.Undefined;
                foreach (var variant in variants)
                {
                    var variantGroup = match.Groups[variant];
                    if (variantGroup.Success)
                    {
                        value = new JsValue(variantGroup.Value);
                        break;
                    }
                }

                groups ??= CreateNullPrototypeObject();
                groups.DefineProperty(originalName, new PropertyDescriptor
                {
                    Value = value,
                    Writable = true,
                    Enumerable = true,
                    Configurable = true
                });
                continue;
            }

            groups ??= CreateNullPrototypeObject();

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

    private static JsObject CreateNullPrototypeObject()
    {
        var obj = new JsObject();
        obj.SetPrototype(null);
        return obj;
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

        HashSet<string>? addedOriginals = _duplicateGroupNames is not null
            ? new HashSet<string>(StringComparer.Ordinal)
            : null;

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

            var originalName = GetOriginalGroupName(name);

            // For deduplicated groups, find the value from whichever variant captured
            if (_duplicateGroupNames is not null &&
                _duplicateGroupNames.TryGetValue(originalName, out var variants))
            {
                if (addedOriginals!.Contains(originalName))
                {
                    continue;
                }

                addedOriginals.Add(originalName);

                var value = JsValue.Undefined;
                foreach (var variant in variants)
                {
                    var variantNumber = regex.GroupNumberFromName(variant);
                    if (variantNumber >= 0 && variantNumber < indexValues.Length &&
                        !indexValues[variantNumber].IsUndefined)
                    {
                        value = indexValues[variantNumber];
                        break;
                    }
                }

                groups ??= CreateNullPrototypeObject();
                groups.DefineProperty(originalName, new PropertyDescriptor
                {
                    Value = value,
                    Writable = true,
                    Enumerable = true,
                    Configurable = true
                });
                continue;
            }

            groups ??= CreateNullPrototypeObject();

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

    private static string NormalizePattern(string pattern, bool hasUnicodeFlag, bool ignoreCase, bool dotAll)
    {
        if (!hasUnicodeFlag)
        {
            return NormalizeLegacyPattern(pattern, ignoreCase, dotAll);
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

                    if (definedSoFar.Contains(normalizedName) && !openGroupNames.Contains(normalizedName))
                    {
                        // Backward reference: group already defined and closed
                        builder.Append(pattern, i, end - i + 1);
                    }
                    else
                    {
                        // Forward reference or self-reference to an open group:
                        // use conditional to match empty string if group not yet captured.
                        // (?(name)\k<name>|) - if group captured use backreference, else match empty
                        builder.Append("(?(");
                        builder.Append(normalizedName);
                        builder.Append(")\\k<");
                        builder.Append(normalizedName);
                        builder.Append(">|)");
                    }

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
                // Replace \s, \S, \w, \W, \d, \D with ECMAScript-accurate definitions.
                if (!inCharClass)
                {
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
                            builder.Append(EcmaWordClass);
                            i++;
                            continue;
                        case 'W':
                            builder.Append(UnicodeEcmaNonWordPattern);
                            i++;
                            continue;
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
                var normalized = NormalizeUnicodeCharacterClass(pattern, ref i);
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
                builder.Append(dotAll ? AnyCodePointPattern : UnicodeDotPattern);
                continue;
            }

            // Named capturing group (?<name>...)
            if (c == '(' && i + 2 < pattern.Length && pattern[i + 1] == '?' && pattern[i + 2] == '<')
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
                // Emit (?<normalizedName> — use decoded name since .NET doesn't
                // understand \u escapes in group names
                builder.Append("(?<");
                builder.Append(normalizedName);
                builder.Append('>');
                i = end;
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

                openGroupNames.Push(null);
            }

            if (!inCharClass && c == ')' && groupDepth > 0)
            {
                groupDepth--;
                if (openGroupNames.Count > 0)
                {
                    openGroupNames.Pop();
                }
            }

            if (!inCharClass && c == '{')
            {
                if (i + 1 >= pattern.Length || !char.IsDigit(pattern[i + 1]))
                {
                    throw new ParseException("Invalid regular expression: incomplete quantifier.");
                }
            }

            AppendCodePoint(builder, c, hasUnicodeFlag, ignoreCase, false);
        }

        return builder.ToString();
    }

    private static string NormalizeLegacyPattern(string pattern, bool ignoreCase, bool dotAll)
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
                                    if (definedSoFar.Contains(normalizedName) && !openGroupNames.Contains(normalizedName))
                                    {
                                        // Backward reference: group already defined and closed
                                        builder.Append('\\');
                                        builder.Append('k');
                                        builder.Append('<');
                                        builder.Append(normalizedName);
                                        builder.Append('>');
                                    }
                                    else
                                    {
                                        // Forward reference or self-reference to an open group:
                                        // use conditional to match empty string if group not yet captured.
                                        // (?(name)\k<name>|) - if group captured use backreference, else match empty
                                        builder.Append("(?(");
                                        builder.Append(normalizedName);
                                        builder.Append(")\\k<");
                                        builder.Append(normalizedName);
                                        builder.Append(">|)");
                                    }

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
                builder.Append(dotAll ? LegacyDotAllPattern : LegacyDotPattern);
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
    /// ES2025: Deduplicates named groups in alternatives so that .NET treats each as a separate
    /// capturing group. E.g., (?&lt;x&gt;a)|(?&lt;x&gt;b) → (?&lt;x__dup0&gt;a)|(?&lt;x__dup1&gt;b).
    /// Backreferences \k&lt;x&gt; are converted to conditional patterns that try each variant.
    /// </summary>
    private static string DeduplicateGroupNames(
        string pattern,
        ref Dictionary<string, string>? mapping,
        out Dictionary<string, List<string>>? duplicateNames)
    {
        duplicateNames = null;

        // First pass: collect all named groups and their occurrence counts
        var nameCount = new Dictionary<string, int>(StringComparer.Ordinal);
        ScanNamedGroups(pattern, nameCount);

        // Check if any names are duplicated
        var hasDuplicates = false;
        foreach (var count in nameCount.Values)
        {
            if (count > 1)
            {
                hasDuplicates = true;
                break;
            }
        }

        if (!hasDuplicates)
        {
            return pattern;
        }

        // Pre-compute all deduplicated names
        duplicateNames = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var kvp in nameCount)
        {
            if (kvp.Value > 1)
            {
                var names = new List<string>(kvp.Value);
                for (var j = 0; j < kvp.Value; j++)
                {
                    names.Add($"{kvp.Key}__dup{j.ToString(CultureInfo.InvariantCulture)}");
                }

                duplicateNames[kvp.Key] = names;
            }
        }

        // Second pass: rename groups and convert backreferences
        var result = new StringBuilder(pattern.Length + 64);
        var dupCounters = new Dictionary<string, int>(StringComparer.Ordinal);
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
                // Check for backreference \k<name>
                if (!inCharClass && i + 2 < pattern.Length && pattern[i + 1] == 'k' && pattern[i + 2] == '<')
                {
                    var end = pattern.IndexOf('>', i + 3);
                    if (end != -1)
                    {
                        var name = pattern.Substring(i + 3, end - (i + 3));
                        if (duplicateNames.TryGetValue(name, out var variants))
                        {
                            // Convert \k<x> to conditional: (?(x__dup0)\k<x__dup0>|(?(x__dup1)\k<x__dup1>|))
                            result.Append(BuildConditionalBackreference(variants));
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

            // Check for named group (?<name>...) but not lookbehind (?<= or (?<!
            if (c == '(' && i + 2 < pattern.Length && pattern[i + 1] == '?' && pattern[i + 2] == '<'
                && i + 3 < pattern.Length && pattern[i + 3] != '=' && pattern[i + 3] != '!')
            {
                var end = pattern.IndexOf('>', i + 3);
                if (end != -1)
                {
                    var name = pattern.Substring(i + 3, end - (i + 3));
                    if (duplicateNames.ContainsKey(name))
                    {
                        if (!dupCounters.TryGetValue(name, out var idx))
                        {
                            idx = 0;
                        }

                        dupCounters[name] = idx + 1;
                        var dedupedName = duplicateNames[name][idx];

                        // Add deduped name → original name mapping
                        mapping ??= new Dictionary<string, string>(StringComparer.Ordinal);
                        mapping[dedupedName] = mapping.TryGetValue(name, out var origName) ? origName : name;

                        result.Append("(?<");
                        result.Append(dedupedName);
                        result.Append('>');
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
    /// Scans a pattern to count occurrences of each named group.
    /// </summary>
    private static void ScanNamedGroups(string pattern, Dictionary<string, int> nameCount)
    {
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

            if (c == '(' && i + 2 < pattern.Length && pattern[i + 1] == '?' && pattern[i + 2] == '<'
                && i + 3 < pattern.Length && pattern[i + 3] != '=' && pattern[i + 3] != '!')
            {
                var end = pattern.IndexOf('>', i + 3);
                if (end != -1)
                {
                    var name = pattern.Substring(i + 3, end - (i + 3));
                    if (nameCount.ContainsKey(name))
                    {
                        nameCount[name]++;
                    }
                    else
                    {
                        nameCount[name] = 1;
                    }

                    i = end + 1;
                    continue;
                }
            }

            i++;
        }
    }

    /// <summary>
    /// Builds a .NET conditional backreference expression that tries each variant in order,
    /// falling back to empty match if none captured.
    /// E.g., for ["x__dup0", "x__dup1"]: (?(x__dup0)\k&lt;x__dup0&gt;|(?(x__dup1)\k&lt;x__dup1&gt;|))
    /// </summary>
    private static string BuildConditionalBackreference(List<string> variants)
    {
        // Build nested conditional: (?(v0)\k<v0>|(?(v1)\k<v1>|))
        var sb = new StringBuilder();
        for (var i = 0; i < variants.Count; i++)
        {
            sb.Append("(?(");
            sb.Append(variants[i]);
            sb.Append(")\\k<");
            sb.Append(variants[i]);
            sb.Append(">|");
        }

        // Empty alternative at innermost level (matches empty string when none captured)
        sb.Append(')');
        // Close all the conditionals
        for (var i = 0; i < variants.Count - 1; i++)
        {
            sb.Append(')');
        }

        return sb.ToString();
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

    private static string NormalizeUnicodeCharacterClass(string pattern, ref int index)
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

        // Split into BMP and astral ranges
        var bmpRanges = new List<(int Start, int End)>();
        var astralRanges = new List<(int Start, int End)>();

        foreach (var (start, end) in ranges)
        {
            if (end <= 0xFFFF)
            {
                // Entirely BMP — but exclude surrogates (0xD800-0xDFFF) from the range
                if (start <= 0xD7FF && end >= 0xD800)
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
                if (start <= 0xD7FF)
                    bmpRanges.Add((start, 0xD7FF));
                if (0xE000 <= 0xFFFF)
                    bmpRanges.Add((Math.Max(start, 0xE000), 0xFFFF));
                astralRanges.Add((0x10000, end));
            }
        }

        var bmpContent = BuildBmpClassContent(bmpRanges);
        var astralContent = BuildSurrogatePairRanges(astralRanges);

        if (!negate)
        {
            if (astralContent.Length == 0)
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
            }

            sb.Append(')');
            return sb.ToString();
        }

        // Negated: match any code point NOT in the set
        var disallowed = new StringBuilder();
        disallowed.Append("(?:");
        var needsSeparator = false;
        if (bmpContent.Length > 0)
        {
            disallowed.Append('[');
            disallowed.Append(bmpContent);
            disallowed.Append(']');
            needsSeparator = true;
        }

        if (astralContent.Length > 0)
        {
            if (needsSeparator)
                disallowed.Append('|');
            disallowed.Append(astralContent);
        }

        disallowed.Append(')');
        return $"(?>(?!{disallowed}){AnyCodePointPattern})";
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
        var astralContent = BuildAstralAlternation(astralRanges);

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

        var disallowed = new StringBuilder();
        disallowed.Append("(?:");
        var needsSeparator = false;
        if (bmpContent.Length > 0)
        {
            disallowed.Append('[');
            disallowed.Append(bmpContent);
            disallowed.Append(']');
            needsSeparator = true;
        }

        if (astralContent.Length > 0)
        {
            if (needsSeparator)
            {
                disallowed.Append('|');
            }

            disallowed.Append(astralContent);
        }

        disallowed.Append(')');
        return $"(?>(?!{disallowed}){AnyCodePointPattern})";
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

        index += 2;
        return escape;
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
