using System.Buffers;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Asynkron.JsEngine.Runtime;
using Asynkron.JsEngine.StdLib;
using Asynkron.JsEngine.Parser;
using Asynkron.JsEngine.Ast;

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
        _normalizedPattern = NormalizePattern(pattern, Flags.Contains('u'));

        // Convert JavaScript regex flags to .NET RegexOptions
        var options = RegexOptions.None;
        if (IgnoreCase)
        {
            options |= RegexOptions.IgnoreCase;
        }

        if (Multiline)
        {
            options |= RegexOptions.Multiline;
        }

        try
        {
            _regex = new Regex(_normalizedPattern, options);
        }
        catch (ArgumentException)
        {
            throw;
        }

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

    public void SetProperty(string name, object? value)
    {
        JsObject.SetProperty(name, value);
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

    private static string NormalizePattern(string pattern, bool hasUnicodeFlag)
    {
        if (string.IsNullOrEmpty(pattern))
        {
            return pattern ?? string.Empty;
        }

        var allGroupNames = CollectGroupNames(pattern);
        var definedSoFar = new HashSet<string>();
        var builder = new StringBuilder();
        var inCharClass = false;
        var escaped = false;

        for (var i = 0; i < pattern.Length; i++)
        {
            var c = pattern[i];
            if (escaped)
            {
                builder.Append(c);
                escaped = false;
                continue;
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

                    var rune = new Rune(codePoint);
                    builder.Append(EscapeLiteralRune(rune));
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
                                var rune = new Rune(cp);
                                builder.Append(EscapeLiteralRune(rune));
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

                    if (char.IsSurrogate((char)codeUnit))
                    {
                        builder.Append(EscapeCodeUnit(codeUnit));
                    }
                    else
                    {
                        var rune = new Rune(codeUnit);
                        builder.Append(EscapeLiteralRune(rune));
                    }
                    i += 5;
                    continue;
                }

                if (!inCharClass && i + 1 < pattern.Length && pattern[i + 1] == 'x' && i + 3 < pattern.Length &&
                    IsHexDigit(pattern[i + 2]) && IsHexDigit(pattern[i + 3]))
                {
                    var hexDigits = pattern.Substring(i + 2, 2);
                    var codeUnit = int.Parse(hexDigits, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
                    if (char.IsSurrogate((char)codeUnit))
                    {
                        builder.Append(EscapeCodeUnit(codeUnit));
                    }
                    else
                    {
                        var rune = new Rune(codeUnit);
                        builder.Append(EscapeLiteralRune(rune));
                    }
                    i += 3;
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

                if (i + 1 >= pattern.Length || IsLineTerminator(pattern[i + 1]))
                {
                    throw new ParseException("Invalid regular expression: incomplete escape.");
                }

                var next = pattern[i + 1];
                var codeUnitLiteral = (int)next;
                if (char.IsSurrogate(next))
                {
                    builder.Append(EscapeCodeUnit(codeUnitLiteral));
                }
                else
                {
                    var literalRune = new Rune(codeUnitLiteral);
                    builder.Append(EscapeLiteralRune(literalRune));
                }
                i++; // skip escaped character
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

            if (c == '[')
            {
                inCharClass = true;
                builder.Append(c);
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
                definedSoFar.Add(normalizedName);
                builder.Append(pattern, i, end - i + 1);
                i = end;
                continue;
            }

            builder.Append(c);
        }

        return builder.ToString();
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

    private static string NormalizeGroupNameToken(string rawName)
    {
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
}
