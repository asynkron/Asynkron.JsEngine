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

    private const string UnicodeDotPattern =
        @"(?<![\uD800-\uDBFF])(?:[^\n\r\u2028\u2029]|[\uD800-\uDBFF][\uDC00-\uDFFF]|[\uD800-\uDBFF](?![\uDC00-\uDFFF])|[\uDC00-\uDFFF])";

    private const string UnicodeNonWhitespacePattern =
        @"(?<![\uD800-\uDBFF])(?:[^\s]|[\uD800-\uDBFF][\uDC00-\uDFFF]|[\uD800-\uDBFF](?![\uDC00-\uDFFF])|[\uDC00-\uDFFF])";

    private readonly string _normalizedPattern;
    private readonly RegexOptions _regexOptions;

    /// <summary>
    /// Maps .NET-safe group names back to original ECMAScript group names.
    /// ECMAScript allows '$' and unicode chars in group names that .NET doesn't support.
    /// Key = sanitized name (used in .NET regex), Value = original JS name.
    /// </summary>
    private readonly Dictionary<string, string>? _groupNameMapping;

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
        var normalized = NormalizePattern(pattern, hasUnicodeFlag, IgnoreCase);
        _normalizedPattern = SanitizeGroupNamesForDotNet(normalized, out _groupNameMapping);

        // Convert JavaScript regex flags to .NET RegexOptions
        var options = RegexOptions.CultureInvariant;
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
            EnsureRegex();
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
    /// </summary>
    public bool Test(string input)
    {
        var startIndex = Global || Sticky ? GetLastIndex() : 0;
        if (startIndex > input.Length)
        {
            startIndex = 0;
        }

        var match = EnsureRegex().Match(input, startIndex);

        if (match.Success && Global)
        {
            SetLastIndex(match.Index + match.Length);
        }
        else if (!match.Success && (Global || Sticky))
        {
            SetLastIndex(0);
        }

        if (match.Success)
        {
            RealmState.UpdateRegExpStatics(input, match);
        }

        return match.Success;
    }

    /// <summary>
    ///     Executes a search for a match and returns an array with match details.
    /// </summary>
    public JsArray? Exec(string input)
    {
        var startIndex = Global || Sticky ? GetLastIndex() : 0;
        if (startIndex > input.Length)
        {
            if (Global || Sticky)
            {
                SetLastIndex(0);
            }

            return null;
        }

        var match = EnsureRegex().Match(input, startIndex);

        if (!match.Success)
        {
            if (Global || Sticky)
            {
                SetLastIndex(0);
            }

            return null;
        }

        if (Global || Sticky)
        {
            SetLastIndex(match.Index + match.Length);
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

    private JsArray CreateMatchArray(Match match, string input)
    {
        var result = new JsArray(RealmState);
        var captureValues = new JsValue[match.Groups.Count];

        // Full match + capture groups.
        for (var i = 0; i < match.Groups.Count; i++)
        {
            var group = match.Groups[i];
            captureValues[i] = group.Success ? new JsValue(group.Value) : JsValue.Undefined;
            result.Push(captureValues[i]);
        }

        // Add properties for exec-style results.
        result.SetProperty("index", (double)match.Index);
        result.SetProperty("input", input);

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

    private JsArray BuildIndicesArray(Match match)
    {
        var regex = EnsureRegex();
        var indices = new JsArray(RealmState);
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
                indices.Push(indexValues[i]);
            }
            else
            {
                indexValues[i] = JsValue.Undefined;
                indices.Push(JsValue.Undefined);
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

    private static string NormalizePattern(string pattern, bool hasUnicodeFlag, bool ignoreCase)
    {
        if (!hasUnicodeFlag)
        {
            return NormalizeLegacyPattern(pattern, ignoreCase);
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
                        builder.Append(EscapeCodeUnit(codeUnit));
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

                    if (definedSoFar.Contains(normalizedName))
                    {
                        // Backward reference: group already defined
                        builder.Append(pattern, i, end - i + 1);
                    }
                    else
                    {
                        // Forward reference: use conditional to match empty string if group not yet captured
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
                    }

                    builder.Append('\\');
                    builder.Append(numText);
                    i = end - 1;
                    continue;
                }

                if (i + 1 >= pattern.Length || IsLineTerminator(pattern[i + 1]))
                {
                    throw new ParseException("Invalid regular expression: incomplete escape.");
                }

                var next = pattern[i + 1];
                if (hasUnicodeFlag && !inCharClass && next is 'S')
                {
                    builder.Append(UnicodeNonWhitespacePattern);
                    i++;
                    continue;
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
                builder.Append(UnicodeDotPattern);
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
                if (ContainsSurrogateCodeUnit(normalizedName))
                {
                    throw new ParseException("Invalid regular expression: invalid group name.");
                }

                definedSoFar.Add(normalizedName);
                builder.Append(pattern, i, end - i + 1);
                i = end;
                continue;
            }

            if (!inCharClass && c == '(')
            {
                // Increment capture count for plain capturing groups
                if (!(i + 1 < pattern.Length && pattern[i + 1] == '?'))
                {
                    captureCount++;
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

    private static string NormalizeLegacyPattern(string pattern, bool ignoreCase)
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
                                    if (definedSoFar.Contains(normalizedName))
                                    {
                                        // Backward reference: group already defined
                                        builder.Append('\\');
                                        builder.Append('k');
                                        builder.Append('<');
                                        builder.Append(normalizedName);
                                        builder.Append('>');
                                    }
                                    else
                                    {
                                        // Forward reference: use conditional to match empty string if group not yet captured
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
                            if (value <= captureCount)
                            {
                                builder.Append('\\');
                                builder.Append(numText);
                            }
                            else
                            {
                                // Forward reference behaves like an empty string in JS.
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
                var hasQuestion = i + 1 < pattern.Length && pattern[i + 1] == '?';
                var isNamedCapture = hasQuestion && i + 2 < pattern.Length && pattern[i + 2] == '<' &&
                                     (i + 3 >= pattern.Length || (pattern[i + 3] != '=' && pattern[i + 3] != '!'));

                if (!hasQuestion || isNamedCapture)
                {
                    captureCount++;
                }

                // Track named groups as they are defined
                if (isNamedCapture)
                {
                    var end = pattern.IndexOf('>', i + 3);
                    if (end != -1)
                    {
                        var name = pattern.Substring(i + 3, end - (i + 3));
                        var normalizedName = NormalizeGroupNameToken(name);
                        definedSoFar.Add(normalizedName);
                    }
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
                        throw new ParseException("Invalid regular expression: invalid group name.");
                    }
                }
            }
        }

        foreach (var ch in rawName)
        {
            if (char.IsSurrogate(ch))
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

        // Quick check: if no '$' in the pattern, no sanitization needed
        if (!pattern.Contains('$'))
        {
            return pattern;
        }

        var result = new StringBuilder(pattern.Length);
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
                        var sanitized = SanitizeGroupName(name);
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

            // Check for backreference: \k<name>
            if (c == '\\' && i + 1 < pattern.Length && pattern[i + 1] == 'k'
                && i + 2 < pattern.Length && pattern[i + 2] == '<')
            {
                var end = pattern.IndexOf('>', i + 3);
                if (end != -1)
                {
                    var name = pattern.Substring(i + 3, end - (i + 3));
                    if (NeedsGroupNameSanitization(name))
                    {
                        var sanitized = SanitizeGroupName(name);
                        result.Append("\\k<");
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
                        var sanitized = SanitizeGroupName(name);
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
    /// </summary>
    private static bool NeedsGroupNameSanitization(string name)
    {
        foreach (var ch in name)
        {
            if (ch == '$')
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Replaces characters in a group name that .NET regex doesn't support.
    /// </summary>
    private static string SanitizeGroupName(string name)
    {
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

    private static bool ContainsSurrogateCodeUnit(string value)
    {
        foreach (var ch in value)
        {
            if (char.IsSurrogate(ch))
            {
                return true;
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

                if (code is >= 0xD800 and <= 0xDFFF)
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
        return $"(?:(?!{disallowed}){AnyCodePointPattern})";
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
        return $"(?:(?!{disallowed}){AnyCodePointPattern})";
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
