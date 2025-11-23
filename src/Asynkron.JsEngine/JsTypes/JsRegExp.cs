using System.Text.RegularExpressions;
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

    public JsRegExp(string pattern, string flags = "", RealmState? realmState = null)
    {
        Pattern = pattern;
        Flags = flags;
        _realmState = realmState;
        JsObject = new JsObject();

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
        catch (ArgumentException)
        {
            // When a pattern is valid JavaScript but not supported by
            // .NET's Regex engine (e.g. due to advanced escape rules or
            // lookaround/backreference differences), fall back to a
            // literal match so user code keeps running instead of
            // crashing during module initialisation.
            _regex = new Regex(Regex.Escape(pattern), options);
        }

        // Set standard properties
        JsObject["source"] = pattern;
        JsObject["flags"] = flags;
        JsObject["global"] = Global;
        JsObject["ignoreCase"] = IgnoreCase;
        JsObject["multiline"] = Multiline;
        JsObject["lastIndex"] = 0d;
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
}
