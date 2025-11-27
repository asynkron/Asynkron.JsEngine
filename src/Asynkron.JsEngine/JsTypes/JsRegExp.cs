using System.Buffers;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Asynkron.JsEngine.Parser;
using Asynkron.JsEngine.Runtime;
using Asynkron.JsEngine.StdLib;

namespace Asynkron.JsEngine.JsTypes;

/// <summary>
///     Represents a JavaScript regular expression object.
/// </summary>
public class JsRegExp
{
    private readonly RealmState? _realmState;
    private readonly Regex _regex;
    private readonly string _normalizedPattern;

    public JsRegExp(string pattern, string flags = "", RealmState? realmState = null)
    {
        Pattern = pattern;
        Flags = flags ?? string.Empty;
        _realmState = realmState;
        JsObject = new JsObject();

        ValidateFlags(Flags);
        var hasUnicodeFlag = Flags.Contains('u');
        _normalizedPattern = NormalizePattern(pattern, hasUnicodeFlag, IgnoreCase);

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

        _regex = new Regex(_normalizedPattern, options);

        // Set standard properties
        JsObject["source"] = pattern;
        JsObject["flags"] = flags;
        JsObject["global"] = Global;
        JsObject["ignoreCase"] = IgnoreCase;
        JsObject["multiline"] = Multiline;
        JsObject.DefineProperty("lastIndex",
            new PropertyDescriptor
            {
                Value = 0d, Writable = true, Enumerable = false, Configurable = false
            });
    }

    public string Pattern { get; }

    public string Flags { get; }

    public bool Global => Flags.Contains('g');
    public bool IgnoreCase => Flags.Contains('i');
    public bool Multiline => Flags.Contains('m');
    public int LastIndex { get; set; }

    public JsObject JsObject { get; }

    public void SetProperty(string name, object? value, object? receiver)
    {
        JsObject.SetProperty(name, value, receiver ?? JsObject);
    }

    public void SetProperty(string name, object? value)
    {
        SetProperty(name, value, JsObject);
    }

    /// <summary>
    ///     Tests if the pattern matches the input string.
    /// </summary>
    public bool Test(string input)
    {
        if (input == null)
        {
            return false;
        }

        var startIndex = Global && LastIndex > 0 ? LastIndex : 0;
        if (startIndex > input.Length)
        {
            startIndex = 0;
        }

        var match = _regex.Match(input, startIndex);

        if (match.Success && Global)
        {
            LastIndex = match.Index + match.Length;
            JsObject["lastIndex"] = (double)LastIndex;
        }
        else if (!match.Success && Global)
        {
            LastIndex = 0;
            JsObject["lastIndex"] = 0d;
        }

        if (match.Success)
        {
            StandardLibrary.UpdateRegExpStatics(_realmState, input, match);
        }

        return match.Success;
    }

    /// <summary>
    ///     Executes a search for a match and returns an array with match details.
    /// </summary>
    public object? Exec(string input)
    {
        if (input == null)
        {
            return null;
        }

        var startIndex = Global && LastIndex > 0 ? LastIndex : 0;
        if (startIndex > input.Length)
        {
            if (Global)
            {
                LastIndex = 0;
                JsObject["lastIndex"] = 0d;
            }

            return null;
        }

        var match = _regex.Match(input, startIndex);

        if (!match.Success)
        {
            if (Global)
            {
                LastIndex = 0;
                JsObject["lastIndex"] = 0d;
            }

            return null;
        }

        if (Global)
        {
            LastIndex = match.Index + match.Length;
            JsObject["lastIndex"] = (double)LastIndex;
        }

        // Build result array
        var result = new JsArray(_realmState);
        result.Push(match.Value); // Full match at index 0

        // Add capture groups
        for (var i = 1; i < match.Groups.Count; i++)
        {
            var group = match.Groups[i];
            result.Push(group.Success ? group.Value : null);
        }

        // Add properties
        result.SetProperty("index", (double)match.Index);
        result.SetProperty("input", input);

        StandardLibrary.AddArrayMethods(result, _realmState);
        StandardLibrary.UpdateRegExpStatics(_realmState, input, match);
        return result;
    }

    /// <summary>
    ///     Finds all matches in the input string.
    /// </summary>
    internal JsArray MatchAll(string input)
    {
        if (input == null)
        {
            return new JsArray(_realmState);
        }

        var result = new JsArray(_realmState);
        var matches = _regex.Matches(input);

        foreach (Match match in matches)
        {
            var matchArray = new JsArray(_realmState);
            matchArray.Push(match.Value);

            for (var i = 1; i < match.Groups.Count; i++)
            {
                var group = match.Groups[i];
                matchArray.Push(group.Success ? group.Value : null);
            }

            matchArray.SetProperty("index", (double)match.Index);
            matchArray.SetProperty("input", input);
            StandardLibrary.AddArrayMethods(matchArray, _realmState);

            result.Push(matchArray);
        }

        StandardLibrary.AddArrayMethods(result, _realmState);
        return result;
    }

    private static void ValidateFlags(string flags)
    {
        var seen = new HashSet<char>();
        foreach (var flag in flags)
        {
            if (!seen.Add(flag))
            {
                throw new ParseException($"Invalid regular expression flags: duplicate '{flag}'.");
            }

            if (flag is not ('g' or 'i' or 'm' or 'u' or 'y' or 's' or 'd'))
            {
                throw new ParseException($"Invalid regular expression flag '{flag}'.");
            }
        }
    }

