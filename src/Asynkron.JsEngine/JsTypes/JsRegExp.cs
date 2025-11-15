using System.Text.RegularExpressions;

namespace Asynkron.JsEngine.JsTypes;

/// <summary>
/// Represents a JavaScript regular expression object.
/// </summary>
public class JsRegExp
{
    private readonly Regex _regex;
    private readonly string _pattern;
    private readonly string _flags;
    private readonly JsObject _jsObject;

    public string Pattern => _pattern;
    public string Flags => _flags;
    public bool Global => _flags.Contains('g');
    public bool IgnoreCase => _flags.Contains('i');
    public bool Multiline => _flags.Contains('m');
    public int LastIndex { get; set; }

    public JsObject JsObject => _jsObject;

    public JsRegExp(string pattern, string flags = "")
    {
        _pattern = pattern;
        _flags = flags;
        _jsObject = new JsObject();

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
            _regex = new Regex(pattern, options);
        }
        catch (ArgumentException ex)
        {
            throw new InvalidOperationException($"Invalid regular expression: {ex.Message}", ex);
        }

        // Set standard properties
        _jsObject["source"] = pattern;
        _jsObject["flags"] = flags;
        _jsObject["global"] = Global;
        _jsObject["ignoreCase"] = IgnoreCase;
        _jsObject["multiline"] = Multiline;
        _jsObject["lastIndex"] = 0d;
    }

    public void SetProperty(string name, object? value)
    {
        _jsObject.SetProperty(name, value);
    }

    /// <summary>
    /// Tests if the pattern matches the input string.
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
            _jsObject["lastIndex"] = (double)LastIndex;
        }
        else if (!match.Success && Global)
        {
            LastIndex = 0;
            _jsObject["lastIndex"] = 0d;
        }

        return match.Success;
    }

    /// <summary>
    /// Executes a search for a match and returns an array with match details.
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
                _jsObject["lastIndex"] = 0d;
            }

            return null;
        }

        var match = _regex.Match(input, startIndex);

        if (!match.Success)
        {
            if (Global)
            {
                LastIndex = 0;
                _jsObject["lastIndex"] = 0d;
            }

            return null;
        }

        if (Global)
        {
            LastIndex = match.Index + match.Length;
            _jsObject["lastIndex"] = (double)LastIndex;
        }

        // Build result array
        var result = new JsArray();
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

        StandardLibrary.AddArrayMethods(result);
        return result;
    }

    /// <summary>
    /// Finds all matches in the input string.
    /// </summary>
    internal JsArray MatchAll(string input)
    {
        if (input == null)
        {
            return new JsArray();
        }

        var result = new JsArray();
        var matches = _regex.Matches(input);

        foreach (Match match in matches)
        {
            var matchArray = new JsArray();
            matchArray.Push(match.Value);

            for (var i = 1; i < match.Groups.Count; i++)
            {
                var group = match.Groups[i];
                matchArray.Push(group.Success ? group.Value : null);
            }

            matchArray.SetProperty("index", (double)match.Index);
            matchArray.SetProperty("input", input);
            StandardLibrary.AddArrayMethods(matchArray);

            result.Push(matchArray);
        }

        StandardLibrary.AddArrayMethods(result);
        return result;
    }
}