    private static string NormalizePattern(string pattern, bool hasUnicodeFlag, bool ignoreCase)
    {
        if (!hasUnicodeFlag)
        {
            ValidateLegacyPattern(pattern);
            return pattern ?? string.Empty;
        }

        if (string.IsNullOrEmpty(pattern))
        {
            return pattern ?? string.Empty;
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
                        builder.Append(pattern, i, end - i + 1);
                    }
                    else
                    {
                        // Forward reference behaves like an empty string in JS.
                        builder.Append("(?:)");
                    }

                    definedSoFar.Add(normalizedName);
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

    private static void ValidateLegacyPattern(string pattern)
    {
        if (string.IsNullOrEmpty(pattern))
        {
            return;
        }

        var captureCount = 0;
        var inCharClass = false;
        var escaped = false;

        // First pass: count capturing groups and catch trailing escape
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

            if (c == '[')
            {
                inCharClass = true;
                continue;
            }

            if (c == ']' && inCharClass)
            {
                inCharClass = false;
                continue;
            }

            if (!inCharClass && c == '(' && !(i + 1 < pattern.Length && pattern[i + 1] == '?'))
            {
                captureCount++;
            }
        }

        if (escaped)
        {
            throw new ParseException("Invalid regular expression: incomplete escape.");
        }

        // Second pass: validate escapes/backreferences/quantifiers
        inCharClass = false;
        escaped = false;
        for (var i = 0; i < pattern.Length; i++)
        {
            var c = pattern[i];
            if (escaped)
            {
                escaped = false;
                switch (c)
                {
                    case 'x':
                        if (i + 2 >= pattern.Length || !IsHexDigit(pattern[i + 1]) || !IsHexDigit(pattern[i + 2]))
                        {
                            throw new ParseException("Invalid regular expression: incomplete hex escape.");
                        }

                        i += 2;
                        break;
                    case 'u':
                        if (i + 4 >= pattern.Length ||
                            !IsHexDigit(pattern[i + 1]) ||
                            !IsHexDigit(pattern[i + 2]) ||
                            !IsHexDigit(pattern[i + 3]) ||
                            !IsHexDigit(pattern[i + 4]))
                        {
                            throw new ParseException("Invalid regular expression: incomplete unicode escape.");
                        }

                        i += 4;
                        break;
                    case 'c':
                        if (i + 1 >= pattern.Length || !IsAsciiLetter(pattern[i + 1]))
                        {
                            throw new ParseException("Invalid regular expression: invalid control escape.");
                        }

                        i += 1;
                        break;
                    default:
                        if (!inCharClass && char.IsDigit(c))
                        {
                            var start = i;
                            var end = i;
                            while (end < pattern.Length && char.IsDigit(pattern[end]))
                            {
                                end++;
                            }

                            var numText = pattern[start..end];
                            if (numText.Length > 0 && numText[0] != '0' &&
                                int.TryParse(numText, NumberStyles.None, CultureInfo.InvariantCulture, out var backref))
                            {
                                if (backref == 0 || backref > captureCount)
                                {
                                    throw new ParseException("Invalid regular expression: invalid backreference.");
                                }
                            }

                            i = end - 1;
                        }

                        break;
                }

                continue;
            }

            if (c == '\\')
            {
                escaped = true;
                continue;
            }

            if (c == '[')
            {
                inCharClass = true;
                continue;
            }

            if (c == ']' && inCharClass)
            {
                inCharClass = false;
                continue;
            }

            if (!inCharClass && c == '{')
            {
                if (i + 1 >= pattern.Length || !char.IsDigit(pattern[i + 1]))
                {
                    throw new ParseException("Invalid regular expression: incomplete quantifier.");
                }
            }
        }
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

                    if (codePoint is < 0 or > 0x10FFFF || (codePoint >= 0xD800 && codePoint <= 0xDFFF))
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

                if (code >= 0xD800 && code <= 0xDFFF)
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

    private static bool IsAsciiLetter(char c)
    {
        return (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z');
    }

    private static bool IsLineTerminator(char c)
    {
        return c is '\n' or '\r' or '\u2028' or '\u2029';
    }

    private static string EscapeLiteralRune(Rune rune)
    {
        if (rune.Value == 0)
        {
            return "\\x00";
        }

        var text = rune.ToString();
        return Regex.Escape(text);
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
            builder.Append(asLiteral ? Regex.Escape(text) : text);
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

    private const string AnyCodePointPattern =
        @"(?<![\uD800-\uDBFF])(?:[\uD800-\uDBFF][\uDC00-\uDFFF]|[\u0000-\uD7FF\uE000-\uFFFF]|[\uD800-\uDBFF](?![\uDC00-\uDFFF])|[\uDC00-\uDFFF])";

    private const string UnicodeDotPattern =
        @"(?<![\uD800-\uDBFF])(?:[^\n\r\u2028\u2029]|[\uD800-\uDBFF][\uDC00-\uDFFF]|[\uD800-\uDBFF](?![\uDC00-\uDFFF])|[\uDC00-\uDFFF])";

    private const string UnicodeNonWhitespacePattern =
        @"(?<![\uD800-\uDBFF])(?:[^\s]|[\uD800-\uDBFF][\uDC00-\uDFFF]|[\uD800-\uDBFF](?![\uDC00-\uDFFF])|[\uDC00-\uDFFF])";
}
